    using System.Collections.Generic;
    using UnityEngine;

    public class GridManager : MonoBehaviour
    {
        [Header("Grid Size")]
        [SerializeField] private int width = 10;
        [SerializeField] private int height = 10;
        [SerializeField] private float cellSize = 1f;

        [Header("Hardcoded Test Ship (movable, owned by PlayerA)")]
        [SerializeField] private ShipInstance testShip;

        [Header("Hardcoded Obstruction Ship (static, owned by PlayerB)")]
        [SerializeField] private ShipInstance obstructionShip;

        private Dictionary<Vector2Int, Tile> tiles = new Dictionary<Vector2Int, Tile>();

        public ShipInstance TestShip => testShip;

        public ShipInstance ObstructionShip => obstructionShip;

        public float CellSize => cellSize;

        private void Awake()
        {
            BuildGrid();
        }

        private void Start()
        {
            // --- Wolf Class test ship (PlayerA) ---
            BuildWolfClassTestShip(testShip);
            testShip.owner = PlayerId.PlayerA;
            testShip.anchor = new Vector2Int(3, 3);
            testShip.rotationDegrees = 0;
            PlaceShip(testShip, testShip.GetOccupiedCells());
            testShip.LogStatBlock("Wolf Class [PlayerA]");

            // --- Simple obstruction ship (PlayerB, no combat data needed yet) ---
            BuildWolfClassTestShip(obstructionShip);
            obstructionShip.owner = PlayerId.PlayerB;
            obstructionShip.anchor = new Vector2Int(6, 3);
            obstructionShip.rotationDegrees = 0;
            obstructionShip.maxHealth = 100;
            obstructionShip.currentHealth = 100;
            PlaceShip(obstructionShip, obstructionShip.GetOccupiedCells());
            LogOccupiedCells("Obstruction ship (PlayerB)", obstructionShip);
        }

        // Populates every field of the Wolf Class using the real card numbers.
        // Health 500, Armor 50, 1x2 footprint, 3 weapons, 3 defenses, 3 vision layers.
        private void BuildWolfClassTestShip(ShipInstance ship)
        {
            // --- Core stats ---
            ship.maxHealth = 500;
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
                // MRK-1 Torpedo — sub-surface only, 4 ammo
                new WeaponProfile(
                    id: "MRK-1 Torpedo",
                    ammo: 4,
                    weaponRange: 4,
                    targetDomain: DomainType.SubSurface,
                    rollTiers: new List<RollTier>
                    {
                        new RollTier(1,  10, "Miss",       0),
                        new RollTier(11, 20, "Direct Hit", 800)
                    }
                ),

                // Spear Anti-Ship Missile — surface only, 2 ammo
                new WeaponProfile(
                    id: "Spear Anti-Ship Missile",
                    ammo: 2,
                    weaponRange: 7,
                    targetDomain: DomainType.Surface,
                    rollTiers: new List<RollTier>
                    {
                        new RollTier(1,  3,  "Miss",             0),
                        new RollTier(4,  17, "Direct Hit",       500),
                        new RollTier(18, 20, "Catastrophic Hit", 750)
                    }
                ),

                // Hippocampus Torpedo — both domains, 3 ammo
                new WeaponProfile(
                    id: "Hippocampus Torpedo",
                    ammo: 3,
                    weaponRange: 5,
                    targetDomain: DomainType.Both,
                    rollTiers: new List<RollTier>
                    {
                        new RollTier(1,  6,  "Miss",             0),
                        new RollTier(7,  16, "Direct Hit",       1000),
                        new RollTier(17, 20, "Catastrophic Hit", 1500)
                    }
                )
            };

            /*
            // --- Defenses ---
            ship.defenses = new List<DefenseProfile>
            {
                // Crash Dive — infinite uses, counters surface attacks, forces sub-surface + skip move
                new DefenseProfile(
                    id: "Crash Dive",
                    uses: null,
                    validAgainst: DomainType.Surface,
                    savingThrowTiers: new List<RollTier>
                    {
                        new RollTier(1,  10, "Fail"),
                        new RollTier(11, 20, "Hit Avoided")
                    },
                    sideEffectId: "BecomeSubSurfaceAndSkipNextMove"
                ),

                // Acoustic Decoys — 2 uses, counters sub-surface attacks
                new DefenseProfile(
                    id: "Acoustic Decoys",
                    uses: 2,
                    validAgainst: DomainType.SubSurface,
                    savingThrowTiers: new List<RollTier>
                    {
                        new RollTier(1,  10, "Fail"),
                        new RollTier(11, 20, "Hit Avoided")
                    }
                ),

                // Deep Dive — infinite uses, counters sub-surface attacks, harder to avoid
                new DefenseProfile(
                    id: "Deep Dive",
                    uses: null,
                    validAgainst: DomainType.SubSurface,
                    savingThrowTiers: new List<RollTier>
                    {
                        new RollTier(1,  15, "Fail"),
                        new RollTier(16, 20, "Hit Avoided")
                    }
                )
            };
            */
            
            /*
            // --- Vision layers ---
            ship.visionLayers = new List<VisionLayer>
            {
                // Passive Sonar — always-on halo, detects both domains
                new VisionLayer(
                    id: "Passive Sonar",
                    shape: ShapeType.Halo,
                    range: 2,
                    detects: DomainType.Both,
                    visionType: VisionType.Sensor,
                    isPassive: true
                ),

                // Default Absolute Vision — always-on halo at range 1, but only meaningful while surfaced
                new VisionLayer(
                    id: "Default Absolute Vision",
                    shape: ShapeType.Halo,
                    range: 1,
                    detects: DomainType.Both,
                    visionType: VisionType.Absolute,
                    isPassive: true,
                    onlyWhileSurfaced: true
                ),

                // Active Search Sonar — cone, consumes Search phase
                new VisionLayer(
                    id: "Active Search Sonar",
                    shape: ShapeType.Cone,
                    range: 4,
                    detects: DomainType.Both,
                    visionType: VisionType.Sensor,
                    isPassive: false
                )
            };
            */

            ship.InitializeCharges();
        }

        private void LogOccupiedCells(string label, ShipInstance ship)
        {
            foreach (var cell in ship.GetOccupiedCells())
            {
                Debug.Log($"{label} occupies {cell}");
            }
        }

        private void BuildGrid()
        {
            tiles.Clear();
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Vector2Int pos = new Vector2Int(x, y);
                    tiles[pos] = new Tile(pos);
                }
            }
        }

        public bool IsInBounds(Vector2Int pos)
        {
            return pos.x >= 0 && pos.x < width && pos.y >= 0 && pos.y < height;
        }

        public bool IsOccupied(Vector2Int pos)
        {
            Tile tile = GetTile(pos);
            return tile != null && tile.Occupant != null;
        }

        public Tile GetTile(Vector2Int pos)
        {
            tiles.TryGetValue(pos, out Tile tile);
            return tile;
        }

        public void PlaceShip(ShipInstance ship, List<Vector2Int> cells)
        {
            foreach (var cell in cells)
            {
                Debug.Assert(IsInBounds(cell), $"Cell {cell} is out of bounds. Check hardcoded ship data.");
                Tile tile = GetTile(cell);
                if (tile != null)
                {
                    tile.Occupant = ship;
                }
            }
        }

        public void RemoveShip(ShipInstance ship)
        {
            foreach (var tile in tiles.Values)
            {
                if (tile.Occupant == ship)
                {
                    tile.Occupant = null;
                }
            }
        }

        public bool CanPlaceShip(ShipInstance ship, Vector2Int candidateAnchor, int candidateRotation)
        {
            List<Vector2Int> candidateCells = FootprintUtil.GetWorldCells(candidateAnchor, ship.footprintOffsets, candidateRotation);

            foreach (var cell in candidateCells)
            {
                if (!IsInBounds(cell))
                {
                    return false;
                }

                Tile tile = GetTile(cell);
                if (tile.Occupant != null && tile.Occupant != ship)
                {
                    return false;
                }
            }

            return true;
        }

        public int DistanceBetween(Vector2Int a, Vector2Int b)
        {
            return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        }

        public bool MoveShip(ShipInstance ship, Vector2Int newAnchor, int newRotationDegrees)
        {
            int distance = DistanceBetween(ship.anchor, newAnchor);
            if (distance > ship.movementRange)
            {
                Debug.Log($"Move rejected: distance {distance} exceeds movement range {ship.movementRange}.");
                return false;
            }

            if (!CanPlaceShip(ship, newAnchor, newRotationDegrees))
            {
                Debug.Log($"Move rejected: target cells at {newAnchor} (rotation {newRotationDegrees}) are out of bounds or occupied.");
                return false;
            }

            RemoveShip(ship);
            ship.anchor = newAnchor;
            ship.rotationDegrees = newRotationDegrees;
            PlaceShip(ship, ship.GetOccupiedCells());
            return true;
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
            {
                DrawEmptyGridPreview();
                return;
            }

            foreach (var kvp in tiles)
            {
                Vector2Int pos = kvp.Key;
                Tile tile = kvp.Value;
                Vector3 worldPos = new Vector3(pos.x * cellSize, pos.y * cellSize, 0f);

                if (tile.Occupant != null)
                {
                    Gizmos.color = tile.Occupant == testShip ? Color.cyan : Color.red;
                    Gizmos.DrawCube(worldPos, Vector3.one * cellSize * 0.9f);
                }
                else
                {
                    Gizmos.color = Color.gray;
                    Gizmos.DrawWireCube(worldPos, Vector3.one * cellSize * 0.95f);
                }
            }
        }

        private void DrawEmptyGridPreview()
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Vector3 worldPos = new Vector3(x * cellSize, y * cellSize, 0f);
                    Gizmos.color = Color.gray;
                    Gizmos.DrawWireCube(worldPos, Vector3.one * cellSize * 0.95f);
                }
            }
        }


        //not really meant to be inside GridManager: fix later
        public bool ResolveAttack(ShipInstance attacker, ShipInstance target, WeaponProfile weapon)
        {
            //Checking if the attackers weapon has ammo
            ChargeState weaponCharge = FindChargeState(attacker.weaponCharges, weapon.id);
            if (weaponCharge == null || !weaponCharge.IsReady)
            {
                return false;
            }

            //Weapon Domain Check: Subsurface
            bool domainMatches = weapon.targetDomain == target.currentDomain || weapon.targetDomain == DomainType.Both;
            if (!domainMatches)
            {
                return false;
            }

            //Check target if is within weapon range
            int minDistance = 1000;
            foreach (var targetCells in target.GetOccupiedCells())
            {
                minDistance = Mathf.Min(minDistance, DistanceBetween(attacker.anchor, targetCells));
            }
            if (weapon.weaponRange < minDistance)
            {
                return false;
            }

            RollTier result = RollWeapon(weapon);
            target.currentHealth -= result.damage;
            Debug.Log($"{attacker.owner} fires {weapon.id} at {target.owner}: {result.outcomeLabel}" + (result.damage > 0 ? $" ({result.damage} dmg)" : ""));

            if (target.currentHealth <= 0)
            {
                Debug.Log($"{target.owner}'s ship destroyed!");
                RemoveShip(target); // clears it off the grid, stops it being drawn/targetable
            }

            return true;
            
        }

        //not really meant to be inside GridManager: fix later
        //Roll Dice
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
            Debug.LogWarning($"Roll {roll} didn't match any tier on {weapon.id}. Check tier ranges.");
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

    }

        