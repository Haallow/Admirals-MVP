using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    [Header("Grid Size")]
    [SerializeField] public int width = 30;
    [SerializeField] public int height = 15;
    [SerializeField] private float cellSize = 1f;

    [Header("Starting Zones")]
    [SerializeField] private int startingZoneWidth = 5;
    [SerializeField] private Color playerZoneColor = new Color(0f, 0.35f, 1f, 0.18f);
    [SerializeField] private Color enemyZoneColor = new Color(1f, 0.1f, 0.1f, 0.18f);

    [Header("Test Ship (movable, owned by PlayerA)")]
    [SerializeField] private ShipType testShipType = ShipType.WolfClass;
    [SerializeField] private ShipInstance testShip;

    [Header("Obstruction Ship (static, owned by PlayerB)")]
    [SerializeField] private ShipType obstructionShipType = ShipType.WolfClass;
    [SerializeField] private ShipInstance obstructionShip;

    private Dictionary<Vector2Int, Tile> tiles = new Dictionary<Vector2Int, Tile>();

    public float CellSize => cellSize;

    private MatchState match;
    public MatchState Match => match;
    public ShipInstance TestShip => match.playerA.ships.Count > 0 ? match.playerA.ships[0] : null;
    public ShipInstance ObstructionShip => match.playerB.ships.Count > 0 ? match.playerB.ships[0] : null;

    [SerializeField] private TurnManager turnManager;

    public FogManager Fog { get; private set; }
    

    private void Awake()
    {
        BuildGrid();
        Fog = new FogManager();
    }

    private void Start()
    {
        var playerA = new PlayerState(PlayerId.PlayerA, new List<ShipType> { ShipType.WolfClass, ShipType.AthenaClass });
        var playerB = new PlayerState(PlayerId.PlayerB, new List<ShipType> { ShipType.WolfClass, ShipType.AthenaClass });
        match = new MatchState(playerA, playerB);
        DeploymentService.DeployAll(match, this);

        if (turnManager != null)
        {
            turnManager.PhaseChanged += HandlePhaseChanged;
        }
    }


    private void LogOccupiedCells(string label, ShipInstance ship)
    {
        foreach (var cell in ship.GetOccupiedCells())
        {
            Debug.Log($"{label} occupies {cell}");
        }
    }

    // Rectangular map for now. Milestone 5 can replace this with predefined layouts.
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

    // Assumes callers have already validated the cells. Use CanPlaceShip before
    // moving or deploying so placement rules stay centralized.
    public void PlaceShip(ShipInstance ship, List<Vector2Int> cells)
    {
        foreach (var cell in cells)
        {
            Debug.Assert(IsInBounds(cell), $"Cell {cell} is out of bounds. Check ship data.");
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

    // Single validation path for deploy/move/rotate. Allows a ship to overlap its
    // own current cells during rotation or same-ship movement checks.
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

    // Atomic move: all validation happens before the grid is mutated.
    public bool MoveShip(ShipInstance ship, Vector2Int newAnchor, int newRotationDegrees)
    {
        int distance = DistanceBetween(ship.anchorAtTurnStart, newAnchor);
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

    // Prototype visualization: cyan is PlayerA's test ship, red is PlayerB.
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying)
        {
            DrawEmptyGridPreview();
            return;
        }

        DrawStartingZones();

        foreach (var kvp in tiles)
        {
            Vector2Int pos = kvp.Key;
            Tile tile = kvp.Value;
            Vector3 worldPos = new Vector3(pos.x * cellSize, pos.y * cellSize, 0f);

            if (tile.Occupant != null)
            {
                Gizmos.color = tile.Occupant.owner == PlayerId.PlayerA ? Color.cyan : Color.red;
                Gizmos.DrawCube(worldPos, Vector3.one * cellSize * 0.9f);
            }
            else
            {
                Gizmos.color = Color.gray;
                Gizmos.DrawWireCube(worldPos, Vector3.one * cellSize * 0.95f);
            }
        }

        DrawDebugCones();
        DrawDebugHalos();
    }

    // Scene-view grid preview before Play Mode builds the runtime tile dictionary.
    private void DrawEmptyGridPreview()
    {
        DrawStartingZones();

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

    private void DrawStartingZones()
    {
        int zoneWidth = Mathf.Clamp(startingZoneWidth, 0, width / 2);

        for (int x = 0; x < width; x++)
        {
            bool isPlayerZone = x < zoneWidth;
            bool isEnemyZone = x >= width - zoneWidth;

            if (!isPlayerZone && !isEnemyZone)
            {
                continue;
            }

            Gizmos.color = isPlayerZone ? playerZoneColor : enemyZoneColor;

            for (int y = 0; y < height; y++)
            {
                Vector3 worldPos = new Vector3(x * cellSize, y * cellSize, 0f);
                Gizmos.DrawCube(worldPos, Vector3.one * cellSize * 0.98f);
            }
        }
    }

    // Temporary combat resolver for the prototype. A later Combat system can own
    // this, but keeping the hook here avoids a larger refactor before fog gating.
    public bool ResolveAttack(ShipInstance attacker, ShipInstance target, WeaponProfile weapon)
    {
        //Guard: dead target can't be hit
        if (target.currentHealth <= 0) return false;

        // Ammo/charge check.
        ChargeState weaponCharge = FindChargeState(attacker.weaponCharges, weapon.id);
        if (weaponCharge == null || !weaponCharge.IsReady)
        {
            return false;
        }

        // Weapon domain check.
        bool domainMatches = weapon.targetDomain == target.currentDomain || weapon.targetDomain == DomainType.Both;
        if (!domainMatches)
        {
            return false;
        }

        // Range check against any occupied target cell.
        int minDistance = int.MaxValue;
        foreach (var targetCell in target.GetOccupiedCells())
        {
            minDistance = Mathf.Min(minDistance, DistanceBetween(attacker.anchor, targetCell));
        }

        if (weapon.weaponRange < minDistance)
        {
            return false;
        }

        // Resolve attacks for targets unknown in the map
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
            RemoveShip(target);
            match.GetPlayer(target.owner).ships.Remove(target);
        }

        return true;
    }

    // Roll one d20 and map it through the weapon's configured roll table.
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

    private void HandlePhaseChanged(Phase newPhase)
    {
        if (newPhase == Phase.Staging)
        {
            Fog.RecomputeAllPassive(match);   // fires once, right as Move ends
        }
        else if (newPhase == Phase.End)
        {
            Fog.ClearAllActiveMarks();        // fires once, before switching back to Move
        } 
        else if (newPhase == Phase.Search)
        {
            Fog.RunActiveSearch(turnManager.CurrentPlayer, match);
        }
        // Staging's actual actions (mines, planes) and End's cooldown/win-check
        // ticks aren't built yet — this only handles what Fog needs today.
    }

    [ContextMenu("Debug Recompute Fog")]
    private void DebugRecomputeFog()
    {
        Fog.RecomputeAllPassive(match);
        foreach (var ship in match.AllShips())
        {
            PlayerId viewer = ship.owner == PlayerId.PlayerA ? PlayerId.PlayerB : PlayerId.PlayerA;
            foreach (var cell in ship.GetOccupiedCells())
                Debug.Log($"{viewer} sees {ship.owner} ship at {cell}: {Fog.GetFogGrid(viewer).GetState(cell)}");
        }
    }


    //Bool function to know whether the tile being attack is unknown or known/detected
    public bool IsTargetKnown(ShipInstance attacker, ShipInstance target)
    {
        FogGrid fog = Fog.GetFogGrid(attacker.owner);
        foreach (var cell in target.GetOccupiedCells())
        {
            if (fog.IsKnown(cell)) return true;
        }
        return false;
    }

    // TEMP (Milestone 5, Step 6): remove after the cone geometry is verified.
    [ContextMenu("Debug Cone Counts")]
    private void DebugConeCounts()
    {
        int r2 = VisionResolver.GetConeCells(new Vector2Int(5, 5), Vector2Int.right, 2).Count;
        int r4 = VisionResolver.GetConeCells(new Vector2Int(5, 5), Vector2Int.right, 4).Count;
        Debug.Log($"Cone counts: r2={r2} (expect 8), r4={r4} (expect 24)");
    }

    private void DrawDebugCones()
    {
        if (match == null) return;
        Gizmos.color = new Color(1f, 0.9f, 0f, 0.6f);

        foreach (var ship in match.AllShips())
        {
            foreach (var layer in ship.visionLayers)
            {
                if (layer.isPassive || layer.shape != ShapeType.Cone) continue;

                VisionResolver.GetBowAndFacing(ship, out Vector2Int bow, out Vector2Int forward);
                foreach (var c in VisionResolver.GetConeCells(bow, forward, layer.range))
                {
                    Gizmos.DrawWireCube(new Vector3(c.x * cellSize, c.y * cellSize, 0f), Vector3.one * cellSize * 0.9f);
                }
            }
        }
    }

    private void DrawDebugHalos()
    {
        if (match == null) return;
        Gizmos.color = new Color(0f, 0.8f, 1f, 1f);

        foreach (var ship in match.AllShips())
        {
            foreach (var layer in ship.visionLayers)
            {
                if (!layer.isPassive || layer.shape != ShapeType.Halo) continue;

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        Vector2Int cell = new Vector2Int(x, y);
                        int dist = Mathf.Max(Mathf.Abs(cell.x - ship.anchor.x), Mathf.Abs(cell.y - ship.anchor.y));
                        if (dist <= layer.range)
                        {
                            Gizmos.DrawWireCube(new Vector3(x * cellSize, y * cellSize, 0f), Vector3.one * cellSize * 0.85f);
                        }
                    }
                }
            }
        }
    }
}
