using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    [Header("Grid Size")]
    [SerializeField] private int width = 10;
    [SerializeField] private int height = 10;
    [SerializeField] private float cellSize = 1f;

    [Header("Test Ship (movable, owned by PlayerA)")]
    [SerializeField] private ShipType testShipType = ShipType.WolfClass;
    [SerializeField] private ShipInstance testShip;

    [Header("Obstruction Ship (static, owned by PlayerB)")]
    [SerializeField] private ShipType obstructionShipType = ShipType.WolfClass;
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
        // Ship card data lives in ShipData/ShipFactory; GridManager only owns
        // runtime placement, grid occupancy, and current prototype interactions.
        testShip = ShipFactory.CreateShip(testShipType);
        testShip.owner = PlayerId.PlayerA;
        testShip.anchor = new Vector2Int(3, 3);
        testShip.rotationDegrees = 0;
        PlaceShip(testShip, testShip.GetOccupiedCells());
        testShip.LogStatBlock($"{testShipType} [PlayerA]");

        obstructionShip = ShipFactory.CreateShip(obstructionShipType);
        obstructionShip.owner = PlayerId.PlayerB;
        obstructionShip.anchor = new Vector2Int(6, 3);
        obstructionShip.rotationDegrees = 0;
        PlaceShip(obstructionShip, obstructionShip.GetOccupiedCells());
        obstructionShip.LogStatBlock($"{obstructionShipType} [PlayerB]");
        LogOccupiedCells($"{obstructionShipType} [PlayerB]", obstructionShip);
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

    // Prototype visualization: cyan is PlayerA's test ship, red is PlayerB.
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

    // Scene-view grid preview before Play Mode builds the runtime tile dictionary.
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

    // Temporary combat resolver for the prototype. A later Combat system can own
    // this, but keeping the hook here avoids a larger refactor before fog gating.
    public bool ResolveAttack(ShipInstance attacker, ShipInstance target, WeaponProfile weapon)
    {
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

        RollTier result = RollWeapon(weapon);
        target.currentHealth -= result.damage;
        Debug.Log($"{attacker.owner} fires {weapon.id} at {target.owner}: {result.outcomeLabel}" + (result.damage > 0 ? $" ({result.damage} dmg)" : ""));

        if (target.currentHealth <= 0)
        {
            Debug.Log($"{target.owner}'s ship destroyed!");
            RemoveShip(target);
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
}
