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
    // Per-ship active scan previews. Follows the same pattern as provisionalMoves.
    private Dictionary<ShipInstance, ActiveScanPreviewState> activeScanPreviews =
        new Dictionary<ShipInstance, ActiveScanPreviewState>();
    // Tracks which ships have already confirmed a scan this Search phase.
    private HashSet<ShipInstance> shipsScannedThisPhase = new HashSet<ShipInstance>();

    public float CellSize => cellSize;

    // Read-only exposure for GridView; nothing outside GridManager mutates this directly.
    public IEnumerable<KeyValuePair<Vector2Int, Tile>> AllTiles => tiles;

    private MatchState match;
    public MatchState Match => match;
    public ShipInstance TestShip => match.playerA.ships.Count > 0 ? match.playerA.ships[0] : null;
    public ShipInstance ObstructionShip => match.playerB.ships.Count > 0 ? match.playerB.ships[0] : null;

    public FogManager Fog { get; private set; }
    public CombatResolver Combat { get; private set; }
    // Read-only access to all active scan previews for GridView rendering.
    public IEnumerable<ActiveScanPreviewState> ActiveScanPreviews => activeScanPreviews.Values;

    private void Awake()
    {
        BuildGrid();
        Fog = new FogManager(this);
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
        var playerA = new PlayerState(PlayerId.PlayerA, new List<ShipType> { ShipType.WolfClass, ShipType.AthenaClass, ShipType.SwordFishClass, ShipType.CarrierClass, ShipType.CruiserClass});
        var playerB = new PlayerState(PlayerId.PlayerB, new List<ShipType> { ShipType.WolfClass, ShipType.AthenaClass });
        match = new MatchState(playerA, playerB);
        DeploymentService.DeployAll(match, this);

        // Ensure scan state is clean regardless of initial Inspector phase value.
        activeScanPreviews.Clear();
        shipsScannedThisPhase.Clear();

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

    // Deploys a mine one cell behind the stern of the given ship.
    // Only valid during Staging. Consumes one mine charge on success.
    // Fails silently (with a log) if any validation check fails — no charge is spent.
    public bool DeployMine(ShipInstance ship)
    {
        if (ship == null)
        {
            return false;
        }

        if (turnManager.CurrentPhase != Phase.Staging)
        {
            Debug.Log($"[MINE] Deploy rejected: not Staging phase.");
            return false;
        }

        if (ship.owner != turnManager.CurrentPlayer)
        {
            Debug.Log($"[MINE] Deploy rejected: not {ship.owner}'s turn.");
            return false;
        }

        // Find a ready mine charge on this ship.
        ChargeState mineCharge = null;
        MineProfile mineProfile = null;
        for (int i = 0; i < ship.mines.Count; i++)
        {
            ChargeState candidate = i < ship.mineCharges.Count ? ship.mineCharges[i] : null;
            // Check remaining > 0 directly — recharge only replenishes spent mines,
            // it does not lock mines that are physically still in storage.
            if (candidate != null && candidate.remaining > 0)
            {
                mineCharge = candidate;
                mineProfile = ship.mines[i];
                break;
            }
        }

        if (mineCharge == null || mineProfile == null)
        {
            Debug.Log($"[MINE] Deploy rejected: {ship.shipType} has no mines in storage.");
            return false;
        }

        // Derive stern cell: occupied cell furthest from the bow in the -forward direction,
        // then one step further back.
        VisionResolver.GetBowAndFacing(ship, out Vector2Int bow, out Vector2Int forward);
        List<Vector2Int> occupied = ship.GetOccupiedCells();

        // Find the stern (cell with maximum distance from bow in the -forward direction).
        Vector2Int stern = bow;
        int maxBackDist = 0;
        foreach (Vector2Int cell in occupied)
        {
            // Project cell onto -forward axis relative to bow.
            int backDist = -(forward.x * (cell.x - bow.x) + forward.y * (cell.y - bow.y));
            if (backDist > maxBackDist)
            {
                maxBackDist = backDist;
                stern = cell;
            }
        }

        Vector2Int deployCell = stern - forward; // one step behind the stern

        // Validate deploy cell.
        if (!IsInBounds(deployCell))
        {
            Debug.Log($"[MINE] Deploy rejected: cell {deployCell} is out of bounds.");
            return false;
        }

        if (!IsTerrainPassable(deployCell))
        {
            Debug.Log($"[MINE] Deploy rejected: cell {deployCell} is impassable terrain.");
            return false;
        }

        if (IsOccupied(deployCell))
        {
            Debug.Log($"[MINE] Deploy rejected: cell {deployCell} is occupied by a ship.");
            return false;
        }

        foreach (MineTile existing in match.mines)
        {
            if (existing.position == deployCell)
            {
                Debug.Log($"[MINE] Deploy rejected: a mine already exists at {deployCell}.");
                return false;
            }
        }

        // All checks passed — deploy.
        match.mines.Add(new MineTile(ship.owner, deployCell, mineProfile.damage));
        mineCharge.remaining--;
        if (mineProfile.rechargeTime > 0)
        {
            mineCharge.turnsUntilRecharge = mineProfile.rechargeTime;
        }
        Debug.Log($"[MINE] {ship.owner} deployed {mineProfile.id} at {deployCell}. Mines remaining: {mineCharge.remaining}" +
                  (mineProfile.rechargeTime > 0 ? $" (recharges in {mineProfile.rechargeTime} turns)" : ""));
        return true;
    }

    // Checks whether the given ship is standing on any mines after moving.
    // Collects all triggered mines first, then applies total damage once,
    // then removes them all — avoids mid-iteration modification and
    // ensures a ship at 0 HP is only destroyed once regardless of mine count.
    private void ResolveMinesFor(ShipInstance ship)
    {
        if (match == null || match.mines.Count == 0) return;

        List<Vector2Int> shipCells = ship.GetOccupiedCells();
        List<MineTile> triggered = new List<MineTile>();

        foreach (MineTile mine in match.mines)
        {
            foreach (Vector2Int cell in shipCells)
            {
                if (mine.position == cell)
                {
                    triggered.Add(mine);
                    break; // one mine can only hit a ship once even if multi-cell
                }
            }
        }

        if (triggered.Count == 0) return;

        int totalDamage = 0;
        foreach (MineTile mine in triggered)
        {
            totalDamage += mine.damage;
            Debug.Log($"[MINE] {ship.owner}'s {ship.shipType} hit mine at {mine.position} — {mine.damage} damage.");
        }

        // Remove triggered mines before applying damage so a destroyed ship
        // doesn't get re-evaluated if something iterates mines again.
        foreach (MineTile mine in triggered)
        {
            match.mines.Remove(mine);
        }

        // Flat damage: bypasses armor and defenses per spec.
        ship.currentHealth -= totalDamage;
        Debug.Log($"[MINE] {ship.owner}'s {ship.shipType} took {totalDamage} total mine damage. HP: {ship.currentHealth}/{ship.maxHealth}");

        if (ship.currentHealth <= 0)
        {
            Debug.Log($"[MINE] {ship.owner}'s {ship.shipType} destroyed by mine(s)!");
            RemoveShip(ship);
            match.GetPlayer(ship.owner).ships.Remove(ship);
        }
    }

    // preferredForward is optional: when supplied (currently only by
    // AIActiveScanPlanner, after scoring the four cardinal facings against
    // AIEnemyMemory and fog coverage), the cone uses that direction instead
    // of the hull's own default facing. TestShipController's manual scan
    // omits it and keeps the original hull-facing behavior unchanged.
    public bool ActivateActiveScan(ShipInstance ship, Vector2Int? preferredForward = null)
    {
        if (ship == null || ship.owner != turnManager.CurrentPlayer)
        {
            return false;
        }

        // Gate to Search phase only.
        if (turnManager.CurrentPhase != Phase.Search)
        {
            return false;
        }

        // One confirmed scan per ship per Search phase.
        if (shipsScannedThisPhase.Contains(ship))
        {
            Debug.Log($"[Scan] {ship.shipType} has already scanned this Search phase.");
            return false;
        }

        VisionLayer layer = null;
        foreach (VisionLayer candidate in ship.visionLayers)
        {
            if (!candidate.isPassive && candidate.shape == ShapeType.Cone)
            {
                layer = candidate;
                break;
            }
        }

        if (layer == null)
        {
            return false;
        }

        VisionResolver.GetBowAndFacing(ship, out Vector2Int bow, out Vector2Int defaultForward);
        Vector2Int forward = preferredForward ?? defaultForward;
        activeScanPreviews[ship] = new ActiveScanPreviewState(ship, layer, bow, forward);
        return true;
    }

    public bool RotateActiveScan(ShipInstance ship, int quarterTurns)
    {
        if (ship == null || quarterTurns == 0)
        {
            return false;
        }

        if (!activeScanPreviews.TryGetValue(ship, out ActiveScanPreviewState preview))
        {
            return false;
        }

        preview.Rotate(quarterTurns);
        return true;
    }

    public bool ConfirmActiveScan(ShipInstance ship)
    {
        if (ship == null || match == null)
        {
            return false;
        }

        // Gate to Search phase only.
        if (turnManager.CurrentPhase != Phase.Search)
        {
            return false;
        }

        if (!activeScanPreviews.TryGetValue(ship, out ActiveScanPreviewState preview))
        {
            return false;
        }

        Fog.RunActiveSearch(
            preview.Ship,
            preview.Layer,
            preview.Bow,
            preview.Forward,
            match);

        activeScanPreviews.Remove(ship);
        shipsScannedThisPhase.Add(ship);
        return true;
    }

    public void CancelActiveScan(ShipInstance ship)
    {
        if (ship != null)
        {
            activeScanPreviews.Remove(ship);
        }
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

        // Resolve mines for every ship that just moved, after all are placed.
        foreach (ProvisionalMovementState state in provisionalMoves.Values)
        {
            // Ship may have been destroyed by an earlier mine this same commit.
            if (state.Ship.currentHealth > 0)
            {
                ResolveMinesFor(state.Ship);
            }
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

        // One-tile exclusion zone: collect all other ships' current preview
        // cells so ships moving on the same turn cannot crowd each other.
        var otherProvisionalCells = new List<Vector2Int>();
        foreach (ProvisionalMovementState otherState in provisionalMoves.Values)
        {
            if (otherState.Ship == ship) continue;
            otherProvisionalCells.AddRange(otherState.GetPreviewCells());
        }

        if (!HasClearExclusionZone(candidateCells, ship, otherProvisionalCells, checkCommittedTiles: false))
        {
            return false;
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

        // One-tile exclusion zone at confirmation: collect all other ships'
        // preview cells so ships cannot end their moves adjacent to each other.
        var otherConfirmedCells = new List<Vector2Int>();
        foreach (ProvisionalMovementState otherState in provisionalMoves.Values)
        {
            if (otherState.Ship == state.Ship) continue;
            otherConfirmedCells.AddRange(otherState.GetPreviewCells());
        }

        if (!HasClearExclusionZone(state.GetPreviewCells(), state.Ship, otherConfirmedCells, checkCommittedTiles: false))
        {
            return false;
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

    // Returns true when none of the candidate cells are within Chebyshev
    // distance 1 of any occupied cell belonging to a different ship.
    // excludeShip is the ship being placed/moved — it is never checked
    // against itself, which allows the ship to overlap its own current cells
    // during rotation and movement validation.
    // checkCommittedTiles: when true, also checks the authoritative tile
    //   dictionary (used by CanPlaceShip for deployment and the AI legacy path).
    //   When false, only provisionalExcludeCells is checked — used by the
    //   preview and confirmation paths so ships are only blocked by other
    //   ships' provisional positions, not their committed start-of-phase tiles.
    // provisionalExcludeCells: other ships' preview footprint cells that have
    //   not yet been committed to the tile dictionary.
    private bool HasClearExclusionZone(
        List<Vector2Int> candidateCells,
        ShipInstance excludeShip,
        List<Vector2Int> provisionalExcludeCells = null,
        bool checkCommittedTiles = true)
    {
        foreach (Vector2Int candidate in candidateCells)
        {
            if (checkCommittedTiles)
            {
                // Check every king-move neighbour in the tile dictionary.
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        Vector2Int neighbour = candidate + new Vector2Int(dx, dy);
                        Tile tile = GetTile(neighbour);
                        if (tile == null || tile.Occupant == null) continue;
                        if (tile.Occupant == excludeShip) continue;

                        return false; // another ship is within 1 tile
                    }
                }
            }

            // Check other ships' provisional preview cells regardless of mode.
            if (provisionalExcludeCells != null)
            {
                foreach (Vector2Int provisionalCell in provisionalExcludeCells)
                {
                    if (DistanceBetween(candidate, provisionalCell) <= 1)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
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

        // Enforce one-tile exclusion zone: no candidate cell may be within
        // Chebyshev distance 1 of any cell belonging to another ship.
        if (!HasClearExclusionZone(candidateCells, ship))
        {
            return false;
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
        ResolveMinesFor(ship);
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
            // Passive vision remains automatic; active scans require player activation.
            Fog.RecomputeAllPassive(match);
            // Clear both the preview dictionary and the per-phase scan limit.
            activeScanPreviews.Clear();
            shipsScannedThisPhase.Clear();
        }
        else if (newPhase == Phase.End)
        {
            Fog.ClearAllActiveMarks();
            // Clear both so abandoned previews and the scan limit don't persist.
            activeScanPreviews.Clear();
            shipsScannedThisPhase.Clear();

            // Tick recharge for the acting player's living ships only.
            // Opposing ships tick at the end of their own player's turn.
            if (match != null)
            {
                List<ShipInstance> actingShips = turnManager.CurrentPlayer == PlayerId.PlayerA
                    ? match.playerA.ships
                    : match.playerB.ships;

                foreach (ShipInstance ship in actingShips)
                {
                    ship.TickRecharge();
                }
            }
        }
        // Staging: mine deployment is player-activated via DeployMine(ship).
        // Planes and repair are not yet built.
    }

}