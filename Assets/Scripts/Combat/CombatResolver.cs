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
        if (target.currentHealth <= 0) return false;

        ChargeState weaponCharge = FindChargeState(attacker.weaponCharges, weapon.id);
        if (weaponCharge == null || !weaponCharge.IsReady)
        {
            return false;
        }

        bool domainMatches = weapon.targetDomain == target.currentDomain || weapon.targetDomain == DomainType.Both;
        if (!domainMatches)
        {
            return false;
        }

        // Nearest attacker cell to nearest target cell, not anchor-only on either side.
        int minDistance = int.MaxValue;
        foreach (var attackerCell in attacker.GetOccupiedCells())
        {
            foreach (var targetCell in target.GetOccupiedCells())
            {
                minDistance = Mathf.Min(minDistance, gridManager.DistanceBetween(attackerCell, targetCell));
            }
        }

        if (weapon.weaponRange < minDistance)
        {
            return false;
        }

        if (!IsTargetKnown(attacker, target))
        {
            Debug.Log("Target not known.");
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