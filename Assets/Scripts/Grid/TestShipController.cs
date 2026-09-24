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

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (turnManager.CurrentPhase == Phase.Move &&
                !gridManager.ConfirmProvisionalMovement())
            {
                Debug.LogWarning("Cannot leave Move phase while provisional movement is invalid.");
                return;
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

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            currentShipIndex = (currentShipIndex + 1) % myShips.Count;
            selectedWeaponIndex = 0;
            Debug.Log($"Switched to ship {currentShipIndex}: {myShips[currentShipIndex].shipType}");
        }

        ShipInstance ship = myShips[currentShipIndex];

        // --- Domain toggle (ungated by phase/owner, unchanged from before) ---
        if (Input.GetKeyDown(KeyCode.D))
        {
            ship.currentDomain = ship.currentDomain == DomainType.Surface
                ? DomainType.SubSurface
                : DomainType.Surface;

            Debug.Log($"--- Domain toggled to: {ship.currentDomain} ---");
            ship.LogStatBlock($"{ship.shipType} [PlayerA] after domain toggle");
        }

        if (ship.owner != turnManager.CurrentPlayer)
        {
            lastPhaseSeen = turnManager.CurrentPhase;
            return; // not this player's turn at all
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

        lastPhaseSeen = turnManager.CurrentPhase;
    }

    private void HandleMoveInput(ShipInstance ship)
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            gridManager.CancelProvisionalMovement();
            Debug.Log("Provisional movement cancelled.");
            return;
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            gridManager.CancelProvisionalMovement();
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
            if (!gridManager.PreviewMove(ship, candidateAnchor, GetPreviewRotation(ship)))
            {
                Debug.Log($"Move preview rejected at {candidateAnchor}; the ship remains at its last valid preview.");
            }
            else
            {
                Debug.Log($"Previewed move to {candidateAnchor}");
            }
        }

        int rotationDelta = 0;
        if (Input.GetKeyDown(KeyCode.Q))      rotationDelta = -90;
        else if (Input.GetKeyDown(KeyCode.E)) rotationDelta = 90;

        if (rotationDelta != 0)
        {
            int candidateRotation = ((GetPreviewRotation(ship) + rotationDelta) % 360 + 360) % 360;
            if (!gridManager.PreviewMove(ship, GetPreviewAnchor(ship), candidateRotation))
            {
                Debug.Log($"Rotation preview rejected at {candidateRotation}; the ship remains at its last valid preview.");
            }
            else
            {
                Debug.Log($"Previewed rotation to {candidateRotation}");
            }
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
            if (!gridManager.PreviewMove(
                    ship,
                    currentCell,
                    GetPreviewRotation(ship)))
            {
                Debug.Log($"Pointer move preview rejected at {currentCell}; the ship remains at its last valid preview.");
            }
            else
            {
                Debug.Log($"Pointer-previewed move to {currentCell}");
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDraggingMovement = false;
            Debug.Log("Pointer movement released; provisional movement remains unconfirmed.");
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

    private void HandleActiveScanInput(ShipInstance ship)
    {
        if (Input.GetKeyDown(KeyCode.S))
        {
            if (gridManager.ActivateActiveScan(ship))
            {
                Debug.Log($"Active scan preview started for {ship.shipType}.");
            }
            else
            {
                Debug.Log("Selected ship has no non-passive cone scan.");
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
        if (clickedTile == null || clickedTile.Occupant == null || clickedTile.Occupant.owner == ship.owner)
        {
            Debug.Log("No valid enemy target at clicked tile.");
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