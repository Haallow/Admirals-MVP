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

    public static Decision ChooseDestination(ShipInstance self, AITurnContext context, GridManager gridManager)
    {
        List<Vector2Int> candidates = GetPlaceableReachableTiles(self, gridManager);

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

                float score = ScoreAttackFromTile(self, enemy, tile, weapon, context, gridManager);
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
            Vector2Int safeTile = FindSafestTile(candidates, self, context, gridManager);
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

    private static float ScoreAttackFromTile(
        ShipInstance self, ShipInstance target, Vector2Int tile, WeaponProfile weapon,
        AITurnContext context, GridManager gridManager)
    {
        float expectedDamage = AIScoring.ExpectedValue(weapon);
        float score = 0f;

        // Kill bonus dominates everything else.
        if (expectedDamage >= target.currentHealth)
        {
            score += 50f;
        }
        else
        {
            score += Mathf.Min(expectedDamage * 0.4f, 40f);
        }

        // Prefer finishing off targets that are already low.
        score += Mathf.Max(0, 20 - target.currentHealth) * 0.5f;

        // Penalty for how exposed this tile is to known enemies after moving there.
        score -= DangerAtTile(tile, self, context, gridManager) * 0.5f;

        // Risk aversion when self is already low HP.
        score -= Mathf.Max(0, 20 - self.currentHealth) * 0.3f;

        return score;
    }

    // Total expected damage every KNOWN enemy could deal to a ship standing
    // at `tile` next turn. Deliberately only counts known enemies -- danger
    // from a hidden ship can't be evaluated without cheating the AI's fog.
    // No counter-fire modeling exists in CombatResolver, so this is "next
    // full enemy turn" danger, not an immediate counter-attack.
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
    // only a small tie-breaker on top.
    private static Vector2Int FindSafestTile(
        List<Vector2Int> candidates, ShipInstance self, AITurnContext context, GridManager gridManager)
    {
        Vector2Int best = self.anchor;
        float bestSafety = float.NegativeInfinity;

        foreach (Vector2Int tile in candidates)
        {
            float danger = DangerAtTile(tile, self, context, gridManager);
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