using System;
using System.Collections.Generic;
using UnityEngine;

// Plain [Serializable] class, not a MonoBehaviour.
// This is what makes it show up editable in GridManager's Inspector
// when used as a [SerializeField] field there.
[Serializable]
public class ShipInstance
{
    // --- Identity / ownership ---
    public PlayerId owner = PlayerId.PlayerA;

    // --- Grid placement ---
    public Vector2Int anchor;
    public int rotationDegrees; // expected values: 0, 90, 180, 270
    public int movementRange = 3; // placeholder; real numbers come from the design doc later

    // Footprint is supplied by ShipData for each ship class.
    public List<Vector2Int> footprintOffsets = new List<Vector2Int>();

    // --- Core stats ---
    public int maxHealth;
    public int currentHealth;
    public int armor;

    // --- Domain state (Surface / SubSurface / Both) ---
    // Mutable at runtime — submarines toggle between Surface and SubSurface.
    // "Both" is not a valid runtime state for a ship; it's only used on profile definitions.
    public DomainType currentDomain = DomainType.Surface;

    // --- Combat profiles (immutable definitions set at ship creation) ---
    public List<WeaponProfile> weapons = new List<WeaponProfile>();
    public List<DefenseProfile> defenses = new List<DefenseProfile>();
    public List<VisionLayer> visionLayers = new List<VisionLayer>();

    // --- Runtime charge tracking (one entry per weapon / defense profile) ---
    // Populated by InitializeCharges(). These change as the game is played.
    public List<ChargeState> weaponCharges = new List<ChargeState>();
    public List<ChargeState> defenseCharges = new List<ChargeState>();

    // --- Grid helper ---
    public List<Vector2Int> GetOccupiedCells()
    {
        return FootprintUtil.GetWorldCells(anchor, footprintOffsets, rotationDegrees);
    }

    // --- Setup ---

    // Call once after all profiles are assigned to populate the charge-tracking lists.
    // Uses -1 as the sentinel for "infinite" since ChargeState.remaining is a plain int.
    public void InitializeCharges()
    {
        currentHealth = maxHealth;

        weaponCharges.Clear();
        foreach (var weapon in weapons)
        {
            int starting = weapon.ammo.HasValue ? weapon.ammo.Value : -1;
            weaponCharges.Add(new ChargeState(weapon.id, starting));
        }

        defenseCharges.Clear();
        foreach (var defense in defenses)
        {
            int starting = defense.uses.HasValue ? defense.uses.Value : -1;
            defenseCharges.Add(new ChargeState(defense.id, starting));
        }
    }

    // --- Debug logging ---

    // Prints the full stat block to Console so each milestone can be verified visually.
    public void LogStatBlock(string label)
    {
        Debug.Log($"=== {label} ===");
        Debug.Log($"  Owner: {owner} | Domain: {currentDomain} | HP: {currentHealth}/{maxHealth} | Armor: {armor}");
        Debug.Log($"  Footprint: {footprintOffsets.Count} tile(s) | Move range: {movementRange}");

        for (int i = 0; i < weapons.Count; i++)
        {
            var w = weapons[i];
            var c = i < weaponCharges.Count ? weaponCharges[i] : null;
            string ammoStr = c != null ? (c.remaining == -1 ? "∞" : c.remaining.ToString()) : "?";
            Debug.Log($"  [WEAPON] {w.id} | Domain: {w.targetDomain} | Ammo: {ammoStr}");
            foreach (var t in w.rollTiers)
                Debug.Log($"    D{t.minRoll}-{t.maxRoll}: {t.outcomeLabel}" + (t.damage > 0 ? $" ({t.damage} dmg)" : ""));
        }

        for (int i = 0; i < defenses.Count; i++)
        {
            var d = defenses[i];
            var c = i < defenseCharges.Count ? defenseCharges[i] : null;
            string usesStr = c != null ? (c.remaining == -1 ? "∞" : c.remaining.ToString()) : "?";
            string sideEffect = string.IsNullOrEmpty(d.sideEffectId) ? "" : $" | SideEffect: {d.sideEffectId}";
            Debug.Log($"  [DEFENSE] {d.id} | Against: {d.validAgainst} | Uses: {usesStr}{sideEffect}");
            foreach (var t in d.savingThrowTiers)
                Debug.Log($"    D{t.minRoll}-{t.maxRoll}: {t.outcomeLabel}");
        }

        foreach (var v in visionLayers)
        {
            bool surfacedOnly = v.onlyWhileSurfaced;
            bool currentlyActive = !surfacedOnly || currentDomain == DomainType.Surface;
            string activeStr = surfacedOnly ? $" | Active now: {currentlyActive} (onlyWhileSurfaced)" : "";
            Debug.Log($"  [VISION] {v.id} | {v.shape} r={v.range} | Detects: {v.detects} | {v.visionType} | Passive: {v.isPassive}{activeStr}");
        }
    }
}
