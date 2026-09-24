using System.Collections.Generic;
using UnityEngine;

// Extracted from GridManager. Plain class, not a MonoBehaviour, same
// convention as ShipInstance/FogGrid: logic that doesn't need Unity
// lifecycle callbacks or Inspector wiring stays a plain class.
// Holds a GridManager reference for RemoveShip, DistanceBetween, Match, and Fog.
public class CombatResolver
{
    private readonly GridManager gridManager;

    public CombatResolver(GridManager gridManager)
    {
        this.gridManager = gridManager;
    }

    public bool ResolveAttack(ShipInstance attacker, ShipInstance target, WeaponProfile weapon)
    {
        if (target.currentHealth <= 0)
        {
            Debug.Log($"Attack rejected: {target.owner}'s ship is already destroyed.");
            return false;
        }

        ChargeState weaponCharge = FindChargeState(attacker.weaponCharges, weapon.id);
        if (weaponCharge == null || !weaponCharge.IsReady)
        {
            Debug.Log($"Attack rejected: {weapon.id} is not ready (no charge state or recharging).");
            return false;
        }

        bool domainMatches = weapon.targetDomain == target.currentDomain || weapon.targetDomain == DomainType.Both;
        if (!domainMatches)
        {
            Debug.Log($"Attack rejected: {weapon.id} targets {weapon.targetDomain} but {target.owner}'s ship is {target.currentDomain}.");
            return false;
        }

        // Nearest attacker cell to nearest target cell, not anchor-only on either side.
        // Collect every in-range pair so the line-of-fire check can reuse them without
        // recalculating distances a second time.
        var attackerCells = attacker.GetOccupiedCells();
        var targetCells   = target.GetOccupiedCells();

        var inRangePairs = new List<(Vector2Int a, Vector2Int t)>();
        int minDistance  = int.MaxValue;

        foreach (var ac in attackerCells)
        {
            foreach (var tc in targetCells)
            {
                int d = gridManager.DistanceBetween(ac, tc);
                if (d < minDistance) minDistance = d;
                if (d <= weapon.weaponRange)
                {
                    inRangePairs.Add((ac, tc));
                }
            }
        }

        if (weapon.weaponRange < minDistance)
        {
            Debug.Log($"Attack rejected: {weapon.id} range {weapon.weaponRange} is less than distance {minDistance} to {target.owner}'s ship.");
            return false;
        }

        if (!IsTargetKnown(attacker, target))
        {
            Debug.Log("Target not known.");
            return false;
        }

        // Phase 9B: terrain line-of-fire.
        // The attack is legal when at least one in-range pair has a clear shot.
        // Only Impassable terrain blocks; Normal and Costly are transparent.
        // The attacker and target endpoint cells are excluded from the blocker test
        // (same rule as vision LOS — a ship may fire from its own hull).
        if (!HasClearLineOfFire(inRangePairs, out Vector2Int firstBlocker, out Vector2Int blockedA, out Vector2Int blockedT))
        {
            Debug.Log($"[COMBAT] BLOCKED_LINE_OF_FIRE attacker={attacker.owner} weapon={weapon.id} " +
                      $"target={target.owner} pair=({blockedA},{blockedT}) blocker={firstBlocker}");
            return false;
        }

        RollTier result = RollWeapon(weapon);
        target.currentHealth -= result.damage;
        Debug.Log($"{attacker.owner} fires {weapon.id} at {target.owner}: {result.outcomeLabel}" + (result.damage > 0 ? $" ({result.damage} dmg)" : ""));

        if (target.currentHealth <= 0)
        {
            Debug.Log($"{target.owner}'s ship destroyed!");
            gridManager.RemoveShip(target);
            gridManager.Match.GetPlayer(target.owner).ships.Remove(target);
        }

        return true;
    }

    // Returns true when at least one supplied attacker/target pair has a clear
    // supercover Bresenham path (no Impassable intermediate cell).
    // When every pair is blocked, out-params carry the blocker and the pair that
    // was last tested, for the rejection log.
    // Reuses VisionResolver.TryGetFirstBlockingCell so the same algorithm and
    // corner policy govern both vision LOS and attack line-of-fire (9B spec).
    public bool HasClearLineOfFire(
        List<(Vector2Int a, Vector2Int t)> inRangePairs,
        out Vector2Int firstBlocker,
        out Vector2Int lastTestedA,
        out Vector2Int lastTestedT)
    {
        firstBlocker = default;
        lastTestedA  = default;
        lastTestedT  = default;

        foreach (var (a, t) in inRangePairs)
        {
            lastTestedA = a;
            lastTestedT = t;

            if (!VisionResolver.TryGetFirstBlockingCell(a, t, gridManager, out Vector2Int blocker))
            {
                return true; // at least one clear pair — attack may resolve
            }

            firstBlocker = blocker;
        }

        // Every pair was blocked (or the list was empty, which should not happen
        // because we only reach here after passing the range check).
        return false;
    }

    private RollTier RollWeapon(WeaponProfile weapon)
    {
        int roll = Random.Range(1, 21);

        foreach (var tier in weapon.rollTiers)
        {
            if (roll >= tier.minRoll && roll <= tier.maxRoll)
            {
                return tier;
            }
        }

        Debug.LogWarning($"Roll {roll} did not match any tier on {weapon.id}. Check tier ranges.");
        return new RollTier(0, 0, "Error", 0);
    }

    public ChargeState FindChargeState(List<ChargeState> charges, string profileId)
    {
        foreach (var charge in charges)
        {
            if (charge.profileId == profileId)
            {
                return charge;
            }
        }
        return null;
    }

    public bool IsTargetKnown(ShipInstance attacker, ShipInstance target)
    {
        FogGrid fog = gridManager.Fog.GetFogGrid(attacker.owner);
        foreach (var cell in target.GetOccupiedCells())
        {
            if (fog.IsKnown(cell)) return true;
        }
        return false;
    }
}