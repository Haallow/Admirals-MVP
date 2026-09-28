using System.Collections.Generic;
using UnityEngine;

// Decides where an AI ship should move, and -- if a good attack looks
// possible from the destination -- which known enemy it's lining up on.
// Movement never fires a weapon itself; AIController still does that
// separately during the Battle phase. Ported from an older FE-style
// prototype's greedy tile-scoring approach (kill bonus, danger-at-tile,
// flee-to-safety), adapted to this game's reachable-cell pathfinding,
// multi-cell footprints, and fog-gated (known-enemies-only) reasoning.
public static class AIMovementPlanner
{
    public struct Decision
    {
        public Vector2Int destination;
        public ShipInstance likelyTarget; // informational only; null if just repositioning
        public bool isFleeing;
    }

    // How low (as a fraction of max health) a ship can drop before it
    // retreats instead of advancing, absent a guaranteed kill. Kept as a
    // constant here rather than a ShipInstance field -- ship stats aren't
    // part of this codebase area yet.
    private const float FleeHealthFraction = 0.3f;

    // Score weights for ScoreAttackFromTile, kept on a comparable 0-50
    // scale so no single factor silently swamps the others.
    //
    // CHANGE (AI Optimization Recommendations, item 2.2): the original
    // weights were ported from an FE-style prototype where unit health sat
    // around 20-40 HP. This game's ships run 400-2000 HP, so raw-HP-delta
    // terms like "Max(0, 20 - currentHealth)" were dead code almost all of
    // the time, and the danger penalty (raw expected incoming damage,
    // typically in the hundreds) completely swamped the 50-point kill
    // bonus despite the "kill bonus dominates everything else" comment.
    // Every term below is now expressed as a *fraction* of a ship's own
    // max health instead of a raw HP number, so the relative weights below
    // actually hold regardless of which ship class is involved.
    private const float KillBonus = 50f;
    private const float MaxDamageScore = 40f;
    private const float TargetWeaknessWeight = 10f;
    private const float DangerWeight = 35f;
    private const float SelfRiskWeight = 15f;

    // CHANGE (AI Optimization Recommendations, item 2.3): soft penalty
    // applied when scoring a non-lethal hit on a target one of this ship's
    // siblings has already lined up on this same Move phase, so the fleet
    // spreads out across known enemies instead of every ship converging on
    // the same one. Smaller than KillBonus, so a guaranteed kill always
    // still wins regardless of who else is already engaging that target.
    private const float AlreadyClaimedPenalty = 20f;

    // claimedTargets is the set of enemies one of THIS side's other ships
    // has already lined up on this same Move phase (see AiController). Pass
    // null, or an empty set, to opt out of deconfliction entirely.
    public static Decision ChooseDestination(
        ShipInstance self, AITurnContext context, GridManager gridManager, HashSet<ShipInstance> claimedTargets)
    {
        List<Vector2Int> candidates = GetPlaceableReachableTiles(self, gridManager);

        // Danger at a tile doesn't depend on which enemy we're currently
        // scoring an attack against, so it's computed once per tile here and
        // reused below -- instead of being recomputed from scratch for every
        // (tile, enemy) combination, and then a third time in FindSafestTile.
        Dictionary<Vector2Int, float> dangerByTile = new Dictionary<Vector2Int, float>();
        foreach (Vector2Int tile in candidates)
        {
            dangerByTile[tile] = DangerAtTile(tile, self, context, gridManager);
        }

        float bestScore = float.NegativeInfinity;
        Vector2Int bestTile = self.anchor;
        ShipInstance bestTarget = null;
        WeaponProfile bestWeapon = null;

        foreach (Vector2Int tile in candidates)
        {
            foreach (ShipInstance enemy in context.KnownEnemies)
            {
                WeaponProfile weapon = AIAttackPlanner.ChooseWeaponFromCandidateAnchor(self, tile, enemy, gridManager);
                if (weapon == null)
                {
                    continue;
                }

                bool alreadyClaimed = claimedTargets != null && claimedTargets.Contains(enemy);
                float score = ScoreAttackFromTile(self, enemy, dangerByTile[tile], weapon, alreadyClaimed);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTile = tile;
                    bestTarget = enemy;
                    bestWeapon = weapon;
                }
            }
        }

        bool isCriticallyLow = self.maxHealth > 0 &&
            (float)self.currentHealth / self.maxHealth <= FleeHealthFraction;
        bool bestIsLethal = bestTarget != null && bestWeapon != null &&
            AIScoring.ExpectedValue(bestWeapon) >= bestTarget.currentHealth;

        if (isCriticallyLow && !bestIsLethal)
        {
            Vector2Int safeTile = FindSafestTile(candidates, self, context, gridManager, dangerByTile);
            return new Decision { destination = safeTile, likelyTarget = null, isFleeing = true };
        }

        if (bestTarget == null)
        {
            // Nothing known and attackable -- close in on a known enemy if
            // one exists, otherwise regroup toward the map's center rather
            // than sitting still (AGENTS.md's planned AI behavior).
            bestTile = context.KnownEnemies.Count > 0
                ? ClosestTileToAnyEnemy(candidates, context.KnownEnemies, gridManager)
                : ClosestTileToCenter(candidates, gridManager);
        }

        return new Decision { destination = bestTile, likelyTarget = bestTarget, isFleeing = false };
    }

    // CalculateReachableCells only validates the anchor cell (see
    // GridPathfinder), so every candidate still needs a CanPlaceShip check
    // against the ship's full multi-cell footprint. The ship's current tile
    // is always included so "stay put" is a valid outcome.
    private static List<Vector2Int> GetPlaceableReachableTiles(ShipInstance self, GridManager gridManager)
    {
        var placeable = new List<Vector2Int> { self.anchor };
        foreach (Vector2Int anchor in gridManager.CalculateReachableCells(self))
        {
            if (anchor != self.anchor && gridManager.CanPlaceShip(self, anchor, self.rotationDegrees))
            {
                placeable.Add(anchor);
            }
        }
        return placeable;
    }

    // Takes the tile's danger as an already-computed value (see the cache
    // in ChooseDestination) rather than a tile/context/gridManager triple,
    // since danger no longer needs to be derived here.
    private static float ScoreAttackFromTile(
        ShipInstance self, ShipInstance target, float danger, WeaponProfile weapon, bool alreadyClaimed)
    {
        float expectedDamage = AIScoring.ExpectedValue(weapon);
        float score = 0f;

        // Kill bonus dominates everything else.
        bool isLethal = expectedDamage >= target.currentHealth;
        if (isLethal)
        {
            score += KillBonus;
        }
        else
        {
            // How big a bite this hit takes out of the target's CURRENT
            // health, 0..1. Replaces the old flat "expectedDamage * 0.4,
            // capped at 40" rule, which saturated for nearly every weapon
            // in this game (most clear the old ~100-damage cap easily) and
            // so barely told a strong weapon apart from a mediocre one.
            float damageFraction = target.currentHealth > 0
                ? Mathf.Clamp01(expectedDamage / target.currentHealth)
                : 0f;
            score += damageFraction * MaxDamageScore;

            // A guaranteed kill (the branch above) always overrides this --
            // piling on is still correct when it actually finishes the
            // target off. This only discourages a merely-okay non-lethal
            // hit on a target a sibling ship is already engaging, when a
            // comparable option exists against an unpressured known enemy.
            if (alreadyClaimed)
            {
                score -= AlreadyClaimedPenalty;
            }
        }

        // Secondary, smaller nudge toward targets that are already
        // weakened overall (independent of what THIS weapon would do),
        // based on how much of the target's max health is already gone.
        // Replaces "Max(0, 20 - currentHealth)", which assumed ~20 HP
        // ships and was effectively dead code at this game's 400-2000 HP
        // scale.
        float targetWeaknessFraction = target.maxHealth > 0
            ? 1f - Mathf.Clamp01((float)target.currentHealth / target.maxHealth)
            : 0f;
        score += targetWeaknessFraction * TargetWeaknessWeight;

        // Penalty for how exposed this tile is to known enemies after
        // moving there, expressed as a fraction of THIS ship's own max
        // health rather than a raw expected-damage number. The old raw
        // version (typically in the hundreds) silently dwarfed the kill
        // bonus and damage terms above, so tile choice was effectively
        // "avoid danger" almost to the exclusion of everything else.
        float dangerFraction = self.maxHealth > 0
            ? Mathf.Clamp01(danger / self.maxHealth)
            : 0f;
        score -= dangerFraction * DangerWeight;

        // Extra caution once self is already hurt, based on health
        // fraction instead of a raw "20 - currentHealth" delta that never
        // fired at this game's health scale.
        float selfHealthFraction = self.maxHealth > 0
            ? Mathf.Clamp01((float)self.currentHealth / self.maxHealth)
            : 1f;
        score -= (1f - selfHealthFraction) * SelfRiskWeight;

        return score;
    }

    // Total expected damage every KNOWN enemy could deal to a ship standing
    // at `tile` next turn. Deliberately only counts known enemies -- danger
    // from a hidden ship can't be evaluated without cheating the AI's fog.
    // No counter-fire modeling exists in CombatResolver, so this is "next
    // full enemy turn" danger, not an immediate counter-attack.
    //
    // Callers should prefer the dangerByTile cache built in
    // ChooseDestination over calling this directly, so the same tile's
    // danger is never computed more than once per turn.
    private static float DangerAtTile(Vector2Int tile, ShipInstance self, AITurnContext context, GridManager gridManager)
    {
        float danger = 0f;
        foreach (ShipInstance enemy in context.KnownEnemies)
        {
            WeaponProfile weapon = AIAttackPlanner.ChooseWeaponAgainstCandidateAnchor(enemy, self, tile, gridManager);
            if (weapon != null)
            {
                danger += AIScoring.ExpectedValue(weapon);
            }
        }
        return danger;
    }

    // Lowest danger wins first; distance from the nearest known enemy is
    // only a small tie-breaker on top. Takes the precomputed per-tile
    // danger cache instead of recalculating it a third time.
    private static Vector2Int FindSafestTile(
        List<Vector2Int> candidates, ShipInstance self, AITurnContext context, GridManager gridManager,
        Dictionary<Vector2Int, float> dangerByTile)
    {
        Vector2Int best = self.anchor;
        float bestSafety = float.NegativeInfinity;

        foreach (Vector2Int tile in candidates)
        {
            float danger = dangerByTile[tile];
            float nearestEnemyDist = NearestKnownEnemyDistance(tile, context, gridManager);
            float safety = -danger + nearestEnemyDist * 0.01f;

            if (safety > bestSafety)
            {
                bestSafety = safety;
                best = tile;
            }
        }

        return best;
    }

    private static Vector2Int ClosestTileToAnyEnemy(List<Vector2Int> candidates, List<ShipInstance> enemies, GridManager gridManager)
    {
        Vector2Int best = candidates[0];
        int bestDist = int.MaxValue;

        foreach (Vector2Int tile in candidates)
        {
            foreach (ShipInstance enemy in enemies)
            {
                int dist = gridManager.DistanceBetween(tile, enemy.anchor);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = tile;
                }
            }
        }

        return best;
    }

    private static Vector2Int ClosestTileToCenter(List<Vector2Int> candidates, GridManager gridManager)
    {
        Vector2Int center = new Vector2Int(gridManager.width / 2, gridManager.height / 2);
        Vector2Int best = candidates[0];
        int bestDist = int.MaxValue;

        foreach (Vector2Int tile in candidates)
        {
            int dist = gridManager.DistanceBetween(tile, center);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = tile;
            }
        }

        return best;
    }

    private static float NearestKnownEnemyDistance(Vector2Int tile, AITurnContext context, GridManager gridManager)
    {
        float min = float.MaxValue;
        foreach (ShipInstance enemy in context.KnownEnemies)
        {
            int d = gridManager.DistanceBetween(tile, enemy.anchor);
            if (d < min)
            {
                min = d;
            }
        }
        return min == float.MaxValue ? 0f : min;
    }
}