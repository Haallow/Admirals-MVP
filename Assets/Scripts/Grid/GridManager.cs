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
        if (newPhase == Phase.Search)
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