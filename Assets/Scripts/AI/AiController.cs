using System.Collections.Generic;
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

        if (newPhase == Phase.Move)
        {
            AITurnContext context = AITurnContext.Build(gridManager, PlayerId.PlayerB);
            LogTurnContext(context);

            // Fog is non-sticky by design; this keeps a separate AI-side memory
            // of where each enemy was last seen (and its heading) for
            // AIActiveScanPlanner. advanceTurn: true is the once-per-turn tick
            // that ages ships currently hidden from fog.
            AIEnemyMemory.Remember(PlayerId.PlayerB, context.EnemyShips, context.KnownEnemies, advanceTurn: true);

            // CHANGE (AI Optimization Recommendations, item 2.3): tracks
            // which known enemies one of this side's ships has already
            // lined up on this Move phase, so later ships are nudged away
            // from all converging on the same one. Scoped to this phase
            // only -- a fresh set is built every time Move begins.
            HashSet<ShipInstance> claimedTargets = new HashSet<ShipInstance>();

            // Sequential: each ship's move fully resolves (including its own
            // updated position/occupancy) before the next ship decides, so a
            // later ship's pathfinding sees the earlier ship's new position.
            foreach (ShipInstance ship in context.MyShips)
            {
                ship.anchorAtTurnStart = ship.anchor;
                ExecuteMove(ship, context, claimedTargets);
            }
        }
        else if (newPhase == Phase.Battle)
        {
            AITurnContext context = AITurnContext.Build(gridManager, PlayerId.PlayerB);
            LogTurnContext(context);

            // Battle can see something Move didn't: the Search phase (and its
            // active scan) ran in between. Record it, but don't age hidden
            // ships again -- their clock only ticks once per turn, at Move.
            AIEnemyMemory.Remember(PlayerId.PlayerB, context.EnemyShips, context.KnownEnemies, advanceTurn: false);

            // Same idea as the Move-phase set above, but tracking which
            // enemies have already been fired on this Battle phase. Kept
            // separate from the Move-phase set -- a ship's Move-phase
            // target is only ever informational and Battle re-decides
            // independently, so there is no shared state to carry over.
            HashSet<ShipInstance> claimedTargets = new HashSet<ShipInstance>();

            foreach (ShipInstance ship in context.MyShips)
            {
                if (ship.currentHealth <= 0)
                {
                    continue; // may have been destroyed earlier this same phase
                }

                ExecuteAttack(ship, context, claimedTargets);
            }
        }
        else if (newPhase == Phase.Search)
        {
            AITurnContext context = AITurnContext.Build(gridManager, PlayerId.PlayerB);
            AIActiveScanPlanner.RunActiveScans(context, gridManager);
        }
        // Staging/End: no AI decision needed yet.
    }

    private void ExecuteMove(ShipInstance aiShip, AITurnContext context, HashSet<ShipInstance> claimedTargets)
    {
        AIMovementPlanner.Decision decision =
            AIMovementPlanner.ChooseDestination(aiShip, context, gridManager, claimedTargets);

        if (gridManager.MoveShip(aiShip, decision.destination, aiShip.rotationDegrees))
        {
            // Claim only on an actually-successful move -- a rejected move
            // never repositioned this ship toward the target, so it
            // shouldn't discourage a sibling ship from going after it.
            if (decision.likelyTarget != null)
            {
                claimedTargets.Add(decision.likelyTarget);
            }

            string intent = decision.isFleeing
                ? "fleeing"
                : decision.likelyTarget != null
                    ? $"lining up on {decision.likelyTarget.shipType}"
                    : "repositioning";
            Debug.Log($"[AI] {aiShip.shipType} moved to {aiShip.anchor} ({intent})");
        }
        else
        {
            Debug.Log($"[AI] {aiShip.shipType} move blocked, staying put this turn.");
        }
    }

    // Step 4: fog-aware and multi-enemy -- only ever targets an enemy the
    // AI's own fog currently knows about, matching AIMovementPlanner's
    // targeting logic.
    private void ExecuteAttack(ShipInstance aiShip, AITurnContext context, HashSet<ShipInstance> claimedTargets)
    {
        if (!AIAttackPlanner.TryChooseTarget(
                aiShip, context.KnownEnemies, gridManager, claimedTargets,
                out ShipInstance target, out WeaponProfile weapon))
        {
            Debug.Log($"[AI] {aiShip.shipType} has no known enemy in range this turn.");
            return;
        }

        bool hit = gridManager.Combat.ResolveAttack(aiShip, target, weapon);

        // Claim only when the attack actually resolved (ResolveAttack
        // returns true even on a d20 miss, false only on rejection) -- a
        // rejected attack never actually engaged the target, so it
        // shouldn't discourage a sibling ship from trying it instead.
        if (hit)
        {
            claimedTargets.Add(target);
        }

        float expectedValue = AIScoring.ExpectedValue(weapon);
        Debug.Log($"[AI] {aiShip.shipType} fired {weapon.id} at {target.shipType} (expected value {expectedValue:F1}): {(hit ? "resolved" : "rejected")}");
    }

    private void LogTurnContext(AITurnContext context)
    {
    }
}