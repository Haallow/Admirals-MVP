using UnityEngine;

// Milestone test input, still throwaway, still not the real input system.
//
// New this round:
//   - Tab cycles which of PlayerA's ships is currently controlled.
//   - Number keys 1/2/3 pick which weapon HandleAttackInput will fire.
// Both are needed now that PlayerA can have more than one ship on the board.
public class TestShipController : MonoBehaviour
{
    [SerializeField] private GridManager gridManager;
    [SerializeField] private TurnManager turnManager;

    private Phase lastPhaseSeen;

    // Index into gridManager.Match.playerA.ships — which ship responds to input.
    private int currentShipIndex = 0;

    // Index into the current ship's weapons list — which weapon HandleAttackInput uses.
    private int selectedWeaponIndex = 0;
    private bool isDraggingMovement;
    private Vector2Int lastDragCell;

    // --- Plane input state ---
    private bool planePlacementPending = false;
    private bool isControllingPlane = false;
    private int currentPlaneIndex = 0;
    private int deploymentSlot;
    private bool draggingDeployment;
    private Vector2Int deploymentDragOffset;
    private Vector2Int lastDeploymentDragCell;

    private void Update()
    {
        if (gridManager == null || turnManager == null || gridManager.Match == null) return;
        if (gridManager.Deployment == null || !gridManager.Deployment.IsComplete)
        {
            HandleDeploymentInput();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (turnManager.CurrentPhase == Phase.Move)
            {
                if (!gridManager.ConfirmProvisionalMovement())
                {
                    Debug.LogWarning("Cannot leave Move phase while provisional movement is invalid.");
                    return;
                }
            }

            if (turnManager.CurrentPhase == Phase.Staging)
            {
                if (gridManager.Match != null)
                {
                    foreach (PlaneUnit p in gridManager.Match.planes)
                    {
                        if (p.owner == turnManager.CurrentPlayer)
                        {
                            gridManager.ConfirmPlaneMove(p);
                        }
                    }
                }
            }

            if (turnManager.CurrentPhase == Phase.Search &&
                gridManager.ActiveScanPreview != null)
            {
                gridManager.ConfirmActiveScan();
            }

            turnManager.AdvancePhase();
        }

        if (gridManager == null || turnManager == null || gridManager.Match == null)
        {
            return;
        }

        // All of PlayerA's ships, so ship-switching has something to cycle through.
        var myShips = gridManager.Match.playerA.ships;
        if (myShips.Count == 0)
        {
            return;
        }

        // --- Ship switching (works regardless of whose turn/phase it is,
        // same as Space — just changes which ship future input targets) ---
        currentShipIndex = Mathf.Min(currentShipIndex, myShips.Count - 1);

        // --- Click-to-select active ship ---
        // Left-clicking a Player A ship selects it as the active ship.
        // Pass 1: provisional preview cells (ships that have already been dragged).
        // Pass 2: tile occupancy, but only when the occupant has NOT been
        //         provisionally moved away — this lets un-dragged ships be
        //         selected at their original tile in Move phase while still
        //         ignoring the vacated anchor cells of already-dragged ships.
        if (Input.GetMouseButtonDown(0) && Camera.main != null && !planePlacementPending)
        {
            Vector2Int clickedCell = GetMouseGridCell();

            // In Staging phase: planes are clickable to become active.
            bool planeSelected = false;
            if (turnManager.CurrentPhase == Phase.Staging && gridManager.Match != null)
            {
                int pIndex = 0;
                foreach (PlaneUnit p in gridManager.Match.planes)
                {
                    if (p.owner == PlayerId.PlayerA)
                    {
                        if (p.position == clickedCell)
                        {
                            isControllingPlane = true;
                            currentPlaneIndex = pIndex;
                            isDraggingMovement = false;
                            planeSelected = true;
                            Debug.Log($"[Click] Switched to plane {currentPlaneIndex} at {p.position}");
                            break;
                        }
                        pIndex++;
                    }
                }
            }

            if (!planeSelected)
            {
                ShipInstance clickedShip = null;

                // Pass 1: provisional preview cells (always checked first).
                foreach (ProvisionalMovementState state in gridManager.ProvisionalMoves)
                {
                    if (state.Ship.owner == PlayerId.PlayerA &&
                        state.GetPreviewCells().Contains(clickedCell))
                    {
                        clickedShip = state.Ship;
                        break;
                    }
                }

                // Pass 2: tile occupancy fallback.
                // In Move phase only accept the occupant if it has no provisional
                // state that already moved it away from this cell, so that vacated
                // anchor cells of dragged ships cannot trigger a selection.
                if (clickedShip == null)
                {
                    Tile clickedTile = gridManager.GetTile(clickedCell);
                    if (clickedTile?.Occupant != null &&
                        clickedTile.Occupant.owner == PlayerId.PlayerA)
                    {
                        ShipInstance candidate = clickedTile.Occupant;
                        bool movedAway = false;
                        if (turnManager.CurrentPhase == Phase.Move)
                        {
                            foreach (ProvisionalMovementState state in gridManager.ProvisionalMoves)
                            {
                                if (state.Ship == candidate &&
                                    !state.GetPreviewCells().Contains(clickedCell))
                                {
                                    movedAway = true;
                                    break;
                                }
                            }
                        }
                        if (!movedAway)
                        {
                            clickedShip = candidate;
                        }
                    }
                }

                if (clickedShip != null)
                {
                    int foundIndex = myShips.IndexOf(clickedShip);
                    if (foundIndex >= 0)
                    {
                        currentShipIndex = foundIndex;
                        selectedWeaponIndex = 0;
                        isControllingPlane = false;
                        // Cancel any in-progress drag so HandlePointerMovement cannot
                        // treat this selection click as a drag-start on the new ship.
                        isDraggingMovement = false;
                        Debug.Log($"[Click] Switched to ship {currentShipIndex}: {myShips[currentShipIndex].shipType}");
                    }
                }
            }
        }

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            // During Staging phase, Tab cycles through ships then planes.
            if (turnManager.CurrentPhase == Phase.Staging)
            {
                // Count living planes owned by PlayerA.
                int planeCount = 0;
                if (gridManager.Match != null)
                {
                    foreach (PlaneUnit p in gridManager.Match.planes)
                    {
                        if (p.owner == PlayerId.PlayerA) planeCount++;
                    }
                }

                if (isControllingPlane)
                {
                    currentPlaneIndex++;
                    if (currentPlaneIndex >= planeCount)
                    {
                        // Cycled past last plane — return to first ship.
                        isControllingPlane = false;
                        currentPlaneIndex = 0;
                        currentShipIndex = 0;
                        selectedWeaponIndex = 0;
                        Debug.Log($"Switched to ship {currentShipIndex}: {myShips[currentShipIndex].shipType}");
                    }
                    else
                    {
                        Debug.Log($"Switched to plane {currentPlaneIndex}");
                    }
                }
                else
                {
                    currentShipIndex++;
                    if (currentShipIndex >= myShips.Count)
                    {
                        // Cycled past last ship — switch to planes if any exist.
                        if (planeCount > 0)
                        {
                            isControllingPlane = true;
                            currentPlaneIndex = 0;
                            currentShipIndex = Mathf.Min(currentShipIndex, myShips.Count - 1);
                            Debug.Log($"Switched to plane {currentPlaneIndex}");
                        }
                        else
                        {
                            // No planes — wrap to first ship.
                            currentShipIndex = 0;
                            selectedWeaponIndex = 0;
                            Debug.Log($"Switched to ship {currentShipIndex}: {myShips[currentShipIndex].shipType}");
                        }
                    }
                    else
                    {
                        selectedWeaponIndex = 0;
                        Debug.Log($"Switched to ship {currentShipIndex}: {myShips[currentShipIndex].shipType}");
                    }
                }
            }
            else
            {
                // Non-Staging phases: standard ship cycling only.
                currentShipIndex = (currentShipIndex + 1) % myShips.Count;
                selectedWeaponIndex = 0;
                isControllingPlane = false;
                Debug.Log($"Switched to ship {currentShipIndex}: {myShips[currentShipIndex].shipType}");
            }
        }

        ShipInstance ship = myShips[currentShipIndex];

        if (ship.owner != turnManager.CurrentPlayer)
        {
            lastPhaseSeen = turnManager.CurrentPhase;
            return; // not this player's turn at all
        }

        if (lastPhaseSeen != turnManager.CurrentPhase)
        {
            planePlacementPending = false;
        }

        if (turnManager.CurrentPhase == Phase.Move)
        {
            // Just entered Move phase: snapshot EVERY one of this player's ships,
            // not just the currently selected one. Otherwise switching ships
            // mid-phase would let the newly selected ship dodge its own range
            // check, since its anchorAtTurnStart would still be stale.
            if (lastPhaseSeen != Phase.Move)
            {
                foreach (var s in myShips)
                {
                    s.anchorAtTurnStart = s.anchor;
                }
                isControllingPlane = false;
                currentPlaneIndex = 0;
            }

            // Domain toggle: submarine (WolfClass) only, only during Move phase when active.
            if (ship.shipType == ShipType.WolfClass && Input.GetKeyDown(KeyCode.D))
            {
                ship.currentDomain = ship.currentDomain == DomainType.Surface
                    ? DomainType.SubSurface
                    : DomainType.Surface;

                Debug.Log($"--- Domain toggled to: {ship.currentDomain} ---");
            }

            HandleMoveInput(ship);
        }
        else if (turnManager.CurrentPhase == Phase.Battle)
        {
            HandleWeaponSelection(ship);
            HandleAttackInput(ship);
        }
        else if (turnManager.CurrentPhase == Phase.Search)
        {
            HandleActiveScanInput(ship);
        }
        else if (turnManager.CurrentPhase == Phase.Staging)
        {
            if (lastPhaseSeen != Phase.Staging)
            {
                isControllingPlane = false;
                currentPlaneIndex = 0;
                planePlacementPending = false;
            }

            if (isControllingPlane)
            {
                HandlePlaneMovementInput();
            }
            else
            {
                HandleStagingInput(ship);
            }
        }

        lastPhaseSeen = turnManager.CurrentPhase;
    }

    private void HandleDeploymentInput()
    {
        DeploymentService deployment = gridManager.Deployment;
        if (deployment == null || !deployment.IsValid || Camera.main == null) return;
        if (deployment.IsConfirmed(PlayerId.PlayerA)) return;

        int count = gridManager.Match.playerA.fleetRoster.Count;
        if (count == 0) return;
        deploymentSlot = Mathf.Clamp(deploymentSlot, 0, count - 1);
        string zoneId = deployment.GetAssignedZone(PlayerId.PlayerA);

        if (Input.GetKeyDown(KeyCode.Tab)) deploymentSlot = (deploymentSlot + 1) % count;
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.C))
            draggingDeployment = false;

        if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E))
        {
            if (deployment.TryGetDraft(PlayerId.PlayerA, PlayerId.PlayerA, deploymentSlot, out DeploymentDraft draft))
            {
                int delta = Input.GetKeyDown(KeyCode.Q) ? -90 : 90;
                int rotation = (draft.RotationDegrees + delta + 360) % 360;
                if (!deployment.TryPlace(PlayerId.PlayerA, zoneId, deploymentSlot, draft.Anchor, rotation))
                    Debug.Log("Deployment rotation rejected.");
            }
        }

        // Keep the right screen panel's clicks out of board placement.
        if (Input.GetMouseButtonDown(0) && Input.mousePosition.x < Screen.width - 230)
        {
            Vector2Int cell = GetMouseGridCell();
            foreach (DeploymentDraft draft in deployment.GetVisibleDrafts(PlayerId.PlayerA, PlayerId.PlayerA))
            foreach (Vector2Int draftCell in draft.Cells)
            {
                if (draftCell != cell) continue;
                deploymentSlot = draft.RosterSlot;
                draggingDeployment = true;
                deploymentDragOffset = draft.Anchor - cell;
                lastDeploymentDragCell = cell;
                return;
            }

            int rotation = deployment.TryGetDraft(PlayerId.PlayerA, PlayerId.PlayerA,
                deploymentSlot, out DeploymentDraft selected) ? selected.RotationDegrees : 0;
            if (!deployment.TryPlace(PlayerId.PlayerA, zoneId, deploymentSlot, cell, rotation))
                Debug.Log("Deployment placement rejected.");
        }

        if (draggingDeployment && Input.GetMouseButton(0))
        {
            Vector2Int cell = GetMouseGridCell();
            if (cell != lastDeploymentDragCell)
            {
                lastDeploymentDragCell = cell;
                if (deployment.TryGetDraft(PlayerId.PlayerA, PlayerId.PlayerA, deploymentSlot, out DeploymentDraft draft) &&
                    !deployment.TryPlace(PlayerId.PlayerA, zoneId, deploymentSlot,
                        cell + deploymentDragOffset, draft.RotationDegrees))
                    Debug.Log("Deployment drag rejected.");
            }
        }
        if (Input.GetMouseButtonUp(0)) draggingDeployment = false;
    }

    private void OnGUI()
    {
        if (gridManager == null || gridManager.Match == null || gridManager.Deployment == null ||
            gridManager.Deployment.IsComplete) return;

        GUILayout.BeginArea(new Rect(Screen.width - 223, 8, 215, 310), GUI.skin.box);
        GUILayout.Label("Player A Deployment");
        if (!gridManager.Deployment.IsValid)
        {
            GUILayout.Label("Map deployment zones are invalid.");
        }
        else
        {
            var roster = gridManager.Match.playerA.fleetRoster;
            for (int slot = 0; slot < roster.Count; slot++)
            {
                bool placed = gridManager.Deployment.TryGetDraft(PlayerId.PlayerA, PlayerId.PlayerA, slot, out _);
                if (GUILayout.Button($"{(slot == deploymentSlot ? "> " : "")}{roster[slot]} {(placed ? "Placed" : "Unplaced")}"))
                    deploymentSlot = slot;
            }
            GUILayout.Label(gridManager.Deployment.IsConfirmed(PlayerId.PlayerB)
                ? "Player B ready" : "Waiting for Player B");
            GUILayout.Label("Click to place, drag to move, Q/E rotate.");
            GUI.enabled = gridManager.Deployment.CanConfirm(PlayerId.PlayerA);
            if (GUILayout.Button("Confirm Deployment"))
                gridManager.ConfirmDeployment(PlayerId.PlayerA);
            GUI.enabled = true;
        }
        GUILayout.EndArea();
    }

    private void HandleMoveInput(ShipInstance ship)
    {
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.C))
        {
            gridManager.CancelProvisionalMovement();
            isDraggingMovement = false;
            Debug.Log("Provisional movement cancelled; ships reverted to their movement-phase positions.");
            return;
        }

        HandlePointerMovement(ship);

        Vector2Int direction = Vector2Int.zero;
        if (Input.GetKeyDown(KeyCode.UpArrow))    direction = Vector2Int.up;
        else if (Input.GetKeyDown(KeyCode.DownArrow))  direction = Vector2Int.down;
        else if (Input.GetKeyDown(KeyCode.LeftArrow))  direction = Vector2Int.left;
        else if (Input.GetKeyDown(KeyCode.RightArrow)) direction = Vector2Int.right;

        if (direction != Vector2Int.zero)
        {
            Vector2Int candidateAnchor = GetPreviewAnchor(ship) + direction;
            gridManager.PreviewMove(ship, candidateAnchor, GetPreviewRotation(ship));
        }

        int rotationDelta = 0;
        if (Input.GetKeyDown(KeyCode.Q))      rotationDelta = -90;
        else if (Input.GetKeyDown(KeyCode.E)) rotationDelta = 90;

        if (rotationDelta != 0)
        {
            int candidateRotation = ((GetPreviewRotation(ship) + rotationDelta) % 360 + 360) % 360;
            gridManager.PreviewMove(ship, GetPreviewAnchor(ship), candidateRotation);
        }
    }

    private void HandlePointerMovement(ShipInstance ship)
    {
        if (Camera.main == null)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            Vector2Int pressedCell = GetMouseGridCell();
            if (IsShipPreviewCell(ship, pressedCell))
            {
                isDraggingMovement = true;
                lastDragCell = pressedCell;
            }
        }

        if (!isDraggingMovement)
        {
            return;
        }

        Vector2Int currentCell = GetMouseGridCell();
        if (currentCell != lastDragCell)
        {
            lastDragCell = currentCell;
            gridManager.PreviewMove(
                ship,
                currentCell,
                GetPreviewRotation(ship));
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDraggingMovement = false;
        }
    }

    private Vector2Int GetMouseGridCell()
    {
        Vector3 mouseScreen = Input.mousePosition;
        mouseScreen.z = -Camera.main.transform.position.z;
        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(mouseScreen);
        return new Vector2Int(
            Mathf.RoundToInt(mouseWorld.x / gridManager.CellSize),
            Mathf.RoundToInt(mouseWorld.y / gridManager.CellSize));
    }

    private bool IsShipPreviewCell(ShipInstance ship, Vector2Int cell)
    {
        foreach (ProvisionalMovementState state in gridManager.ProvisionalMoves)
        {
            if (state.Ship == ship && state.GetPreviewCells().Contains(cell))
            {
                return true;
            }
        }

        return ship.GetOccupiedCells().Contains(cell);
    }

    private PlaneUnit GetSelectedPlane()
    {
        if (gridManager.Match == null) return null;
        int index = 0;
        foreach (PlaneUnit p in gridManager.Match.planes)
        {
            if (p.owner == PlayerId.PlayerA)
            {
                if (index == currentPlaneIndex)
                {
                    return p;
                }
                index++;
            }
        }
        return null;
    }

    private void HandlePlaneMovementInput()
    {
        PlaneUnit plane = GetSelectedPlane();
        if (plane == null)
        {
            isControllingPlane = false;
            return;
        }

        // C cancels: if the plane was deployed this turn, undeploy it. Otherwise revert move.
        if (Input.GetKeyDown(KeyCode.C))
        {
            if (plane.deployedThisTurn)
            {
                if (gridManager.UndeployPlane(plane))
                {
                    isControllingPlane = false;
                    currentPlaneIndex = 0;
                    return;
                }
            }

            plane.position = plane.positionAtTurnStart;
            Debug.Log($"[PLANE] Plane reverted to turn start position: {plane.positionAtTurnStart}");
            return;
        }

        Vector2Int direction = Vector2Int.zero;
        if (Input.GetKeyDown(KeyCode.UpArrow))    direction = Vector2Int.up;
        else if (Input.GetKeyDown(KeyCode.DownArrow))  direction = Vector2Int.down;
        else if (Input.GetKeyDown(KeyCode.LeftArrow))  direction = Vector2Int.left;
        else if (Input.GetKeyDown(KeyCode.RightArrow)) direction = Vector2Int.right;

        if (direction != Vector2Int.zero)
        {
            Vector2Int candidate = plane.position + direction;
            gridManager.PreviewPlaneMove(plane, candidate);
        }

        if (Input.GetMouseButtonDown(0))
        {
            Vector2Int clickedCell = GetMouseGridCell();
            if (clickedCell != plane.position)
            {
                // Only move if not clicking a friendly ship or another friendly plane (which are selection targets)
                bool isFriendlyShip = false;
                Tile clickedTile = gridManager.GetTile(clickedCell);
                if (clickedTile?.Occupant != null && clickedTile.Occupant.owner == plane.owner)
                {
                    isFriendlyShip = true;
                }

                bool isOtherFriendlyPlane = false;
                if (gridManager.Match != null)
                {
                    foreach (PlaneUnit other in gridManager.Match.planes)
                    {
                        if (other != plane && other.owner == plane.owner && other.position == clickedCell)
                        {
                            isOtherFriendlyPlane = true;
                            break;
                        }
                    }
                }

                if (!isFriendlyShip && !isOtherFriendlyPlane)
                {
                    gridManager.PreviewPlaneMove(plane, clickedCell);
                }
            }
        }
    }

    private void HandleActiveScanInput(ShipInstance ship)
    {
        // Cancel active scan preview if active ship changed
        if (gridManager.ActiveScanPreview != null && gridManager.ActiveScanPreview.Ship != ship)
        {
            gridManager.CancelActiveScan();
        }

        if (Input.GetKeyDown(KeyCode.S))
        {
            if (gridManager.ActivateActiveScan(ship))
            {
                Debug.Log($"Active scan preview started for {ship.shipType}.");
            }
            else
            {
                Debug.Log("Active scan activation rejected (fleet scan already used this phase, wrong phase, or no cone layer).");
            }
        }

        // Mouse click or drag to rotate active scan direction
        if (gridManager.ActiveScanPreview != null && Camera.main != null)
        {
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
            {
                Vector2Int mouseCell = GetMouseGridCell();
                Vector2Int origin = gridManager.ActiveScanPreview.Bow;
                Vector2Int diff = mouseCell - origin;
                if (diff == Vector2Int.zero)
                {
                    diff = mouseCell - ship.anchor;
                }

                if (diff != Vector2Int.zero)
                {
                    Vector2Int newForward;
                    if (Mathf.Abs(diff.x) >= Mathf.Abs(diff.y))
                    {
                        newForward = diff.x > 0 ? Vector2Int.right : Vector2Int.left;
                    }
                    else
                    {
                        newForward = diff.y > 0 ? Vector2Int.up : Vector2Int.down;
                    }

                    gridManager.SetActiveScanForward(newForward);
                }
            }
        }
        else if (gridManager.ActiveScanPreview == null && Input.GetMouseButtonDown(0) && Camera.main != null)
        {
            // Clicking the active ship during Search phase activates the scan preview
            Vector2Int clickedCell = GetMouseGridCell();
            Tile clickedTile = gridManager.GetTile(clickedCell);
            if (clickedTile?.Occupant == ship)
            {
                if (gridManager.ActivateActiveScan(ship))
                {
                    Debug.Log($"Active scan preview started for {ship.shipType}.");
                }
            }
        }

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.C))
        {
            gridManager.CancelActiveScan();
            Debug.Log("Active scan preview cancelled.");
            return;
        }

        if (Input.GetKeyDown(KeyCode.Q))
        {
            gridManager.RotateActiveScan(-1);
        }
        else if (Input.GetKeyDown(KeyCode.E))
        {
            gridManager.RotateActiveScan(1);
        }

        if (Input.GetKeyDown(KeyCode.Return))
        {
            if (gridManager.ConfirmActiveScan())
            {
                Debug.Log("Active scan confirmed.");
            }
        }
    }

    private void HandleStagingInput(ShipInstance ship)
    {
        // M — deploy a mine one cell behind the selected ship's stern.
        if (Input.GetKeyDown(KeyCode.M))
        {
            if (gridManager.DeployMine(ship))
            {
                Debug.Log($"Mine deployed by {ship.shipType}.");
            }
            // DeployMine logs its own rejection reason on failure.
        }

        // P key — enter plane placement targeting mode.
        // Next left-click places the plane on the clicked cell.
        if (Input.GetKeyDown(KeyCode.P))
        {
            planePlacementPending = true;
            Debug.Log("Plane placement mode: click a cell to deploy (or Esc to cancel).");
        }

        if (planePlacementPending && (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.C)))
        {
            planePlacementPending = false;
            Debug.Log("Plane placement mode cancelled.");
            return;
        }

        if (planePlacementPending && Input.GetMouseButtonDown(0))
        {
            planePlacementPending = false;
            Vector2Int cell = GetMouseGridCell();

            // Prefer selected ship if it has plane capability; otherwise auto-select carrier if available.
            ShipInstance launchShip = ship;
            if (launchShip.planes.Count == 0 && gridManager.Match != null)
            {
                var carrier = gridManager.Match.playerA.ships.Find(s => s.planes.Count > 0);
                if (carrier != null)
                {
                    launchShip = carrier;
                }
            }

            if (gridManager.DeployPlane(launchShip, cell))
            {
                Debug.Log($"Plane deployed to {cell} from {launchShip.shipType}.");

                // Immediately make the newly deployed plane active upon deploying
                if (gridManager.Match != null)
                {
                    int pIndex = 0;
                    foreach (PlaneUnit p in gridManager.Match.planes)
                    {
                        if (p.owner == PlayerId.PlayerA)
                        {
                            if (p.position == cell)
                            {
                                isControllingPlane = true;
                                currentPlaneIndex = pIndex;
                                Debug.Log($"[PLANE] Activated newly deployed plane {currentPlaneIndex} at {cell}. Movement locked until next Staging phase (C to undeploy).");
                                break;
                            }
                            pIndex++;
                        }
                    }
                }
            }
            // DeployMine / DeployPlane logs its own rejection reason on failure.
        }
    }

    private Vector2Int GetPreviewAnchor(ShipInstance ship)
    {
        foreach (ProvisionalMovementState state in gridManager.ProvisionalMoves)
        {
            if (state.Ship == ship)
            {
                return state.PreviewAnchor;
            }
        }

        return ship.anchor;
    }

    private int GetPreviewRotation(ShipInstance ship)
    {
        foreach (ProvisionalMovementState state in gridManager.ProvisionalMoves)
        {
            if (state.Ship == ship)
            {
                return state.PreviewRotation;
            }
        }

        return ship.rotationDegrees;
    }

    // Number keys 1/2/3 pick which of the current ship's weapons will be used
    // by HandleAttackInput. Out-of-range keys (e.g. pressing 3 on a 1-weapon
    // ship) are ignored, index just doesn't change.
    private void HandleWeaponSelection(ShipInstance ship)
    {
        int requestedIndex = -1;
        if (Input.GetKeyDown(KeyCode.Alpha1)) requestedIndex = 0;
        else if (Input.GetKeyDown(KeyCode.Alpha2)) requestedIndex = 1;
        else if (Input.GetKeyDown(KeyCode.Alpha3)) requestedIndex = 2;

        if (requestedIndex == -1)
        {
            return; // no number key pressed this frame
        }

        if (requestedIndex >= ship.weapons.Count)
        {
            Debug.Log($"Ship only has {ship.weapons.Count} weapon(s), no slot {requestedIndex + 1}.");
            return;
        }

        selectedWeaponIndex = requestedIndex;
        Debug.Log($"Selected weapon: {ship.weapons[selectedWeaponIndex].id}");
    }

    private void HandleAttackInput(ShipInstance ship)
    {
        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2Int clickedPos = new Vector2Int(
            Mathf.RoundToInt(mouseWorld.x / gridManager.CellSize),
            Mathf.RoundToInt(mouseWorld.y / gridManager.CellSize)
        );

        Tile clickedTile = gridManager.GetTile(clickedPos);

        // Friendly ship — treated as a ship-selection click (handled above in
        // Update); silently ignore so no misleading log appears.
        if (clickedTile?.Occupant != null && clickedTile.Occupant.owner == ship.owner)
        {
            return;
        }

        // Empty tile or out-of-bounds — nothing to attack.
        if (clickedTile == null || clickedTile.Occupant == null)
        {
            return;
        }

        // Each ship can only attack once per Battle phase.
        if (ship.hasAttackedThisPhase)
        {
            Debug.Log($"Attack rejected: {ship.owner}'s {ship.shipType} has already attacked this Battle phase.");
            return;
        }

        // selectedWeaponIndex was already bounds-checked in HandleWeaponSelection,
        // but re-check here too in case the active ship was switched (Tab) after
        // selecting a weapon on a different ship with more weapon slots.
        if (selectedWeaponIndex >= ship.weapons.Count)
        {
            Debug.Log("Selected weapon slot doesn't exist on this ship.");
            return;
        }

        WeaponProfile weaponToUse = ship.weapons[selectedWeaponIndex];
        bool hit = gridManager.Combat.ResolveAttack(ship, clickedTile.Occupant, weaponToUse);
        Debug.Log(hit ? "Attack resolved." : "Attack rejected.");
    }
}
