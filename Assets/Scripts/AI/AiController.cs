using UnityEngine;

// Barebone AI for PlayerB (the obstruction ship).
//
// This is the orchestrator only -- it wires phase events to decisions and
// carries them out. The actual decision logic lives in:
//   - AITurnContext      (roster + fog snapshot for a turn)
//   - AIMovementPlanner  (where to move)
//   - AIAttackPlanner    (what to fire, at what)
//   - AIScoring          (shared weapon-value math)
// AIController itself should never grow scoring or fog logic -- if a new
// piece of decision-making is needed, it belongs in one of the planner
// files above, not here.
public class AIController : MonoBehaviour
{
    [SerializeField] private GridManager gridManager;
    [SerializeField] private TurnManager turnManager;

    private void OnEnable()
    {
        if (turnManager != null)
        {
            turnManager.PhaseChanged += HandlePhaseChanged;
        }
    }

    private void OnDisable()
    {
        if (turnManager != null)
        {
            turnManager.PhaseChanged -= HandlePhaseChanged;
        }
    }

    // Fires exactly once per phase transition -- no debounce flags needed.
    private void HandlePhaseChanged(Phase newPhase)
    {
        if (gridManager == null || turnManager == null)
        {
            return;
        }

        if (turnManager.CurrentPlayer != PlayerId.PlayerB)
        {
            return; // not the AI's turn
        }

        ShipInstance aiShip = gridManager.ObstructionShip;
        ShipInstance enemyShip = gridManager.TestShip;

        if (aiShip == null || enemyShip == null)
        {
            return;
        }

        if (newPhase == Phase.Move)
        {
            AITurnContext context = AITurnContext.Build(gridManager, PlayerId.PlayerB);
            LogTurnContext(context);

            aiShip.anchorAtTurnStart = aiShip.anchor;
            ExecuteMove(aiShip, context);
        }
        else if (newPhase == Phase.Battle)
        {
            AITurnContext context = AITurnContext.Build(gridManager, PlayerId.PlayerB);
            LogTurnContext(context);

            ExecuteAttack(aiShip, enemyShip);
        }
        // Staging/Search/End: no AI decision needed yet.
    }

    private void ExecuteMove(ShipInstance aiShip, AITurnContext context)
    {
        AIMovementPlanner.Decision decision = AIMovementPlanner.ChooseDestination(aiShip, context, gridManager);

        if (gridManager.MoveShip(aiShip, decision.destination, aiShip.rotationDegrees))
        {
            string intent = decision.isFleeing
                ? "fleeing"
                : decision.likelyTarget != null
                    ? $"lining up on {decision.likelyTarget.shipType}"
                    : "repositioning";
            Debug.Log($"[AI] Moved to {aiShip.anchor} ({intent})");
        }
        else
        {
            Debug.Log("[AI] Move blocked, staying put this turn.");
        }
    }

    // Still single-target for now -- Step 4 makes this fog-aware and
    // multi-enemy, matching AIMovementPlanner's targeting.
    private void ExecuteAttack(ShipInstance aiShip, ShipInstance enemyShip)
    {
        WeaponProfile weapon = AIAttackPlanner.ChooseWeapon(aiShip, enemyShip, gridManager);

        if (weapon == null)
        {
            Debug.Log("[AI] No valid weapon to fire this turn.");
            return;
        }

        bool hit = gridManager.Combat.ResolveAttack(aiShip, enemyShip, weapon);
        float expectedValue = AIScoring.ExpectedValue(weapon);
        Debug.Log($"[AI] Fired {weapon.id} (expected value {expectedValue:F1}): {(hit ? "resolved" : "rejected")}");
    }

    private void LogTurnContext(AITurnContext context)
    {
        Debug.Log($"[AI][Context] {context.MyShips.Count} living ship(s) of mine, "
            + $"{context.EnemyShips.Count} living enemy ship(s), "
            + $"{context.KnownEnemies.Count} of those currently known to my fog.");
    }
}