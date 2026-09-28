using System.Collections.Generic;
using UnityEngine;

// Decides which weapon (if any) can hit a given target -- in three flavors:
//   - both ships at their real, current positions (an actual attack)
//   - the attacker at a hypothetical anchor, target real ("if I moved here,
//     could I hit them?" -- used by AIMovementPlanner to score a move)
//   - the target at a hypothetical anchor, attacker real ("if I moved here,
//     could THEY hit me?" -- used by AIMovementPlanner's danger calculation)
// All three funnel through the same CanFire/scoring core so the legality
// rules never drift between a real attack and a "what if" check.
public static class AIAttackPlanner
{
    // Kill bonus dominates every other weapon-value comparison in
    // TryChooseTarget, so a guaranteed kill always wins over a merely
    // strong non-lethal option.
    private const float KillBonus = 10000f;

    // CHANGE (AI Optimization Recommendations, item 2.3): soft penalty
    // applied when a non-lethal shot would land on a target one of this
    // ship's siblings has already fired on this Battle phase, so the fleet
    // spreads its fire instead of every ship independently piling onto the
    // same easiest target. Small enough relative to typical expected-damage
    // values (roughly 100-1000 in this game) that it only tips the balance
    // between comparable options -- it never overrides the kill bonus above,
    // and it never leaves a ship with literally nothing to shoot at.
    private const float AlreadyClaimedPenalty = 300f;

    public static WeaponProfile ChooseWeapon(ShipInstance attacker, ShipInstance target, GridManager gridManager)
    {
        return ChooseBestWeapon(attacker, attacker.GetOccupiedCells(), target.GetOccupiedCells(), target.currentDomain, gridManager);
    }

    // Fog-aware, multi-enemy target selection for the Battle phase. Only
    // considers enemies the attacker's own fog currently knows about --
    // mirrors how AIMovementPlanner already reasons over context.KnownEnemies.
    // A guaranteed kill always wins over a higher-expected-value non-kill.
    //
    // claimedTargets is the set of enemies one of THIS side's other ships
    // has already fired on this same Battle phase (see AiController). Pass
    // null, or an empty set, to opt out of deconfliction entirely.
    public static bool TryChooseTarget(
        ShipInstance attacker,
        List<ShipInstance> knownEnemies,
        GridManager gridManager,
        HashSet<ShipInstance> claimedTargets,
        out ShipInstance target,
        out WeaponProfile weapon)
    {
        target = null;
        weapon = null;
        float bestScore = float.NegativeInfinity;

        foreach (ShipInstance enemy in knownEnemies)
        {
            if (enemy.currentHealth <= 0)
            {
                continue; // may have been destroyed by an earlier ship this same phase
            }

            WeaponProfile candidate = ChooseWeapon(attacker, enemy, gridManager);
            if (candidate == null)
            {
                continue;
            }

            float score = AIScoring.ExpectedValue(candidate);

            bool isLethal = score >= enemy.currentHealth;
            if (isLethal)
            {
                score += KillBonus;
            }
            else if (claimedTargets != null && claimedTargets.Contains(enemy))
            {
                score -= AlreadyClaimedPenalty;
            }

            if (score > bestScore)
            {
                bestScore = score;
                target = enemy;
                weapon = candidate;
            }
        }

        return target != null && weapon != null;
    }

    // "If attacker moved to candidateAnchor, could it then hit target?"
    // Used by AIMovementPlanner to score a potential destination.
    public static WeaponProfile ChooseWeaponFromCandidateAnchor(
        ShipInstance attacker, Vector2Int candidateAnchor, ShipInstance target, GridManager gridManager)
    {
        List<Vector2Int> attackerCells = FootprintUtil.GetWorldCells(candidateAnchor, attacker.footprintOffsets, attacker.rotationDegrees);
        return ChooseBestWeapon(attacker, attackerCells, target.GetOccupiedCells(), target.currentDomain, gridManager);
    }

    // "If target (usually 'self', evaluated for danger) moved to
    // candidateTargetAnchor, could attacker (a real, fixed known enemy) hit
    // it there?" Used by AIMovementPlanner's DangerAtTile calculation.
    public static WeaponProfile ChooseWeaponAgainstCandidateAnchor(
        ShipInstance attacker, ShipInstance target, Vector2Int candidateTargetAnchor, GridManager gridManager)
    {
        List<Vector2Int> targetCells = FootprintUtil.GetWorldCells(candidateTargetAnchor, target.footprintOffsets, target.rotationDegrees);
        return ChooseBestWeapon(attacker, attacker.GetOccupiedCells(), targetCells, target.currentDomain, gridManager);
    }

    private static WeaponProfile ChooseBestWeapon(
        ShipInstance attacker, List<Vector2Int> attackerCells, List<Vector2Int> targetCells,
        DomainType targetDomain, GridManager gridManager)
    {
        WeaponProfile best = null;
        float bestScore = -1f;

        foreach (var weapon in attacker.weapons)
        {
            if (!CanFire(attacker, attackerCells, targetCells, targetDomain, weapon, gridManager))
            {
                continue;
            }

            float score = AIScoring.ExpectedValue(weapon);
            if (score > bestScore)
            {
                bestScore = score;
                best = weapon;
            }
        }

        return best;
    }

    // Mirrors CombatResolver.ResolveAttack's checks (charge, domain,
    // nearest-cell-to-nearest-cell range, and terrain line-of-fire), without
    // side effects, so it's safe to call for hypothetical positions too.
    //
    // CHANGE (AI Optimization Recommendations, item 2.1): this used to stop
    // at a range check and never verified line of fire, so the AI could
    // believe a shot was legal when CombatResolver.ResolveAttack would
    // actually reject it for being terrain-blocked -- wasting a move or an
    // attack action with no fallback. It now walks every in-range
    // attacker/target cell pair, same as CombatResolver.HasClearLineOfFire,
    // and only accepts the weapon if at least one pair has a clear
    // supercover Bresenham line (VisionResolver.TryGetFirstBlockingCell).
    // Because both AIMovementPlanner (destination scoring + danger
    // calculation) and AiController's battle-phase targeting all route
    // through CanFire, this one fix corrects all three call sites at once.
    private static bool CanFire(
        ShipInstance attacker, List<Vector2Int> attackerCells, List<Vector2Int> targetCells,
        DomainType targetDomain, WeaponProfile weapon, GridManager gridManager)
    {
        ChargeState charge = gridManager.Combat.FindChargeState(attacker.weaponCharges, weapon.id);
        if (charge == null || !charge.IsReady)
        {
            return false;
        }

        bool domainMatches = weapon.targetDomain == targetDomain || weapon.targetDomain == DomainType.Both;
        if (!domainMatches)
        {
            return false;
        }

        // Walk every attacker-cell/target-cell pair within weapon range and
        // accept the first one with a clear line of fire. This matches
        // CombatResolver.ResolveAttack's own range + line-of-fire gating,
        // instead of only checking the single nearest pair's distance.
        foreach (var attackerCell in attackerCells)
        {
            foreach (var targetCell in targetCells)
            {
                if (gridManager.DistanceBetween(attackerCell, targetCell) > weapon.weaponRange)
                {
                    continue;
                }

                if (!VisionResolver.TryGetFirstBlockingCell(attackerCell, targetCell, gridManager, out _))
                {
                    return true; // in range AND a clear shot -- weapon can fire
                }
            }
        }

        // Either no cell pair was in range, or every in-range pair was
        // blocked by Impassable terrain.
        return false;
    }
}