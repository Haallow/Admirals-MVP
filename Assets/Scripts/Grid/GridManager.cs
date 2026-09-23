using System.Collections.Generic;
using UnityEngine;

// Board logic only, per the roadmap reorg. Combat moved to CombatResolver and
// drawing moved to GridView. TestShip and ObstructionShip remain as temporary
// prototype accessors for AIController.
public class GridManager : MonoBehaviour
{
    [Header("Grid Size")]
    [SerializeField] public int width = 30;
    [SerializeField] public int height = 15;
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private MapDefinition mapDefinition;

    [SerializeField] private TurnManager turnManager;

    private Dictionary<Vector2Int, Tile> tiles = new Dictionary<Vector2Int, Tile>();
    private Dictionary<ShipInstance, ProvisionalMovementState> provisionalMoves =
        new Dictionary<ShipInstance, ProvisionalMovementState>();

    public float CellSize => cellSize;

    // Read-only exposure for GridView; nothing outside GridManager mutates this directly.
    public IEnumerable<KeyValuePair<Vector2Int, Tile>> AllTiles => tiles;

    private MatchState match;
    public MatchState Match => match;
    public ShipInstance TestShip => match.playerA.ships.Count > 0 ? match.playerA.ships[0] : null;
    public ShipInstance ObstructionShip => match.playerB.ships.Count > 0 ? match.playerB.ships[0] : null;

    public FogManager Fog { get; private set; }
    public CombatResolver Combat { get; private set; }

    private void Awake()
    {
        BuildGrid();
        Fog = new FogManager();
        Combat = new CombatResolver(this);
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            BuildGrid();
        }
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

    private void BuildGrid()
    {
        if (mapDefinition != null)
        {
            width = mapDefinition.width;
            height = mapDefinition.height;
        }

        tiles.Clear();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int pos = new Vector2Int(x, y);
                Tile tile = new Tile(pos);
                if (mapDefinition != null &&
                    mapDefinition.TryGetTerrain(pos, out TerrainType terrainType, out int movementCost))
                {
                    tile.SetTerrain(terrainType, movementCost);
                }

                tiles[pos] = tile;
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

    public TerrainType GetTerrainType(Vector2Int pos)
    {
        Tile tile = GetTile(pos);
        return tile != null ? tile.TerrainType : TerrainType.Impassable;
    }

    public bool IsTerrainPassable(Vector2Int pos)
    {
        Tile tile = GetTile(pos);
        return tile != null && tile.IsPassable;
    }

    public int GetTerrainMovementCost(Vector2Int pos)
    {
        Tile tile = GetTile(pos);
        return tile != null ? tile.MovementCost : 0;
    }

    public MovementPathResult CalculateMovementPath(
        Vector2Int start,
        Vector2Int destination,
        int movementBudget,
        ShipInstance movingShip = null)
    {
        return GridPathfinder.FindPath(this, start, destination, movementBudget, movingShip);
    }

    public MovementPathResult CalculateShipMovementPath(ShipInstance ship, Vector2Int destination)
    {
        if (ship == null)
        {
            return MovementPathResult.Unreachable();
        }

        return CalculateMovementPath(ship.anchor, destination, ship.movementRange, ship);
    }

    public HashSet<Vector2Int> CalculateReachableCells(ShipInstance ship)
    {
        if (ship == null)
        {
            return new HashSet<Vector2Int>();
        }

        return GridPathfinder.FindReachableCells(
            this,
            ship.anchor,
            ship.movementRange,
            ship);
    }

    public HashSet<Vector2Int> CalculateReachablePreviewAnchors(
        ProvisionalMovementState state)
    {
        var validAnchors = new HashSet<Vector2Int>();
        if (state == null)
        {
            return validAnchors;
        }

        foreach (Vector2Int anchor in CalculateReachableCells(state.Ship))
        {
            if (IsValidPreviewFootprint(state.Ship, anchor, state.PreviewRotation))
            {
                validAnchors.Add(anchor);
            }
        }

        return validAnchors;
    }

    public IReadOnlyCollection<ProvisionalMovementState> ProvisionalMoves => provisionalMoves.Values;

    public bool PreviewMove(ShipInstance ship, Vector2Int candidateAnchor, int candidateRotation)
    {
        if (ship == null)
        {
            return false;
        }

        EnsureMovementSnapshot(ship);
        ProvisionalMovementState state = provisionalMoves[ship];
        MovementPathResult path = CalculateMovementPath(
            state.OriginalAnchor,
            candidateAnchor,
            ship.movementRange,
            ship);

        bool valid = path.CanMove && IsValidPreviewFootprint(ship, candidateAnchor, candidateRotation);
        if (!valid)
        {
            return false;
        }

        state.SetPreview(candidateAnchor, candidateRotation, path, true);
        return true;
    }

    public void CancelProvisionalMovement()
    {
        provisionalMoves.Clear();
    }

    public bool ConfirmProvisionalMovement()
    {
        foreach (ProvisionalMovementState state in provisionalMoves.Values)
        {
            if (!state.IsValid || !IsValidConfirmationFootprint(state))
            {
                return false;
            }
        }

        Dictionary<ShipInstance, List<Vector2Int>> candidateCells =
            new Dictionary<ShipInstance, List<Vector2Int>>();
        foreach (ProvisionalMovementState state in provisionalMoves.Values)
        {
            candidateCells[state.Ship] = state.GetPreviewCells();
        }

        foreach (KeyValuePair<ShipInstance, List<Vector2Int>> candidate in candidateCells)
        {
            foreach (Vector2Int cell in candidate.Value)
            {
                foreach (KeyValuePair<ShipInstance, List<Vector2Int>> other in candidateCells)
                {
                    if (candidate.Key != other.Key && other.Value.Contains(cell))
                    {
                        return false;
                    }
                }
            }
        }

        foreach (ProvisionalMovementState state in provisionalMoves.Values)
        {
            RemoveShip(state.Ship);
        }

        foreach (ProvisionalMovementState state in provisionalMoves.Values)
        {
            state.Ship.anchor = state.PreviewAnchor;
            state.Ship.rotationDegrees = state.PreviewRotation;
            PlaceShip(state.Ship, candidateCells[state.Ship]);
        }

        provisionalMoves.Clear();
        return true;
    }

    private void EnsureMovementSnapshot(ShipInstance ship)
    {
        if (!provisionalMoves.ContainsKey(ship))
        {
            provisionalMoves[ship] = new ProvisionalMovementState(ship);
        }
    }

    private bool IsValidPreviewFootprint(
        ShipInstance ship,
        Vector2Int candidateAnchor,
        int candidateRotation)
    {
        List<Vector2Int> candidateCells =
            FootprintUtil.GetWorldCells(candidateAnchor, ship.footprintOffsets, candidateRotation);

        foreach (Vector2Int cell in candidateCells)
        {
            if (!IsInBounds(cell) || !IsTerrainPassable(cell))
            {
                return false;
            }

            Tile tile = GetTile(cell);
            if (tile.Occupant != null && tile.Occupant != ship)
            {
                ProvisionalMovementState otherState;
                if (!provisionalMoves.TryGetValue(tile.Occupant, out otherState) ||
                    !otherState.GetPreviewCells().Contains(cell))
                {
                    return false;
                }
            }

            foreach (ProvisionalMovementState otherState in provisionalMoves.Values)
            {
                if (otherState.Ship == ship)
                {
                    continue;
                }

                if (otherState.GetPreviewCells().Contains(cell))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool IsValidConfirmationFootprint(ProvisionalMovementState state)
    {
        List<ShipInstance> movingShips = new List<ShipInstance>(provisionalMoves.Keys);
        foreach (Vector2Int cell in state.GetPreviewCells())
        {
            if (!IsInBounds(cell) || !IsTerrainPassable(cell))
            {
                return false;
            }

            Tile tile = GetTile(cell);
            if (tile.Occupant != null &&
                !movingShips.Contains(tile.Occupant))
            {
                return false;
            }
        }

        return true;
    }

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

    private void HandlePhaseChanged(Phase newPhase)
    {
        if (newPhase == Phase.Move)
        {
            provisionalMoves.Clear();
            if (match != null)
            {
                foreach (ShipInstance ship in turnManager.CurrentPlayer == PlayerId.PlayerA
                    ? match.playerA.ships
                    : match.playerB.ships)
                {
                    provisionalMoves[ship] = new ProvisionalMovementState(ship);
                }
            }
        }
        else if (newPhase == Phase.Staging)
        {
            if (!ConfirmProvisionalMovement())
            {
                Debug.LogWarning("Provisional movement confirmation failed; no ships were moved.");
            }
        }
        else if (newPhase == Phase.Search)
        {
            // Passive refresh first, then this turn's active scan on top of it.
            Fog.RecomputeAllPassive(match);
            Fog.RunActiveSearch(turnManager.CurrentPlayer, match);
        }
        else if (newPhase == Phase.End)
        {
            Fog.ClearAllActiveMarks();
        }
        // Staging's actual actions (mines, planes, repair) aren't built yet.
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
}