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
    private ActiveScanPreviewState activeScanPreview;
    // Fleet active scan limit: at most one confirmed active scan per player per Search phase.
    private bool fleetScannedThisPhase = false;

    public float CellSize => cellSize;

    // Read-only exposure for GridView; nothing outside GridManager mutates this directly.
    public IEnumerable<KeyValuePair<Vector2Int, Tile>> AllTiles => tiles;

    private MatchState match;
    public MatchState Match => match;
    public ShipInstance TestShip => match.playerA.ships.Count > 0 ? match.playerA.ships[0] : null;
    public ShipInstance ObstructionShip => match.playerB.ships.Count > 0 ? match.playerB.ships[0] : null;

    public FogManager Fog { get; private set; }
    public CombatResolver Combat { get; private set; }
    public ActiveScanPreviewState ActiveScanPreview => activeScanPreview;
    public TurnManager TurnManager => turnManager;

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
    // Fails silently (with a log) if any validation check fails â€” no charge is spent.
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
            // Check remaining > 0 directly â€” recharge only replenishes spent mines,
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

        // All checks passed â€” deploy.
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

    // Deploys a reconnaissance plane to the given target cell during Staging.
    // The plane is launched from a Carrier â€” launch range is validated as
    // Chebyshev distance from any hull cell to the target cell.
    // Planes do NOT occupy tiles (they are airborne).
    public bool DeployPlane(ShipInstance ship, Vector2Int targetCell)
    {
        if (ship == null)
        {
            return false;
        }

        if (turnManager.CurrentPhase != Phase.Staging)
        {
            Debug.Log($"[PLANE] Deploy rejected: not Staging phase.");
            return false;
        }

        if (ship.owner != turnManager.CurrentPlayer)
        {
            Debug.Log($"[PLANE] Deploy rejected: not {ship.owner}'s turn.");
            return false;
        }

        // Find a ready plane charge on this ship.
        ChargeState planeCharge = null;
        PlaneProfile planeProfile = null;
        for (int i = 0; i < ship.planes.Count; i++)
        {
            ChargeState candidate = i < ship.planeCharges.Count ? ship.planeCharges[i] : null;
            if (candidate != null && candidate.remaining > 0)
            {
                planeCharge = candidate;
                planeProfile = ship.planes[i];
                break;
            }
        }

        if (planeCharge == null || planeProfile == null)
        {
            Debug.Log($"[PLANE] Deploy rejected: {ship.shipType} has no sorties remaining.");
            return false;
        }

        // Validate target cell: in bounds.
        if (!IsInBounds(targetCell))
        {
            Debug.Log($"[PLANE] Deploy rejected: cell {targetCell} is out of bounds.");
            return false;
        }

        // Validate target cell: not Impassable terrain (per Test 3 spec).
        if (!IsTerrainPassable(targetCell))
        {
            Debug.Log($"[PLANE] Deploy rejected: cell {targetCell} is impassable terrain.");
            return false;
        }

        // Launch range check: minimum Chebyshev distance from any hull cell
        // to targetCell must be <= profile.launchRange.
        List<Vector2Int> hullCells = ship.GetOccupiedCells();
        int minDist = int.MaxValue;
        foreach (Vector2Int hullCell in hullCells)
        {
            int dist = DistanceBetween(hullCell, targetCell);
            if (dist < minDist) minDist = dist;
        }

        if (minDist > planeProfile.launchRange)
        {
            Debug.Log($"[PLANE] Deploy rejected: cell {targetCell} is out of launch range " +
                      $"(distance {minDist} > launch range {planeProfile.launchRange}).");
            return false;
        }

        // All checks passed — deploy.
        // Planes do NOT check for ship occupancy — they fly over ships.
        PlaneUnit plane = new PlaneUnit(
            ship.owner, targetCell, planeProfile.fuelTurns,
            planeProfile.movementRange, planeProfile.visionRange,
            ship, planeProfile.id);
        plane.deployedThisTurn = true;
        match.planes.Add(plane);

        planeCharge.remaining--;
        Debug.Log($"[PLANE] {ship.owner} deployed {planeProfile.id} at {targetCell}. " +
                  $"Launch distance: {minDist}. Fuel: {planeProfile.fuelTurns}. " +
                  $"Sorties remaining: {planeCharge.remaining}");

        if (Fog != null && match != null)
        {
            Fog.RecomputeAllPassive(match);
        }

        return true;
    }

    // Previews a plane move during Staging phase.
    // Validates Chebyshev distance from positionAtTurnStart.
    // Planes do not interact with tiles, exclusion zones, or other ships.
    public bool PreviewPlaneMove(PlaneUnit plane, Vector2Int candidatePosition)
    {
        if (plane == null)
        {
            return false;
        }

        if (turnManager != null && turnManager.CurrentPhase != Phase.Staging)
        {
            Debug.Log($"[PLANE] Move rejected: planes can only move in the Staging phase (current: {turnManager.CurrentPhase}).");
            return false;
        }

        if (turnManager != null && plane.owner != turnManager.CurrentPlayer)
        {
            Debug.Log($"[PLANE] Move rejected: it is not {plane.owner}'s turn.");
            return false;
        }

        if (plane.deployedThisTurn)
        {
            Debug.Log("[PLANE] Move rejected: planes cannot move on the turn they are deployed (movement is allowed in the next Staging phase).");
            return false;
        }

        int dist = DistanceBetween(plane.positionAtTurnStart, candidatePosition);
        if (dist > plane.movementRange)
        {
            Debug.Log($"[PLANE] Move rejected: distance {dist} exceeds movement range {plane.movementRange}.");
            return false;
        }

        if (!IsInBounds(candidatePosition))
        {
            Debug.Log($"[PLANE] Move rejected: cell {candidatePosition} is out of bounds.");
            return false;
        }

        // Planes fly over everything â€” no terrain or occupancy check.
        plane.position = candidatePosition;
        return true;
    }

    // Commits a plane move.
    public void ConfirmPlaneMove(PlaneUnit plane)
    {
        if (plane == null) return;
        Debug.Log($"[PLANE] Plane move committed to {plane.position}.");
    }

    // Undeploys a plane that was deployed during the current Staging phase.
    // Removes the plane from match.planes, refunds the sortie to the launch ship,
    // and recomputes passive fog.
    public bool UndeployPlane(PlaneUnit plane)
    {
        if (plane == null || match == null)
        {
            return false;
        }

        if (turnManager != null && turnManager.CurrentPhase != Phase.Staging)
        {
            Debug.Log("[PLANE] Undeploy rejected: can only undeploy during Staging phase.");
            return false;
        }

        if (turnManager != null && plane.owner != turnManager.CurrentPlayer)
        {
            Debug.Log($"[PLANE] Undeploy rejected: not {plane.owner}'s turn.");
            return false;
        }

        if (!plane.deployedThisTurn)
        {
            Debug.Log("[PLANE] Undeploy rejected: plane was deployed on a previous turn and cannot be undeployed.");
            return false;
        }

        if (!match.planes.Contains(plane))
        {
            return false;
        }

        match.planes.Remove(plane);

        // Refund sortie to launch ship
        if (plane.launchedFrom != null)
        {
            foreach (ChargeState charge in plane.launchedFrom.planeCharges)
            {
                if (charge.profileId == plane.profileId)
                {
                    if (charge.maxCapacity == -1 || charge.remaining < charge.maxCapacity)
                    {
                        charge.remaining++;
                    }
                    Debug.Log($"[PLANE] Sortie refunded to {plane.launchedFrom.shipType}. Remaining: {charge.remaining}");
                    break;
                }
            }
        }

        if (Fog != null)
        {
            Fog.RecomputeAllPassive(match);
        }

        Debug.Log($"[PLANE] Plane at {plane.position} undeployed.");
        return true;
    }

    // Checks whether the given ship is standing on any mines after moving.
    // Collects all triggered mines first, then applies total damage once,
    // then removes them all â€” avoids mid-iteration modification and
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
            Debug.Log($"[MINE] {ship.owner}'s {ship.shipType} hit mine at {mine.position} â€” {mine.damage} damage.");
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
            // Clears passive and active fog marks for this ship so destroyed targets don't retain ghost contact markers.
            Fog?.ClearMarksForShip(ship);
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

        // Fleet scan limit: once any ship has confirmed a scan this Search phase, no other ship can scan.
        if (fleetScannedThisPhase)
        {
            Debug.Log($"[Scan] {ship.owner} has already performed an active scan this Search phase (fleet scan limit: 1).");
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
        activeScanPreview = new ActiveScanPreviewState(ship, layer, bow, forward);
        return true;
    }

    public bool RotateActiveScan(int quarterTurns)
    {
        if (activeScanPreview == null || quarterTurns == 0)
        {
            return false;
        }

        activeScanPreview.Rotate(quarterTurns);
        return true;
    }

    public bool RotateActiveScan(ShipInstance ship, int quarterTurns)
    {
        return RotateActiveScan(quarterTurns);
    }

    public bool SetActiveScanForward(Vector2Int forward)
    {
        if (activeScanPreview == null || forward == Vector2Int.zero)
        {
            return false;
        }

        activeScanPreview.SetForward(forward);
        return true;
    }

    public bool SetActiveScanForward(ShipInstance ship, Vector2Int forward)
    {
        return SetActiveScanForward(forward);
    }

    public bool ConfirmActiveScan()
    {
        if (activeScanPreview == null || match == null)
        {
            return false;
        }

        // Gate to Search phase only.
        if (turnManager.CurrentPhase != Phase.Search)
        {
            return false;
        }

        if (fleetScannedThisPhase)
        {
            return false;
        }

        Fog.RunActiveSearch(
            activeScanPreview.Ship,
            activeScanPreview.Layer,
            activeScanPreview.Bow,
            activeScanPreview.Forward,
            match);
        activeScanPreview = null;
        fleetScannedThisPhase = true;
        return true;
    }

    public bool ConfirmActiveScan(ShipInstance ship)
    {
        return ConfirmActiveScan();
    }

    public void CancelActiveScan()
    {
        activeScanPreview = null;
    }

    public void CancelActiveScan(ShipInstance ship)
    {
        CancelActiveScan();
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
            if (match != null && turnManager.CurrentPlayer == PlayerId.PlayerA)
            {
                foreach (ShipInstance ship in match.playerA.ships)
                {
                    provisionalMoves[ship] = new ProvisionalMovementState(ship);
                }
            }
        }
        else if (newPhase == Phase.Staging)
        {
            if (turnManager.CurrentPlayer == PlayerId.PlayerA)
            {
                if (!ConfirmProvisionalMovement())
                {
                    Debug.LogWarning("Provisional movement confirmation failed; no ships were moved.");
                }
            }

            if (match != null)
            {
                // Snapshot plane positions for movement range validation during Staging phase.
                PlayerId actingPlayer = turnManager.CurrentPlayer;
                foreach (PlaneUnit plane in match.planes)
                {
                    if (plane.owner == actingPlayer)
                    {
                        plane.positionAtTurnStart = plane.position;
                    }
                }
            }
        }
        else if (newPhase == Phase.Search)
        {
            if (match != null)
            {
                PlayerId actingPlayer = turnManager != null ? turnManager.CurrentPlayer : PlayerId.PlayerA;
                foreach (PlaneUnit plane in match.planes)
                {
                    if (plane.owner == actingPlayer)
                    {
                        plane.deployedThisTurn = false;
                    }
                }
            }

            // Passive vision remains automatic; active scans require player activation.
            Fog.RecomputeAllPassive(match);
            activeScanPreview = null;
            fleetScannedThisPhase = false;
        }
        else if (newPhase == Phase.Battle)
        {
            // Resets per-ship attack tracking so each living ship can make one attack this Battle phase.
            ResetShipAttackStates();
        }
        else if (newPhase == Phase.End)
        {
            ResetShipAttackStates();
            Fog.ClearAllActiveMarks();
            activeScanPreview = null;
            fleetScannedThisPhase = false;

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

                // Tick plane fuel for the acting player's planes.
                // Decrement fuelRemaining; remove planes that reach 0.
                PlayerId actingPlayer = turnManager.CurrentPlayer;
                for (int i = match.planes.Count - 1; i >= 0; i--)
                {
                    PlaneUnit plane = match.planes[i];
                    if (plane.owner != actingPlayer) continue;

                    plane.fuelRemaining--;

                    if (plane.fuelRemaining <= 0)
                    {
                        match.planes.RemoveAt(i);
                        Debug.Log($"[PLANE] {plane.owner} plane at {plane.position} ran out of fuel and was removed.");
                    }
                }

                Fog.RecomputeAllPassive(match);
            }
        }
        // Staging: mine deployment is player-activated via DeployMine(ship).
        // Plane deployment is player-activated via DeployPlane(ship, cell).
    }

    public void ResetShipAttackStates()
    {
        if (match == null) return;
        foreach (ShipInstance ship in match.playerA.ships)
        {
            ship.hasAttackedThisPhase = false;
        }
        foreach (ShipInstance ship in match.playerB.ships)
        {
            ship.hasAttackedThisPhase = false;
        }
    }
}