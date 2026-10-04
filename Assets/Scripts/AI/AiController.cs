using System.Collections.Generic;
using UnityEngine;

// Barebone AI for PlayerB (the obstruction ship).
//
// This is the orchestrator only -- it wires phase events to decisions and
// carries them out. The actual decision logic lives in:
//   - AITurnContext      (roster + fog snapshot for a turn)
//   - FleetCommander     (fleet-level strategy, stance, coordination)
//   - AIMovementPlanner  (where to move, including formation terms)
//   - AIAttackPlanner    (what to fire, at what)
//   - AIScoring          (shared weapon-value math)
// AIController itself should never grow scoring or fog logic -- if a new
// piece of decision-making is needed, it belongs in one of the planner
// files above, not here.
public class AIController : MonoBehaviour
{
    [SerializeField] private GridManager gridManager;
    [SerializeField] private TurnManager turnManager;

    // Stage 2: central fleet strategy layer. Created lazily on the first
    // phase event so it's fresh every match and never a static.
    private FleetCommander fleetCommander;

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

        // Lazy-create the commander on the first phase event we handle,
        // so it resets cleanly every play session without static state.
        if (fleetCommander == null)
        {
            fleetCommander = new FleetCommander(gridManager, PlayerId.PlayerB);
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

            // Stage 2: evaluate fleet stance and snapshot once per turn.
            // Dwell-time tracking and stance changes happen here, so the
            // stance is stable for the whole Move phase.
            FleetSnapshot snapshot = fleetCommander.Evaluate(context, debugForceStance);

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
                ExecuteMove(ship, context, snapshot, claimedTargets);
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

            // Stage 2: refresh contacts only. Stance changes and dwell-time
            // ticks stay once-per-turn (at Move), so stances are stable and
            // don't flip mid-turn when Battle sees something new.
            FleetSnapshot snapshot = fleetCommander.RefreshContacts(context);

            // Stage 4: prioritize targets for coordinated fire
            List<ShipInstance> prioritizedTargets = fleetCommander.PrioritizeTargets(context.KnownEnemies, gridManager);

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

                ExecuteAttack(ship, prioritizedTargets, snapshot, gridManager, claimedTargets);
            }
        }
        else if (newPhase == Phase.Search)
        {
            AITurnContext context = AITurnContext.Build(gridManager, PlayerId.PlayerB);
            AIActiveScanPlanner.RunActiveScans(context, gridManager);
        }
        // Staging/End: no AI decision needed yet.
    }

    private void ExecuteMove(ShipInstance aiShip, AITurnContext context, FleetSnapshot snapshot, HashSet<ShipInstance> claimedTargets)
    {
        AIMovementPlanner.Decision decision =
            AIMovementPlanner.ChooseDestination(aiShip, context, snapshot, gridManager, claimedTargets);

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
    private void ExecuteAttack(ShipInstance aiShip, List<ShipInstance> prioritizedTargets, FleetSnapshot snapshot, GridManager gridManager, HashSet<ShipInstance> claimedTargets)
    {
        if (!AIAttackPlanner.TryChooseTarget(
                aiShip, prioritizedTargets, gridManager, snapshot.CurrentStance, claimedTargets,
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


    // ---- Debug Testing Helpers (Stage 3) ------------------------------------

    private FleetStance? debugForceStance = null;

    private void Update()
    {
        if (turnManager == null || turnManager.CurrentPlayer != PlayerId.PlayerB)
            return;

        // Debug hotkeys: force specific stances for testing
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            debugForceStance = FleetStance.Hunt;
            Debug.Log("[AI][Debug] Forced stance: Hunt");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            debugForceStance = FleetStance.Advance;
            Debug.Log("[AI][Debug] Forced stance: Advance");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            debugForceStance = FleetStance.Hold;
            Debug.Log("[AI][Debug] Forced stance: Hold");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            debugForceStance = FleetStance.DefensiveFallback;
            Debug.Log("[AI][Debug] Forced stance: DefensiveFallback");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            debugForceStance = FleetStance.Retreat;
            Debug.Log("[AI][Debug] Forced stance: Retreat");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            debugForceStance = null;
            Debug.Log("[AI][Debug] Released stance override (auto)");
        }

        // Debug: damage AI fleet for Retreat testing
        if (Input.GetKeyDown(KeyCode.Minus))
        {
            if (gridManager != null && gridManager.Match != null)
            {
                foreach (ShipInstance ship in gridManager.Match.GetPlayer(PlayerId.PlayerB).ships)
                {
                    if (ship.currentHealth > 0)
                    {
                        ship.currentHealth = Mathf.Max(1, ship.currentHealth / 3);
                        Debug.Log($"[AI][Debug] Damaged {ship.shipType} to {ship.currentHealth}/{ship.maxHealth} HP");
                    }
                }
            }
        }

        // Debug: heal AI fleet
        if (Input.GetKeyDown(KeyCode.Equals))
        {
            if (gridManager != null && gridManager.Match != null)
            {
                foreach (ShipInstance ship in gridManager.Match.GetPlayer(PlayerId.PlayerB).ships)
                {
                    if (ship.currentHealth > 0)
                    {
                        ship.currentHealth = ship.maxHealth;
                        Debug.Log($"[AI][Debug] Healed {ship.shipType} to full HP");
                    }
                }
            }
        }
    }

    private void LogTurnContext(AITurnContext context)
    {
    }
}