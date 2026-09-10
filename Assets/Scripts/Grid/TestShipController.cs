using UnityEngine;

// Milestone 3 test input. Still throwaway, still not the real input system.
// Milestone 4 addition: D key toggles the Wolf Class ship's domain between
// Surface and SubSurface and re-logs the stat block so you can verify the
// "onlyWhileSurfaced" vision layer active/inactive flag changes correctly.
public class TestShipController : MonoBehaviour
{
    [SerializeField] private GridManager gridManager;
    [SerializeField] private TurnManager turnManager;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            turnManager.AdvancePhase();
        }

        if (gridManager == null || gridManager.TestShip == null || turnManager == null)
        {
            return;
        }

        ShipInstance ship = gridManager.TestShip;

        // --- Milestone 4: domain toggle ---
        // Press D to flip the Wolf Class between Surface and SubSurface and re-log.
        // Proves the "Default Absolute Vision (onlyWhileSurfaced)" line changes accordingly.
        if (Input.GetKeyDown(KeyCode.D))
        {
            ship.currentDomain = ship.currentDomain == DomainType.Surface
                ? DomainType.SubSurface
                : DomainType.Surface;

            Debug.Log($"--- Domain toggled to: {ship.currentDomain} ---");
            ship.LogStatBlock("Wolf Class [PlayerA] after domain toggle");
        }

        // --- Movement / rotation (Milestone 3, unchanged) ---
        if (turnManager.CurrentPhase != Phase.Move)
        {
            return; // no acting outside Move phase
        }

        if (ship.owner != turnManager.CurrentPlayer)
        {
            return; // not this player's ship
        }

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
}
