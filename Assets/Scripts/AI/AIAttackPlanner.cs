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
    public static WeaponProfile ChooseWeapon(ShipInstance attacker, ShipInstance target, GridManager gridManager)
    {
        return ChooseBestWeapon(attacker, attacker.GetOccupiedCells(), target.GetOccupiedCells(), target.currentDomain, gridManager);
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
    // nearest-cell-to-nearest-cell range), without side effects, so it's
    // safe to call for hypothetical positions too.
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

        int minDistance = int.MaxValue;
        foreach (var attackerCell in attackerCells)
        {
            foreach (var targetCell in targetCells)
            {
                minDistance = Mathf.Min(minDistance, gridManager.DistanceBetween(attackerCell, targetCell));
            }
        }

        return weapon.weaponRange >= minDistance;
    }
}