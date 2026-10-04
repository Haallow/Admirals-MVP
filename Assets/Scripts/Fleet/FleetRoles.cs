using System;
using System.Collections.Generic;
using UnityEngine;

// Roles the fleet commander understands. Hardcoded per ship class (design decision):
// adding a class should only need one new line in FleetRoles.Table below.
public enum FleetRole
{
    Scout,    // goes deep at the front
    Anchor,   // sturdy line ship
    Striker   // fragile damage dealer, kept toward the back
}

public struct FleetRoleEntry
{
    public FleetRole Role;

    // 0 = sturdy, 1 = paper. Static, per class. Current health is blended in later
    // (see FleetMember.EffectiveFragility).
    public float StaticFragility;

    public FleetRoleEntry(FleetRole role, float staticFragility)
    {
        Role = role;
        StaticFragility = staticFragility;
    }
}

public static class FleetRoles
{
    // Placeholder fragility numbers, ordered by total HP (Wolf 500, Athena 2000,
    // SwordFish 400). Tune freely; only the ordering matters in Stage 1.
    private static readonly Dictionary<ShipType, FleetRoleEntry> Table =
        new Dictionary<ShipType, FleetRoleEntry>
        {
            { ShipType.WolfClass,      new FleetRoleEntry(FleetRole.Scout,   0.70f) },
            { ShipType.AthenaClass,    new FleetRoleEntry(FleetRole.Anchor,  0.20f) },
            { ShipType.SwordFishClass, new FleetRoleEntry(FleetRole.Striker, 0.80f) },
        };

    private static readonly FleetRoleEntry Fallback = new FleetRoleEntry(FleetRole.Striker, 0.5f);

    public static FleetRoleEntry Get(ShipType type)
    {
        if (Table.TryGetValue(type, out FleetRoleEntry entry))
        {
            return entry;
        }

        Debug.LogWarning($"[AI][Fleet] No role entry for {type}; add it to FleetRoles.Table. Using fallback.");
        return Fallback;
    }

    // ---- Footprint catalog -------------------------------------------------
    // Our own class catalog (built from ShipFactory), NOT enemy data. Used to work out
    // which classes could explain a contact of a given size.

    private static Dictionary<ShipType, int> footprintLengths;
    private static List<ShipType> allTypes;
    private static int maxFootprintLength = 1;

    private static void EnsureCatalog()
    {
        if (footprintLengths != null) return;

        footprintLengths = new Dictionary<ShipType, int>();
        allTypes = new List<ShipType>();

        foreach (ShipType type in Enum.GetValues(typeof(ShipType)))
        {
            ShipInstance card = ShipFactory.CreateShip(type);
            int length = Mathf.Max(1, card.footprintOffsets.Count);
            footprintLengths[type] = length;
            allTypes.Add(type);
            if (length > maxFootprintLength) maxFootprintLength = length;
        }
    }

    public static IReadOnlyList<ShipType> AllTypes
    {
        get { EnsureCatalog(); return allTypes; }
    }

    public static int GetFootprintLength(ShipType type)
    {
        EnsureCatalog();
        return footprintLengths[type];
    }

    public static int MaxFootprintLength
    {
        get { EnsureCatalog(); return maxFootprintLength; }
    }
}