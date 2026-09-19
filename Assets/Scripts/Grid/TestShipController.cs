using UnityEngine;

public class TestShipController : MonoBehaviour
{
    [SerializeField] private GridManager gridManager;
    [SerializeField] private TurnManager turnManager;
    private Vector2Int deploymentCursor;
    private int deploymentRotation;
    private bool deploymentConfirmed;

    private void Start()
    {
        if (gridManager != null)
        {
            int deploymentZoneWidth = (gridManager.Width - gridManager.DeploymentDeadSpaceColumns) / 2;
            deploymentCursor = new Vector2Int(deploymentZoneWidth / 2, gridManager.Height / 2);
        }
    }

    private void Update()
    {
        if (gridManager == null || gridManager.TestShip == null || turnManager == null)
        {
            return;
        }

        if (turnManager.CurrentPhase == Phase.Deployment)
        {
            gridManager.SetDeploymentPreview(gridManager.TestShip, deploymentCursor, deploymentRotation, !deploymentConfirmed);
            HandleDeploymentInput(gridManager.TestShip);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            turnManager.AdvancePhase();
        }

        ShipInstance ship = gridManager.TestShip;

        // --- Domain toggle (ungated by phase/owner for now, same as before) ---
        if (Input.GetKeyDown(KeyCode.D))
        {
            ship.currentDomain = ship.currentDomain == DomainType.Surface
                ? DomainType.SubSurface
                : DomainType.Surface;

            Debug.Log($"--- Domain toggled to: {ship.currentDomain} ---");
            ship.LogStatBlock("Wolf Class [PlayerA] after domain toggle");
        }

        // --- Ownership gate applies to both Move and Battle actions below ---
        if (ship.owner != turnManager.CurrentPlayer)
        {
            return; // not this player's turn at all
        }

        if (turnManager.CurrentPhase == Phase.Move)
        {
            HandleMoveInput(ship);
        }
        else if (turnManager.CurrentPhase == Phase.Battle)
        {
            HandleAttackInput(ship);
        }
    }

    private void HandleMoveInput(ShipInstance ship)
    {
        Vector2Int direction = Vector2Int.zero;
        if (Input.GetKeyDown(KeyCode.UpArrow))    direction = Vector2Int.up;
        else if (Input.GetKeyDown(KeyCode.DownArrow))  direction = Vector2Int.down;
        else if (Input.GetKeyDown(KeyCode.LeftArrow))  direction = Vector2Int.left;
        else if (Input.GetKeyDown(KeyCode.RightArrow)) direction = Vector2Int.right;

        if (direction != Vector2Int.zero)
        {
            Vector2Int candidateAnchor = ship.anchor + direction;
            if (gridManager.MoveShip(ship, candidateAnchor, ship.rotationDegrees))
            {
                Debug.Log($"Moved to {ship.anchor}");
            }
        }

        int rotationDelta = 0;
        if (Input.GetKeyDown(KeyCode.Q))      rotationDelta = -90;
        else if (Input.GetKeyDown(KeyCode.E)) rotationDelta = 90;

        if (rotationDelta != 0)
        {
            int candidateRotation = ((ship.rotationDegrees + rotationDelta) % 360 + 360) % 360;
            if (gridManager.MoveShip(ship, ship.anchor, candidateRotation))
            {
                Debug.Log($"Rotated to {ship.rotationDegrees}");
            }
        }
    }

    private void HandleDeploymentInput(ShipInstance ship)
    {
        if (deploymentConfirmed)
        {
            return;
        }

        Vector2Int direction = Vector2Int.zero;
        if (Input.GetKeyDown(KeyCode.UpArrow)) direction = Vector2Int.up;
        else if (Input.GetKeyDown(KeyCode.DownArrow)) direction = Vector2Int.down;
        else if (Input.GetKeyDown(KeyCode.LeftArrow)) direction = Vector2Int.left;
        else if (Input.GetKeyDown(KeyCode.RightArrow)) direction = Vector2Int.right;

        if (direction != Vector2Int.zero)
        {
            Vector2Int candidate = deploymentCursor + direction;
            if (gridManager.CanDeployShip(ship, PlayerId.PlayerA, candidate, deploymentRotation, gridManager.DeploymentDeadSpaceColumns))
            {
                deploymentCursor = candidate;
                Debug.Log($"[Deployment] Player A cursor moved to {deploymentCursor}");
            }
        }

        int rotationDelta = 0;
        if (Input.GetKeyDown(KeyCode.Q)) rotationDelta = -90;
        else if (Input.GetKeyDown(KeyCode.E)) rotationDelta = 90;

        if (rotationDelta != 0)
        {
            int candidateRotation = ((deploymentRotation + rotationDelta) % 360 + 360) % 360;
            if (gridManager.CanDeployShip(ship, PlayerId.PlayerA, deploymentCursor, candidateRotation, gridManager.DeploymentDeadSpaceColumns))
            {
                deploymentRotation = candidateRotation;
                Debug.Log($"[Deployment] Player A rotation changed to {deploymentRotation}");
            }
        }

        if (Input.GetKeyDown(KeyCode.Return) && gridManager.DeployShip(ship, PlayerId.PlayerA, deploymentCursor, deploymentRotation, gridManager.DeploymentDeadSpaceColumns))
        {
            deploymentConfirmed = true;
            gridManager.SetDeploymentPreview(ship, deploymentCursor, deploymentRotation, false);
            Debug.Log($"[Deployment] Player A selected {deploymentCursor}, rotation {deploymentRotation}.");
            turnManager.ConfirmDeployment(PlayerId.PlayerA);
        }
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

        WeaponProfile weaponToUse = ship.weapons[2]; // barebone: always first weapon for now
        bool hit = gridManager.ResolveAttack(ship, clickedTile.Occupant, weaponToUse);
        Debug.Log(hit ? "Attack resolved." : "Attack rejected (ammo/domain/range).");
    }
}