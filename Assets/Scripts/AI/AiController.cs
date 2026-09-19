using System.Collections.Generic;
using UnityEngine;

// Barebone AI for PlayerB (the obstruction ship).
// Runs automatically once per phase when it's PlayerB's turn — no keypress needed.
public class AIController : MonoBehaviour
{
    [SerializeField] private GridManager gridManager;
    [SerializeField] private TurnManager turnManager;
    [SerializeField, Range(0f, 1f)] private float fleeHealthFraction = 0.25f;

    private Phase lastPhase;
    private PlayerId lastPlayer;
    private bool actedThisPhase;
    private bool deploymentDecided;

    private void Update()
    {
        if (gridManager == null || turnManager == null)
        {
            return;
        }

        if (turnManager.CurrentPhase == Phase.Deployment)
        {
            DecideDeployment();
            return;
        }

        if (turnManager.CurrentPlayer != PlayerId.PlayerB)
        {
            return; // not the AI's turn
        }

        // Reset the "have I acted" flag whenever we enter a new phase/player turn.
        if (turnManager.CurrentPhase != lastPhase || turnManager.CurrentPlayer != lastPlayer)
        {
            actedThisPhase = false;
            lastPhase = turnManager.CurrentPhase;
            lastPlayer = turnManager.CurrentPlayer;
        }

        if (actedThisPhase)
        {
            return; // already made this phase's decision, wait for Space to advance
        }

        ShipInstance aiShip = gridManager.ObstructionShip;
        ShipInstance enemyShip = gridManager.TestShip;

        if (turnManager.CurrentPhase == Phase.Move)
        {
            DecideMove(aiShip, enemyShip);
            actedThisPhase = true;
        }
        else if (turnManager.CurrentPhase == Phase.Battle)
        {
            DecideAttack(aiShip, enemyShip);
            actedThisPhase = true;
        }
        // Search phase: no decision needed yet, no scanning built.
    }

    private void DecideDeployment()
    {
        if (deploymentDecided || gridManager.ObstructionShip == null)
        {
            return;
        }

        deploymentDecided = true;
        DeploymentAI.Decision decision = DeploymentAI.ChooseDeployment(gridManager.ObstructionShip, gridManager, gridManager.DeploymentDeadSpaceColumns);
        if (gridManager.DeployShip(gridManager.ObstructionShip, PlayerId.PlayerB, decision.anchor, decision.rotationDegrees, gridManager.DeploymentDeadSpaceColumns))
        {
            Debug.Log($"[Deployment AI] Player B selected {decision.anchor}, rotation {decision.rotationDegrees}, score {decision.score:F1}.");
            turnManager.ConfirmDeployment(PlayerId.PlayerB);
        }
        else
        {
            Debug.LogError("[Deployment AI] Could not find a valid deployment position.");
        }
    }

    // Score every legal anchor in movement range, then choose the best attack position.
    private void DecideMove(ShipInstance aiShip, ShipInstance enemyShip)
    {
        List<Vector2Int> reachable = GetReachableAnchors(aiShip);
        float bestScore = float.NegativeInfinity;
        Vector2Int bestTile = aiShip.anchor;
        float bestExpectedDamage = -1f;

        foreach (Vector2Int tile in reachable)
        {
            float expectedDamage = BestExpectedDamageAtAnchor(aiShip, enemyShip, tile);
            float score = expectedDamage < 0f
                ? float.NegativeInfinity
                : ScoreAttack(aiShip, enemyShip, tile, expectedDamage);

            Debug.Log($"[AI] Move candidate {tile}: score={score:F1}, expected damage={Mathf.Max(0f, expectedDamage):F1}, danger={DangerAtTile(tile, aiShip):F1}");

            if (score > bestScore)
            {
                bestScore = score;
                bestTile = tile;
                bestExpectedDamage = expectedDamage;
            }
        }

        bool isCriticallyLow = aiShip.maxHealth > 0 &&
            (float)aiShip.currentHealth / aiShip.maxHealth <= fleeHealthFraction;
        bool bestIsLethal = bestExpectedDamage >= enemyShip.currentHealth;

        if (isCriticallyLow && !bestIsLethal)
        {
            Vector2Int safeTile = FindSafestReachableTile(reachable, aiShip);
            Debug.Log($"[AI] Retreating: HP {aiShip.currentHealth}/{aiShip.maxHealth}, moving to {safeTile}.");
            TryMove(aiShip, safeTile);
            return;
        }

        if (bestExpectedDamage < 0f)
        {
            bestTile = ClosestReachableTileToEnemy(reachable, enemyShip, aiShip.anchor);
            Debug.Log($"[AI] No attack position found; approaching enemy at {bestTile}.");
        }

        Debug.Log($"[AI] Chose move {bestTile} with score {bestScore:F1}.");
        TryMove(aiShip, bestTile);
    }

    private List<Vector2Int> GetReachableAnchors(ShipInstance ship)
    {
        var reachable = new List<Vector2Int>();
        int movementRange = Mathf.Max(0, ship.movementRange);

        for (int x = -movementRange; x <= movementRange; x++)
        {
            for (int y = -movementRange; y <= movementRange; y++)
            {
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) > movementRange)
                {
                    continue;
                }

                Vector2Int candidate = ship.anchor + new Vector2Int(x, y);
                if (gridManager.CanPlaceShip(ship, candidate, ship.rotationDegrees))
                {
                    reachable.Add(candidate);
                }
            }
        }

        return reachable;
    }

    private void TryMove(ShipInstance ship, Vector2Int destination)
    {
        if (gridManager.MoveShip(ship, destination, ship.rotationDegrees))
        {
            Debug.Log($"[AI] Moved to {ship.anchor}");
        }
        else
        {
            Debug.Log("[AI] Move rejected after scoring; staying put this turn.");
        }
    }

    private Vector2Int ClosestReachableTileToEnemy(List<Vector2Int> reachable, ShipInstance enemyShip, Vector2Int fallback)
    {
        Vector2Int best = fallback;
        int bestDistance = int.MaxValue;

        foreach (Vector2Int tile in reachable)
        {
            foreach (Vector2Int enemyCell in enemyShip.GetOccupiedCells())
            {
                int distance = gridManager.DistanceBetween(tile, enemyCell);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = tile;
                }
            }
        }

        return best;
    }

    private Vector2Int FindSafestReachableTile(List<Vector2Int> reachable, ShipInstance aiShip)
    {
        Vector2Int best = aiShip.anchor;
        float bestSafety = float.NegativeInfinity;

        foreach (Vector2Int tile in reachable)
        {
            float danger = DangerAtTile(tile, aiShip);
            float nearestEnemyDistance = NearestEnemyDistance(tile);
            float safety = -danger + nearestEnemyDistance * 0.01f;

            Debug.Log($"[AI] Retreat candidate {tile}: safety={safety:F1}, danger={danger:F1}, nearest enemy distance={nearestEnemyDistance:F1}");

            if (safety > bestSafety)
            {
                bestSafety = safety;
                best = tile;
            }
        }

        return best;
    }

    private float NearestEnemyDistance(Vector2Int tile)
    {
        float nearest = float.MaxValue;
        foreach (Vector2Int enemyCell in gridManager.TestShip.GetOccupiedCells())
        {
            nearest = Mathf.Min(nearest, gridManager.DistanceBetween(tile, enemyCell));
        }

        return nearest == float.MaxValue ? 0f : nearest;
    }

    private float ScoreAttack(ShipInstance aiShip, ShipInstance enemyShip, Vector2Int attackTile, float expectedDamage)
    {
        float score = 0f;

        if (expectedDamage >= enemyShip.currentHealth)
        {
            score += 50f;
        }
        else
        {
            score += Mathf.Min(expectedDamage * 0.4f, 40f);
        }

        score += Mathf.Max(0, 20 - enemyShip.currentHealth) * 0.5f;
        score -= DangerAtTile(attackTile, aiShip) * 0.5f;
        score -= Mathf.Max(0, 20 - aiShip.currentHealth) * 0.3f;

        float counterDamage = BestExpectedDamageAtAnchor(enemyShip, aiShip, enemyShip.anchor, FootprintUtil.GetWorldCells(attackTile, aiShip.footprintOffsets, aiShip.rotationDegrees));
        if (counterDamage >= 0f)
        {
            score -= counterDamage * 0.3f;
        }

        return score;
    }

    private float DangerAtTile(Vector2Int tile, ShipInstance aiShip)
    {
        float danger = 0f;
        List<Vector2Int> aiCells = FootprintUtil.GetWorldCells(tile, aiShip.footprintOffsets, aiShip.rotationDegrees);

        foreach (WeaponProfile weapon in gridManager.TestShip.weapons)
        {
            if (CanFireFromAnchor(gridManager.TestShip, aiShip, weapon, gridManager.TestShip.anchor, aiCells))
            {
                danger += ExpectedValue(weapon);
            }
        }

        return danger;
    }

    private float BestExpectedDamageAtAnchor(ShipInstance attacker, ShipInstance target, Vector2Int attackerAnchor)
    {
        return BestExpectedDamageAtAnchor(attacker, target, attackerAnchor, target.GetOccupiedCells());
    }

    private float BestExpectedDamageAtAnchor(ShipInstance attacker, ShipInstance target, Vector2Int attackerAnchor, List<Vector2Int> targetCells)
    {
        float bestDamage = -1f;

        foreach (WeaponProfile weapon in attacker.weapons)
        {
            if (CanFireFromAnchor(attacker, target, weapon, attackerAnchor, targetCells))
            {
                bestDamage = Mathf.Max(bestDamage, ExpectedValue(weapon));
            }
        }

        return bestDamage;
    }

    private bool CanFireFromAnchor(ShipInstance attacker, ShipInstance target, WeaponProfile weapon, Vector2Int attackerAnchor, List<Vector2Int> targetCells)
    {
        ChargeState charge = gridManager.FindChargeState(attacker.weaponCharges, weapon.id);
        if (charge == null || !charge.IsReady)
        {
            return false;
        }

        bool domainMatches = weapon.targetDomain == target.currentDomain || weapon.targetDomain == DomainType.Both;
        if (!domainMatches)
        {
            return false;
        }

        int minimumDistance = int.MaxValue;
        foreach (Vector2Int cell in targetCells)
        {
            minimumDistance = Mathf.Min(minimumDistance, gridManager.DistanceBetween(attackerAnchor, cell));
        }

        return weapon.weaponRange >= minimumDistance;
    }

    // Greedy scoring: check every weapon, skip ones that can't legally fire,
    // score the rest by ExpectedValue, fire the best one.
    private void DecideAttack(ShipInstance aiShip, ShipInstance enemyShip)
    {
        WeaponProfile best = null;
        float bestScore = -1f;

        foreach (var weapon in aiShip.weapons)
        {
            if (!CanFire(aiShip, enemyShip, weapon))
            {
                continue;
            }

            float score = ExpectedValue(weapon);
            if (score > bestScore)
            {
                bestScore = score;
                best = weapon;
            }
        }

        if (best == null)
        {
            Debug.Log("[AI] No valid weapon to fire this turn.");
            return;
        }

        bool hit = gridManager.ResolveAttack(aiShip, enemyShip, best);
        Debug.Log($"[AI] Fired {best.id} (expected value {bestScore:F1}): {(hit ? "resolved" : "rejected")}");
    }

    // Mirrors ResolveAttack's three checks, without side effects — used to
    // decide which weapons are even worth scoring, before actually firing one.
    private bool CanFire(ShipInstance attacker, ShipInstance target, WeaponProfile weapon)
    {
        ChargeState charge = gridManager.FindChargeState(attacker.weaponCharges, weapon.id);
        if (charge == null || !charge.IsReady)
        {
            return false;
        }

        bool domainMatches = weapon.targetDomain == target.currentDomain || weapon.targetDomain == DomainType.Both;
        if (!domainMatches)
        {
            return false;
        }

        int minDistance = int.MaxValue;
        foreach (var cell in target.GetOccupiedCells())
        {
            minDistance = Mathf.Min(minDistance, gridManager.DistanceBetween(attacker.anchor, cell));
        }

        return weapon.weaponRange >= minDistance;
    }

    // Same concept as the teammate's ExpectedValue, recomputed from RollTier
    // data instead of an atk/def formula: average damage per d20 roll.
    private float ExpectedValue(WeaponProfile weapon)
    {
        float expected = 0f;
        foreach (var tier in weapon.rollTiers)
        {
            int tierWidth = tier.maxRoll - tier.minRoll + 1;
            float probability = tierWidth / 20f;
            expected += probability * tier.damage;
        }
        return expected;
    }
}