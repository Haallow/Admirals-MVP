using System;
using System.Collections.Generic;
using UnityEngine;

public enum AttackRequestStatus { Rejected, PendingDefense, Finalized }

public sealed class AttackOutcome
{
    public ShipInstance Attacker { get; }
    public ShipInstance Target { get; }
    public WeaponProfile Weapon { get; }
    public string DefenseId { get; }
    public bool Avoided { get; }
    public int Damage { get; }

    public AttackOutcome(ShipInstance attacker, ShipInstance target, WeaponProfile weapon,
        string defenseId, bool avoided, int damage)
    {
        Attacker = attacker;
        Target = target;
        Weapon = weapon;
        DefenseId = defenseId;
        Avoided = avoided;
        Damage = damage;
    }
}

public sealed class PendingDefenseView
{
    public PlayerId Defender { get; }
    public ShipInstance Target { get; }
    public IReadOnlyList<string> EligibleDefenseIds { get; }

    public PendingDefenseView(PlayerId defender, ShipInstance target, IReadOnlyList<string> ids)
    {
        Defender = defender;
        Target = target;
        EligibleDefenseIds = ids;
    }
}

// Extracted from GridManager. Plain class, not a MonoBehaviour, same
// convention as ShipInstance/FogGrid: logic that doesn't need Unity
// lifecycle callbacks or Inspector wiring stays a plain class.
// Holds a GridManager reference for RemoveShip, DistanceBetween, Match, and Fog.
public class CombatResolver
{
    private readonly GridManager gridManager;
    private ShipInstance pendingAttacker;
    private ShipInstance pendingTarget;
    private WeaponProfile pendingWeapon;
    private readonly List<string> pendingDefenseIds = new List<string>();

    public Func<int> RollD20 { get; set; } = () => UnityEngine.Random.Range(1, 21);
    public event Action<AttackOutcome> AttackFinalized;
    public bool HasPendingDefense => pendingTarget != null;
    public PendingDefenseView PendingDefense => HasPendingDefense
        ? new PendingDefenseView(pendingTarget.owner, pendingTarget, pendingDefenseIds.AsReadOnly())
        : null;

    public CombatResolver(GridManager gridManager)
    {
        this.gridManager = gridManager;
    }

    public AttackRequestStatus RequestAttack(ShipInstance attacker, ShipInstance target, WeaponProfile weapon)
    {
        if (HasPendingDefense || gridManager.Match == null || attacker == null || target == null || weapon == null ||
            attacker.currentHealth <= 0 || attacker.owner == target.owner ||
            !gridManager.Match.GetPlayer(attacker.owner).ships.Contains(attacker) ||
            !gridManager.Match.GetPlayer(target.owner).ships.Contains(target) ||
            !attacker.weapons.Contains(weapon))
            return AttackRequestStatus.Rejected;

        if (target.currentHealth <= 0)
        {
            Debug.Log($"Attack rejected: {target.owner}'s ship is already destroyed.");
            return AttackRequestStatus.Rejected;
        }

        if (attacker.hasAttackedThisPhase)
        {
            Debug.Log($"Attack rejected: {attacker.owner}'s {attacker.shipType} has already attacked this Battle phase.");
            return AttackRequestStatus.Rejected;
        }

        if (gridManager.TurnManager != null)
        {
            if (gridManager.TurnManager.CurrentPhase != Phase.Battle)
            {
                Debug.Log($"Attack rejected: attacks can only be made during Battle phase (current: {gridManager.TurnManager.CurrentPhase}).");
                return AttackRequestStatus.Rejected;
            }

            if (attacker.owner != gridManager.TurnManager.CurrentPlayer)
            {
                Debug.Log($"Attack rejected: it is not {attacker.owner}'s turn (current: {gridManager.TurnManager.CurrentPlayer}).");
                return AttackRequestStatus.Rejected;
            }
        }

        ChargeState weaponCharge = FindChargeState(attacker.weaponCharges, weapon.id);
        if (weaponCharge == null || !weaponCharge.IsReady)
        {
            Debug.Log($"Attack rejected: {weapon.id} is not ready (no charge state or recharging).");
            return AttackRequestStatus.Rejected;
        }

        bool domainMatches = weapon.targetDomain == target.currentDomain || weapon.targetDomain == DomainType.Both;
        if (!domainMatches)
        {
            Debug.Log($"Attack rejected: {weapon.id} targets {weapon.targetDomain} but {target.owner}'s ship is {target.currentDomain}.");
            return AttackRequestStatus.Rejected;
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
            return AttackRequestStatus.Rejected;
        }

        if (!IsTargetKnown(attacker, target))
        {
            Debug.Log("Target not known.");
            return AttackRequestStatus.Rejected;
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
            return AttackRequestStatus.Rejected;
        }

        pendingAttacker = attacker;
        pendingTarget = target;
        pendingWeapon = weapon;
        pendingDefenseIds.Clear();
        foreach (DefenseProfile defense in target.defenses)
        {
            ChargeState charge = FindChargeState(target.defenseCharges, defense.id);
            if ((defense.validAgainst == DomainType.Both || defense.validAgainst == target.currentDomain) &&
                charge != null && charge.IsReady && !pendingDefenseIds.Contains(defense.id))
                pendingDefenseIds.Add(defense.id);
        }

        if (pendingDefenseIds.Count == 0)
        {
            SubmitDefense(target.owner, null, out _);
            return AttackRequestStatus.Finalized;
        }

        return AttackRequestStatus.PendingDefense;
    }

    public bool SubmitDefense(PlayerId defender, string defenseId, out AttackOutcome outcome)
    {
        outcome = null;
        if (!HasPendingDefense || defender != pendingTarget.owner) return false;

        DefenseProfile defense = null;
        ChargeState defenseCharge = null;
        if (defenseId != null)
        {
            if (!pendingDefenseIds.Contains(defenseId)) return false;
            defense = pendingTarget.defenses.Find(d => d.id == defenseId);
            defenseCharge = FindChargeState(pendingTarget.defenseCharges, defenseId);
            if (defense == null || defenseCharge == null || !defenseCharge.IsReady ||
                (defense.validAgainst != DomainType.Both && defense.validAgainst != pendingTarget.currentDomain))
                return false;
        }

        bool avoided = false;
        if (defense != null)
        {
            int roll = RollD20();
            RollTier? tier = null;
            foreach (RollTier candidate in defense.savingThrowTiers)
                if (roll >= candidate.minRoll && roll <= candidate.maxRoll) { tier = candidate; break; }
            if (!tier.HasValue || (tier.Value.outcomeLabel != "Fail" && tier.Value.outcomeLabel != "Hit Avoided"))
                return false;
            if (defenseCharge.remaining != -1) defenseCharge.remaining--;
            avoided = tier.Value.outcomeLabel == "Hit Avoided";
            if (avoided && defense.sideEffectId == "BecomeSubSurfaceAndSkipNextMove")
            {
                pendingTarget.currentDomain = DomainType.SubSurface;
                pendingTarget.skipNextMove = true;
            }
        }

        ShipInstance attacker = pendingAttacker;
        ShipInstance target = pendingTarget;
        WeaponProfile weapon = pendingWeapon;
        pendingAttacker = null;
        pendingTarget = null;
        pendingWeapon = null;
        pendingDefenseIds.Clear();

        ChargeState weaponCharge = FindChargeState(attacker.weaponCharges, weapon.id);

        // Mark the ship as having attacked this Battle phase. Each ship may only attack once per Battle phase.
        attacker.hasAttackedThisPhase = true;

        RollTier result = avoided ? new RollTier(0, 0, "Hit Avoided", 0) : RollWeapon(weapon);

        // Deduct one use. -1 is the infinite sentinel and is never decremented.
        if (weaponCharge.remaining != -1)
        {
            weaponCharge.remaining--;
        }

        int effectiveDamage = ApplyArmor(result.damage, target.armor);
        target.currentHealth -= effectiveDamage;
        string ammoStr = weaponCharge.remaining == -1 ? "∞" : weaponCharge.remaining.ToString();
        Debug.Log($"{attacker.owner} fires {weapon.id} at {target.owner}: {result.outcomeLabel}" +
                  (result.damage > 0
                      ? $" ({result.damage} raw → {effectiveDamage} after armor {target.armor})"
                      : "") +
                  $" | {weapon.id} ammo remaining: {ammoStr}");

        if (target.currentHealth <= 0)
        {
            Debug.Log($"{target.owner}'s ship destroyed!");
            // Clears passive and active fog marks for this ship so destroyed targets don't retain ghost contact markers.
            gridManager.Fog?.ClearMarksForShip(target);
            gridManager.RemoveShip(target);
            gridManager.Match.GetPlayer(target.owner).ships.Remove(target);
        }

        outcome = new AttackOutcome(attacker, target, weapon, defenseId, avoided, effectiveDamage);
        AttackFinalized?.Invoke(outcome);
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
        int roll = RollD20();

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

    // Applies the armor damage reduction formula: effectiveDamage = round(rawDamage * (1 - armor * 0.0015))
    // 0.15% reduction per armor point. At armor 50 → 7.5%, 100 → 15%, 150 → 22.5%, 200 → 30%.
    // Miss (rawDamage == 0) is returned unchanged as 0; the minimum-1 floor does not apply to misses.
    // All non-zero results are clamped to a minimum of 1 — armor cannot negate a hit entirely.
    public static int ApplyArmor(int rawDamage, int armor)
    {
        if (rawDamage == 0) return 0;

        float reduced = rawDamage * (1f - armor * 0.0015f);
        int result    = Mathf.RoundToInt(reduced);
        return Mathf.Max(1, result);
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

    // Returns true if the ship is alive, has not yet attacked this Battle phase,
    // and (if TurnManager is present) it is currently the Battle phase and this ship owner's turn.
    public bool CanShipAttack(ShipInstance ship)
    {
        if (HasPendingDefense || ship == null || ship.currentHealth <= 0 || ship.hasAttackedThisPhase)
        {
            return false;
        }

        if (gridManager.TurnManager != null)
        {
            if (gridManager.TurnManager.CurrentPhase != Phase.Battle ||
                ship.owner != gridManager.TurnManager.CurrentPlayer)
            {
                return false;
            }
        }

        return true;
    }
}
