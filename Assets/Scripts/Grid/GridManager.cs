using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    [Header("Grid Size")]
    [SerializeField] private int width = 10;
    [SerializeField] private int height = 10;
    [SerializeField] private float cellSize = 1f;
    [SerializeField, Min(0)] private int deploymentDeadSpaceColumns = 2;
    [SerializeField] private TurnManager turnManager;

    [Header("Test Ship (movable, owned by PlayerA)")]
    [SerializeField] private ShipType testShipType = ShipType.WolfClass;
    [SerializeField] private ShipInstance testShip;

    [Header("Obstruction Ship (static, owned by PlayerB)")]
    [SerializeField] private ShipType obstructionShipType = ShipType.WolfClass;
    [SerializeField] private ShipInstance obstructionShip;

    private Dictionary<Vector2Int, Tile> tiles = new Dictionary<Vector2Int, Tile>();
    private ShipInstance deploymentPreviewShip;
    private Vector2Int deploymentPreviewAnchor;
    private int deploymentPreviewRotation;
    private bool showDeploymentPreview;

    private ShipInstance searchPreviewShip;
    private Vector2Int searchPreviewAnchor;
    private bool showSearchPreview;
    private bool searchConfirmed;
    private HashSet<Vector2Int> searchPatternCells = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> searchDetectedCells = new HashSet<Vector2Int>();

    public ShipInstance TestShip => testShip;
    public ShipInstance ObstructionShip => obstructionShip;
    public float CellSize => cellSize;
    public int Width => width;
    public int Height => height;
    public int DeploymentDeadSpaceColumns => deploymentDeadSpaceColumns;

    private void Awake()
    {
        BuildGrid();
    }

    private void Start()
    {
        if (turnManager == null)
        {
            turnManager = FindAnyObjectByType<TurnManager>();
        }

        // Ship card data lives in ShipData/ShipFactory; GridManager only owns
        // runtime placement, grid occupancy, and current prototype interactions.
        testShip = ShipFactory.CreateShip(testShipType);
        testShip.owner = PlayerId.PlayerA;
        testShip.anchor = Vector2Int.zero;
        testShip.rotationDegrees = 0;
        testShip.LogStatBlock($"{testShipType} [PlayerA]");

        obstructionShip = ShipFactory.CreateShip(obstructionShipType);
        obstructionShip.owner = PlayerId.PlayerB;
        obstructionShip.anchor = Vector2Int.zero;
        obstructionShip.rotationDegrees = 0;
        obstructionShip.LogStatBlock($"{obstructionShipType} [PlayerB]");
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

    public bool IsDeploymentZone(PlayerId player, Vector2Int cell, int deadSpaceColumns)
    {
        int safeDeadSpaceColumns = Mathf.Clamp(deadSpaceColumns, 0, width);
        int zoneWidth = (width - safeDeadSpaceColumns) / 2;
        if (zoneWidth <= 0)
        {
            return false;
        }

        if (player == PlayerId.PlayerA)
        {
            return cell.x < zoneWidth;
        }

        int playerBStart = width - zoneWidth;
        return cell.x >= playerBStart;
    }

    public bool CanDeployShip(ShipInstance ship, PlayerId player, Vector2Int candidateAnchor, int candidateRotation, int deadSpaceColumns)
    {
        if (!CanPlaceShip(ship, candidateAnchor, candidateRotation))
        {
            return false;
        }

        foreach (Vector2Int cell in FootprintUtil.GetWorldCells(candidateAnchor, ship.footprintOffsets, candidateRotation))
        {
            if (!IsDeploymentZone(player, cell, deadSpaceColumns))
            {
                return false;
            }
        }

        return true;
    }

    public bool DeployShip(ShipInstance ship, PlayerId player, Vector2Int candidateAnchor, int candidateRotation, int deadSpaceColumns)
    {
        if (ship == null || ship.owner != player || !CanDeployShip(ship, player, candidateAnchor, candidateRotation, deadSpaceColumns))
        {
            return false;
        }

        ship.anchor = candidateAnchor;
        ship.rotationDegrees = candidateRotation;
        PlaceShip(ship, ship.GetOccupiedCells());
        return true;
    }

    public void SetDeploymentPreview(ShipInstance ship, Vector2Int anchor, int rotationDegrees, bool visible)
    {
        deploymentPreviewShip = ship;
        deploymentPreviewAnchor = anchor;
        deploymentPreviewRotation = rotationDegrees;
        showDeploymentPreview = visible;
    }

    public void SetSearchPreview(ShipInstance ship, Vector2Int anchor, bool visible)
    {
        searchPreviewShip = ship;
        searchPreviewAnchor = anchor;
        showSearchPreview = visible;

        if (ship == null || !visible)
        {
            searchPatternCells.Clear();
            searchDetectedCells.Clear();
            return;
        }

        var pattern = ship.GetSelectedSearchPattern();
        searchPatternCells.Clear();

        if (pattern == null)
        {
            searchDetectedCells.Clear();
            return;
        }

        foreach (var cell in pattern.GetCells(anchor, ship.rotationDegrees))
        {
            searchPatternCells.Add(cell);
        }

        if (searchConfirmed)
        {
            searchDetectedCells.Clear();
            ShipInstance enemy = GetEnemyShip(ship);
            if (enemy != null)
            {
                foreach (var cell in enemy.GetOccupiedCells())
                {
                    if (searchPatternCells.Contains(cell))
                    {
                        searchDetectedCells.Add(cell);
                    }
                }
            }
        }
        else
        {
            searchDetectedCells.Clear();
        }
    }

    public void ClearSearchState()
    {
        searchConfirmed = false;
        searchPatternCells.Clear();
        searchDetectedCells.Clear();
        showSearchPreview = false;
    }

    public List<Vector2Int> ResolveSearch(ShipInstance ship, Vector2Int anchor)
    {
        if (ship == null)
        {
            return new List<Vector2Int>();
        }

        SearchPatternDefinition pattern = ship.GetSelectedSearchPattern();
        if (pattern == null || !pattern.IsAvailable(ship))
        {
            return new List<Vector2Int>();
        }

        ShipInstance enemy = GetEnemyShip(ship);
        if (enemy == null)
        {
            return new List<Vector2Int>();
        }

        List<Vector2Int> detected = new List<Vector2Int>();
        foreach (var cell in pattern.GetCells(anchor, ship.rotationDegrees))
        {
            if (enemy.GetOccupiedCells().Contains(cell))
            {
                detected.Add(cell);
            }
        }

        searchPatternCells.Clear();
        searchDetectedCells.Clear();
        foreach (var cell in pattern.GetCells(anchor, ship.rotationDegrees))
        {
            searchPatternCells.Add(cell);
        }

        foreach (var cell in detected)
        {
            searchDetectedCells.Add(cell);
        }

        searchConfirmed = true;
        showSearchPreview = true;

        Debug.Log($"[Search] {ship.owner} scanned {anchor} with {pattern.id}. Detected {detected.Count} enemy tile(s): {string.Join(", ", detected)}");
        return detected;
    }

    public ShipInstance GetEnemyShip(ShipInstance ship)
    {
        if (ship == null)
        {
            return null;
        }

        if (ship.owner == PlayerId.PlayerA)
        {
            return obstructionShip;
        }

        return testShip;
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
            DrawDeploymentDeadSpace();
            return;
        }

        foreach (var kvp in tiles)
        {
            Vector2Int pos = kvp.Key;
            Tile tile = kvp.Value;
            Vector3 worldPos = new Vector3(pos.x * cellSize, pos.y * cellSize, 0f);

            bool hidePlayerBShip = turnManager != null &&
                turnManager.CurrentPhase == Phase.Deployment &&
                tile.Occupant != null &&
                tile.Occupant.owner == PlayerId.PlayerB;

            if (tile.Occupant != null && !hidePlayerBShip)
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

        DrawDeploymentDeadSpace();

        if (showDeploymentPreview && deploymentPreviewShip != null)
        {
            Gizmos.color = new Color(0.15f, 0.55f, 1f, 0.35f);
            foreach (Vector2Int cell in FootprintUtil.GetWorldCells(deploymentPreviewAnchor, deploymentPreviewShip.footprintOffsets, deploymentPreviewRotation))
            {
                Vector3 worldPos = new Vector3(cell.x * cellSize, cell.y * cellSize, -0.1f);
                Gizmos.DrawCube(worldPos, Vector3.one * cellSize * 0.9f);
            }
        }

        if (showSearchPreview && searchPreviewShip != null)
        {
            SearchPatternDefinition pattern = searchPreviewShip.GetSelectedSearchPattern();
            if (pattern != null)
            {
                if (!searchConfirmed)
                {
                    Gizmos.color = new Color(0.15f, 0.75f, 1f, 0.28f);
                    foreach (var cell in pattern.GetCells(searchPreviewAnchor, searchPreviewShip.rotationDegrees))
                    {
                        Vector3 worldPos = new Vector3(cell.x * cellSize, cell.y * cellSize, -0.2f);
                        Gizmos.DrawCube(worldPos, Vector3.one * cellSize * 0.8f);
                    }
                }

                if (searchConfirmed)
                {
                    Gizmos.color = new Color(1f, 0.55f, 0f, 0.85f);
                    foreach (var detectedCell in searchDetectedCells)
                    {
                        Vector3 worldPos = new Vector3(detectedCell.x * cellSize, detectedCell.y * cellSize, -0.15f);
                        Gizmos.DrawCube(worldPos, Vector3.one * cellSize * 0.7f);
                    }
                }
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

    private void DrawDeploymentDeadSpace()
    {
        int safeDeadSpaceColumns = Mathf.Clamp(deploymentDeadSpaceColumns, 0, width);
        int zoneWidth = (width - safeDeadSpaceColumns) / 2;
        if (zoneWidth <= 0)
        {
            return;
        }

        Gizmos.color = new Color(1f, 0.75f, 0.15f, 0.18f);
        for (int x = zoneWidth; x < width - zoneWidth; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector3 worldPos = new Vector3(x * cellSize, y * cellSize, 0.1f);
                Gizmos.DrawCube(worldPos, Vector3.one * cellSize * 0.9f);
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
