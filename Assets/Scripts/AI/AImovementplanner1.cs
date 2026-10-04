using System.Collections.Generic;
using UnityEngine;

// Decides where an AI ship should move, and -- if a good attack looks
// possible from the destination -- which known enemy it's lining up on.
// Movement never fires a weapon itself; AIController still does that
// separately during the Battle phase. Ported from an older FE-style
// prototype's greedy tile-scoring approach (kill bonus, danger-at-tile,
// flee-to-safety), adapted to this game's reachable-cell pathfinding,
// multi-cell footprints, and fog-gated (known-enemies-only) reasoning.
//
// Stage 2: formation and protection terms added. Per-tile scoring now
// includes cohesion pull (toward fleet centroid, scaled by stance),
// fragility pushback (high-fragility ships avoid closing with contacts),
// and scout forward bonus (scouts move toward threats). These are additive
// nudges on top of the existing attack/danger/kill scoring, not overrides.
public static class AIMovementPlanner
{
    // Debug flag: set to true to log detailed tile scoring breakdown
    private const bool DebugScoring = false;
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

    // Stage 2 formation weights: scaled as fractions of a ship's max health
    // so they sit on the same scale as the attack/danger terms above.
    // Cohesion pull nudges ships toward the fleet centroid; strength varies
    // by stance (see GetCohesionMultiplier). Fragility pushback penalizes
    // high-fragility ships for tiles that close with the nearest contact.
    // Scout forward bonus rewards scouts for tiles that advance toward threats.
    private const float CohesionBaseWeight = 8f;
    private const float FragilityPushbackWeight = 12f;
    private const float ScoutForwardWeight = 6f;
    private const float FragilityThreshold = 0.6f;  // EffectiveFragility above this triggers pushback

    // Stage 3 directional bias weights: nudge tile selection based on stance.
    // Retreat/Fallback push away from contacts toward home. Advance pushes toward contacts.
    // Hold has zero bias (defensive, maintain position).
    private const float RetreatDirectionalWeight = 18f;
    private const float FallbackDirectionalWeight = 10f;
    private const float AdvanceDirectionalWeight = 10f;
    private const float RetreatVariance = 4;  // Tiles spread within ±4 of home centroid

    // claimedTargets is the set of enemies one of THIS side's other ships
    // has already lined up on this same Move phase (see AiController). Pass
    // null, or an empty set, to opt out of deconfliction entirely.
    //
    // Stage 2: FleetSnapshot parameter added. Contains stance, centroid,
    // roles, and contacts -- everything needed for formation scoring.
    public static Decision ChooseDestination(
        ShipInstance self,
        AITurnContext context,
        FleetSnapshot snapshot,
        GridManager gridManager,
        HashSet<ShipInstance> claimedTargets)
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

        // Stage 2: find this ship's member entry for role and fragility.
        FleetMember member = FindMember(snapshot, self);

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

                // Stage 2: add formation terms on top of the attack score.
                score += ScoreFormationTerms(tile, self, member, snapshot, gridManager);

                // Stage 3: add directional bias (stance-driven fleet movement).
                score += ScoreDirectionalBias(tile, self, snapshot, gridManager);

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
            // Stage 3: Stance-aware fallback when no valid attack exists.
            if (context.KnownEnemies.Count > 0)
            {
                bestTile = ClosestTileToAnyEnemy(candidates, context.KnownEnemies, gridManager, self, member, snapshot);
            }
            else if (snapshot.CurrentStance == FleetStance.Hunt)
            {
                // Hunt behavior: advance toward the opposite edge from home.
                // Home is snapshot.HomeCentroid, map center is known, so the
                // enemy's likely starting zone is the far side of center.
                Vector2Int center = new Vector2Int(gridManager.width / 2, gridManager.height / 2);
                Vector2Int towardEnemy = center + (center - snapshot.HomeCentroid);
                bestTile = ClosestTileToPoint(candidates, towardEnemy, gridManager, self, member, snapshot);
            }
            else
            {
                // Defensive stances with no contact: regroup at center.
                bestTile = ClosestTileToCenter(candidates, gridManager, self, member, snapshot);
            }
        }

        return new Decision { destination = bestTile, likelyTarget = bestTarget, isFleeing = false };
    }

    // ---- Formation scoring (Stage 2) ----------------------------------------

    private static FleetMember FindMember(FleetSnapshot snapshot, ShipInstance ship)
    {
        foreach (FleetMember m in snapshot.Members)
        {
            if (m.Ship == ship)
                return m;
        }
        // Ship not in the snapshot (destroyed mid-phase?) -- return a neutral fallback.
        return new FleetMember
        {
            Ship = ship,
            Role = FleetRole.Anchor,
            StaticFragility = 0.5f,
            HpFraction = 1f,
            EffectiveFragility = 0.5f
        };
    }

    private static float ScoreFormationTerms(
        Vector2Int tile,
        ShipInstance self,
        FleetMember member,
        FleetSnapshot snapshot,
        GridManager gridManager)
    {
        float score = 0f;

        // Cohesion: nudge toward the fleet centroid, scaled by stance.
        float cohesionMult = GetCohesionMultiplier(snapshot.CurrentStance);
        if (cohesionMult > 0f && snapshot.Members.Count > 1)
        {
            int distToCentroid = gridManager.DistanceBetween(tile, snapshot.FleetCentroid);
            int currentDist = gridManager.DistanceBetween(self.anchor, snapshot.FleetCentroid);
            int deltaTowardCentroid = currentDist - distToCentroid;  // positive = moving closer
            float cohesionPull = deltaTowardCentroid * CohesionBaseWeight * cohesionMult;
            score += cohesionPull;
        }

        // Fragility pushback: high-fragility ships penalized for closing with the nearest contact.
        if (member.EffectiveFragility > FragilityThreshold && snapshot.NearestContact != null)
        {
            int currentDistToContact = NearestDistanceToContact(self.anchor, snapshot.NearestContact, gridManager);
            int tileDistToContact = NearestDistanceToContact(tile, snapshot.NearestContact, gridManager);
            int deltaTowardContact = currentDistToContact - tileDistToContact;  // positive = moving closer to threat
            if (deltaTowardContact > 0)
            {
                float pushback = -deltaTowardContact * FragilityPushbackWeight;
                score += pushback;
            }
        }

        // Scout forward: scouts get a bonus for advancing toward the nearest contact.
        if (member.Role == FleetRole.Scout && snapshot.NearestContact != null)
        {
            int currentDistToContact = NearestDistanceToContact(self.anchor, snapshot.NearestContact, gridManager);
            int tileDistToContact = NearestDistanceToContact(tile, snapshot.NearestContact, gridManager);
            int deltaTowardContact = currentDistToContact - tileDistToContact;  // positive = moving closer
            if (deltaTowardContact > 0)
            {
                float forwardBonus = deltaTowardContact * ScoutForwardWeight;
                score += forwardBonus;
            }
        }

        return score;
    }

    // Stage 3: Directional bias based on stance. Nudges tile selection toward
    // or away from contacts/home depending on strategic intent.
    private static float ScoreDirectionalBias(
        Vector2Int tile,
        ShipInstance self,
        FleetSnapshot snapshot,
        GridManager gridManager)
    {
        FleetStance stance = snapshot.CurrentStance;

        // Hold and Hunt have no directional bias (Hunt uses fallback logic instead)
        if (stance == FleetStance.Hold || stance == FleetStance.Hunt)
            return 0f;

        // Need a contact for directional decisions
        if (snapshot.NearestContact == null)
            return 0f;

        Vector2Int contactCentroid = snapshot.NearestContact.Centroid;
        int currentDistToContact = gridManager.DistanceBetween(self.anchor, contactCentroid);
        int tileDistToContact = gridManager.DistanceBetween(tile, contactCentroid);
        int deltaTowardContact = currentDistToContact - tileDistToContact;  // positive = moving closer

        switch (stance)
        {
            case FleetStance.Advance:
                // Push toward contacts
                if (deltaTowardContact > 0)
                    return deltaTowardContact * AdvanceDirectionalWeight;
                return 0f;

            case FleetStance.Retreat:
            case FleetStance.DefensiveFallback:
                // Push away from contacts, toward home (with variance for spread)
                if (deltaTowardContact < 0)  // moving away from contact
                {
                    float weight = stance == FleetStance.Retreat ? RetreatDirectionalWeight : FallbackDirectionalWeight;
                    float baseScore = -deltaTowardContact * weight;

                    // Add variance: prefer tiles that spread around home centroid rather than
                    // clustering on the exact home pixel. Tiles within ±RetreatVariance of home
                    // get a small bonus.
                    int distToHome = gridManager.DistanceBetween(tile, snapshot.HomeCentroid);
                    if (distToHome <= RetreatVariance)
                    {
                        // Bonus tapers off as you get farther from home
                        float spreadBonus = (RetreatVariance - distToHome) * 0.5f;
                        baseScore += spreadBonus;
                    }

                    return baseScore;
                }
                return 0f;

            default:
                return 0f;
        }
    }

    private static float GetCohesionMultiplier(FleetStance stance)
    {
        switch (stance)
        {
            case FleetStance.Retreat:
            case FleetStance.DefensiveFallback:
                return 1.0f;  // tight formation when defensive
            case FleetStance.Hold:
                return 0.7f;
            case FleetStance.Hunt:
                return 0.0f;  // no cohesion pull while searching — let ships spread out
            case FleetStance.Advance:
                return 0.2f;  // loose, pressing forward
            case FleetStance.Flank:
                return 0.5f;  // reserved for Stage 3+
            default:
                return 0.5f;
        }
    }

    private static int NearestDistanceToContact(Vector2Int pos, AIContact contact, GridManager gridManager)
    {
        int best = int.MaxValue;
        foreach (Vector2Int cell in contact.Cells)
        {
            int d = gridManager.DistanceBetween(pos, cell);
            if (d < best)
                best = d;
        }
        return best == int.MaxValue ? 0 : best;
    }

    // ---- Tile generation and per-tile scoring -------------------------------

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

    // Stage 2: fallback tile choosers now accept snapshot/member for formation scoring.
    private static Vector2Int ClosestTileToAnyEnemy(
        List<Vector2Int> candidates,
        List<ShipInstance> enemies,
        GridManager gridManager,
        ShipInstance self,
        FleetMember member,
        FleetSnapshot snapshot)
    {
        Vector2Int best = candidates[0];
        float bestScore = float.NegativeInfinity;

        foreach (Vector2Int tile in candidates)
        {
            int distToEnemy = int.MaxValue;
            foreach (ShipInstance enemy in enemies)
            {
                int d = gridManager.DistanceBetween(tile, enemy.anchor);
                if (d < distToEnemy)
                    distToEnemy = d;
            }

            // Closer to enemy is better (negative distance), plus formation terms.
            float score = -distToEnemy + ScoreFormationTerms(tile, self, member, snapshot, gridManager);
            if (score > bestScore)
            {
                bestScore = score;
                best = tile;
            }
        }

        return best;
    }

    private static Vector2Int ClosestTileToCenter(
        List<Vector2Int> candidates,
        GridManager gridManager,
        ShipInstance self,
        FleetMember member,
        FleetSnapshot snapshot)
    {
        Vector2Int center = new Vector2Int(gridManager.width / 2, gridManager.height / 2);
        Vector2Int best = candidates[0];
        float bestScore = float.NegativeInfinity;

        Debug.Log($"[AI][Move] {self.shipType} ClosestTileToCenter: center={center}, currentPos={self.anchor}, candidates={candidates.Count}");

        foreach (Vector2Int tile in candidates)
        {
            int distToCenter = gridManager.DistanceBetween(tile, center);
            // Closer to center is better (negative distance), plus formation terms.
            float score = -distToCenter + ScoreFormationTerms(tile, self, member, snapshot, gridManager);
            if (score > bestScore)
            {
                bestScore = score;
                best = tile;
            }
        }
        Debug.Log($"[AI][Move] {self.shipType} chose {best} (score={bestScore:F1}, distToCenter={gridManager.DistanceBetween(best, center)})");
        return best;
    }

    // Generic helper: pick the tile closest to an arbitrary point, with formation terms.
    private static Vector2Int ClosestTileToPoint(
        List<Vector2Int> candidates,
        Vector2Int targetPoint,
        GridManager gridManager,
        ShipInstance self,
        FleetMember member,
        FleetSnapshot snapshot)
    {
        Vector2Int best = candidates[0];
        float bestScore = float.NegativeInfinity;

        foreach (Vector2Int tile in candidates)
        {
            int distToTarget = gridManager.DistanceBetween(tile, targetPoint);
            // Closer to target is better (negative distance), plus formation terms.
            float score = -distToTarget + ScoreFormationTerms(tile, self, member, snapshot, gridManager);
            if (score > bestScore)
            {
                bestScore = score;
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
