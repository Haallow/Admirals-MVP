using System.Collections.Generic;
using UnityEngine;

// Thin coordinator for the enemy AI. The real logic lives in smaller helper files.
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
            return;
        }

        if (turnManager.CurrentPhase != lastPhase || turnManager.CurrentPlayer != lastPlayer)
        {
            actedThisPhase = false;
            lastPhase = turnManager.CurrentPhase;
            lastPlayer = turnManager.CurrentPlayer;
        }

        if (actedThisPhase)
        {
            return;
        }

        ShipInstance aiShip = gridManager.ObstructionShip;
        ShipInstance enemyShip = gridManager.TestShip;

        if (aiShip != null && aiShip.currentHealth <= 0)
        {
            return;
        }

        if (turnManager.CurrentPhase == Phase.Move)
        {
            DecideMove(aiShip, enemyShip);
            actedThisPhase = true;
        }
        else if (turnManager.CurrentPhase == Phase.Search)
        {
            DecideSearch(aiShip, enemyShip);
            actedThisPhase = true;
        }
        else if (turnManager.CurrentPhase == Phase.Battle)
        {
            DecideAttack(aiShip, enemyShip);
            actedThisPhase = true;
        }
    }

    private void DecideDeployment()
    {
        if (deploymentDecided || gridManager.ObstructionShip == null)
        {
            return;
        }

        deploymentDecided = true;
        AIDeploymentPlanner.TryDeploy(gridManager, turnManager, gridManager.ObstructionShip, PlayerId.PlayerB, false);
    }

    private void DecideMove(ShipInstance aiShip, ShipInstance enemyShip)
    {
        if (aiShip == null || enemyShip == null)
        {
            return;
        }

        AIMovePlanner.DecideMove(gridManager, aiShip, enemyShip, fleeHealthFraction);
    }

    private void DecideSearch(ShipInstance aiShip, ShipInstance enemyShip)
    {
        if (aiShip == null || enemyShip == null)
        {
            return;
        }

        AISearchPlanner.DecideSearch(gridManager, aiShip, enemyShip, turnManager);
    }

    private void DecideAttack(ShipInstance aiShip, ShipInstance enemyShip)
    {
        if (aiShip == null || enemyShip == null)
        {
            return;
        }

        AIAttackPlanner.DecideAttack(gridManager, aiShip, enemyShip);
    }

    private List<Vector2Int> GetReachableAnchors(ShipInstance ship)
    {
        return AIUtilities.GetReachableAnchors(gridManager, ship);
    }

    private void TryMove(ShipInstance ship, Vector2Int destination)
    {
        AIUtilities.TryMove(gridManager, ship, destination);
    }

    private Vector2Int ClosestReachableTileToEnemy(List<Vector2Int> reachable, ShipInstance enemyShip, Vector2Int fallback)
    {
        return AIUtilities.ClosestReachableTileToEnemy(gridManager, reachable, enemyShip, fallback);
    }

    private Vector2Int FindSafestReachableTile(List<Vector2Int> reachable, ShipInstance aiShip)
    {
        return AIUtilities.FindSafestReachableTile(gridManager, reachable, aiShip);
    }

    private float NearestEnemyDistance(Vector2Int tile)
    {
        return AIUtilities.NearestEnemyDistance(gridManager, tile);
    }

    private float ScoreAttack(ShipInstance aiShip, ShipInstance enemyShip, Vector2Int attackTile, float expectedDamage)
    {
        return AIUtilities.ScoreAttack(gridManager, aiShip, enemyShip, attackTile, expectedDamage);
    }

    private float DangerAtTile(Vector2Int tile, ShipInstance aiShip)
    {
        return AIUtilities.DangerAtTile(gridManager, tile, aiShip);
    }

    private float BestExpectedDamageAtAnchor(ShipInstance attacker, ShipInstance target, Vector2Int attackerAnchor)
    {
        return AIUtilities.BestExpectedDamageAtAnchor(gridManager, attacker, target, attackerAnchor);
    }

    private float BestExpectedDamageAtAnchor(ShipInstance attacker, ShipInstance target, Vector2Int attackerAnchor, List<Vector2Int> targetCells)
    {
        return AIUtilities.BestExpectedDamageAtAnchor(gridManager, attacker, target, attackerAnchor, targetCells);
    }

    private bool CanFireFromAnchor(ShipInstance attacker, ShipInstance target, WeaponProfile weapon, Vector2Int attackerAnchor, List<Vector2Int> targetCells)
    {
        return AIUtilities.CanFireFromAnchor(gridManager, attacker, target, weapon, attackerAnchor, targetCells);
    }

    private bool CanFire(ShipInstance attacker, ShipInstance target, WeaponProfile weapon)
    {
        return AIUtilities.CanFire(gridManager, attacker, target, weapon);
    }

    private float ExpectedValue(WeaponProfile weapon)
    {
        return AIUtilities.ExpectedValue(weapon);
    }
}