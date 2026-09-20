using System.Collections.Generic;
using UnityEngine;

public static class ShipData
{
    public static void BuildWolfClass(ShipInstance ship)
    {
        // --- Core stats ---
        ship.maxHealth = 1000;
        ship.armor = 50;
        ship.movementRange = 3;
        ship.currentDomain = DomainType.Surface;

        ship.footprintOffsets = new List<Vector2Int>
        {
            new Vector2Int(0, 0),
            new Vector2Int(1, 0)
        };

        // --- Weapons ---
        ship.weapons = new List<WeaponProfile>
        {
            // MRK-1 Torpedo
            new WeaponProfile(
                id: "MRK-1 Torpedo",
                ammo: 4,
                weaponRange: 4,
                targetDomain: DomainType.SubSurface,
                rollTiers: new List<RollTier>
                {
                    new RollTier(1, 10, "Miss", 0),
                    new RollTier(11, 20, "Direct Hit", 800)
                }
            ),

            // Spear Anti-Ship Missile
            new WeaponProfile(
                id: "Spear Anti-Ship Missile",
                ammo: 2,
                weaponRange: 7,
                targetDomain: DomainType.Surface,
                rollTiers: new List<RollTier>
                {
                    new RollTier(1, 3, "Miss", 0),
                    new RollTier(4, 17, "Direct Hit", 500),
                    new RollTier(18, 20, "Catastrophic Hit", 750)
                }
            ),

            // Hippocampus Torpedo
            new WeaponProfile(
                id: "Hippocampus Torpedo",
                ammo: 3,
                weaponRange: 5,
                targetDomain: DomainType.Both,
                rollTiers: new List<RollTier>
                {
                    new RollTier(1, 6, "Miss", 0),
                    new RollTier(7, 16, "Direct Hit", 1000),
                    new RollTier(17, 20, "Catastrophic Hit", 1500)
                }
            )
        };

        ship.defenses = new List<DefenseProfile>
        {
            // Crash Dive - infinite uses, counters surface attacks, forces sub-surface + skip move later.
            new DefenseProfile(
                id: "Crash Dive",
                uses: null,
                validAgainst: DomainType.Surface,
                savingThrowTiers: new List<RollTier>
                {
                    new RollTier(1, 10, "Fail"),
                    new RollTier(11, 20, "Hit Avoided")
                },
                sideEffectId: "BecomeSubSurfaceAndSkipNextMove"
            ),

            // Acoustic Decoys - limited-use sub-surface defense.
            new DefenseProfile(
                id: "Acoustic Decoys",
                uses: 2,
                validAgainst: DomainType.SubSurface,
                savingThrowTiers: new List<RollTier>
                {
                    new RollTier(1, 10, "Fail"),
                    new RollTier(11, 20, "Hit Avoided")
                }
            ),

            // Deep Dive - infinite sub-surface defense with a harder saving throw.
            new DefenseProfile(
                id: "Deep Dive",
                uses: null,
                validAgainst: DomainType.SubSurface,
                savingThrowTiers: new List<RollTier>
                {
                    new RollTier(1, 15, "Fail"),
                    new RollTier(16, 20, "Hit Avoided")
                }
            )
        };

        ship.visionLayers = new List<VisionLayer>
        {
            // Passive Sonar - always-on local sensor vision.
            new VisionLayer(
                id: "Passive Sonar",
                shape: ShapeType.Halo,
                range: 2,
                detects: DomainType.Both,
                visionType: VisionType.Sensor,
                isPassive: true
            ),

            // Default Absolute Vision - close-range identification while surfaced.
            new VisionLayer(
                id: "Default Absolute Vision",
                shape: ShapeType.Halo,
                range: 1,
                detects: DomainType.Both,
                visionType: VisionType.Absolute,
                isPassive: true,
                onlyWhileSurfaced: true
            ),

            // Active Search Sonar - longer-range Search phase scan.
            new VisionLayer(
                id: "Active Search Sonar",
                shape: ShapeType.Cone,
                range: 4,
                detects: DomainType.Both,
                visionType: VisionType.Sensor,
                isPassive: false
            )
        };

        ship.searchPatterns = SearchPatternCatalog.BuildDefaultPatterns();
        ship.selectedSearchPatternIndex = 0;

        ship.InitializeCharges();
    }


    public static void BuildAthenaClass(ShipInstance ship)
    {
        // --- Core stats ---
        ship.maxHealth = 700;
        ship.armor = 80;
        ship.movementRange = 2;
        ship.currentDomain = DomainType.Surface;

        ship.footprintOffsets = new List<Vector2Int>
        {
            new Vector2Int(0, 0),
            new Vector2Int(1, 0),
            new Vector2Int(2, 0)
        };

        // --- Weapons ---
        ship.weapons = new List<WeaponProfile>
        {
            new WeaponProfile(
                id: "Athena Cannon",
                ammo: 5,
                weaponRange: 6,
                targetDomain: DomainType.Surface,
                rollTiers: new List<RollTier>
                {
                    new RollTier(1, 5, "Miss", 0),
                    new RollTier(6, 17, "Direct Hit", 600),
                    new RollTier(18, 20, "Catastrophic Hit", 900)
                }
            )
        };

        ship.defenses = new List<DefenseProfile>();
        ship.visionLayers = new List<VisionLayer>();
        ship.InitializeCharges();
    }


    public static void BuildSwordFishClass(ShipInstance ship)
    {
        // --- Core stats ---
        ship.maxHealth = 400;
        ship.armor = 30;
        ship.movementRange = 4;
        ship.currentDomain = DomainType.Surface;

        ship.footprintOffsets = new List<Vector2Int>
        {
            new Vector2Int(0, 0),
            new Vector2Int(1, 0)
        };

        // --- Weapons ---
        ship.weapons = new List<WeaponProfile>
        {
            new WeaponProfile(
                id: "Sword Fish Torpedo",
                ammo: 3,
                weaponRange: 5,
                targetDomain: DomainType.SubSurface,
                rollTiers: new List<RollTier>
                {
                    new RollTier(1, 7, "Miss", 0),
                    new RollTier(8, 17, "Direct Hit", 700),
                    new RollTier(18, 20, "Catastrophic Hit", 1000)
                }
            )
        };

        ship.defenses = new List<DefenseProfile>();
        ship.visionLayers = new List<VisionLayer>();
        ship.InitializeCharges();
    }
}
