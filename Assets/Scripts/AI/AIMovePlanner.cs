using System.Collections.Generic;
using UnityEngine;

public static class AIMovePlanner
{
    public static void DecideMove(GridManager gridManager, ShipInstance aiShip, ShipInstance enemyShip, float fleeHealthFraction)
    {
        List<Vector2Int> reachable = AIUtilities.GetReachableAnchors(gridManager, aiShip);
        float bestScore = float.NegativeInfinity;
        Vector2Int bestTile = aiShip.anchor;
        float bestExpectedDamage = -1f;

        foreach (Vector2Int tile in reachable)
        {
            float expectedDamage = AIUtilities.BestExpectedDamageAtAnchor(gridManager, aiShip, enemyShip, tile);
            float score = expectedDamage < 0f
                ? float.NegativeInfinity
                : AIUtilities.ScoreAttack(gridManager, aiShip, enemyShip, tile, expectedDamage);

            Debug.Log($"[AI] Move candidate {tile}: score={score:F1}, expected damage={Mathf.Max(0f, expectedDamage):F1}, danger={AIUtilities.DangerAtTile(gridManager, tile, aiShip):F1}");

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
            Vector2Int safeTile = AIUtilities.FindSafestReachableTile(gridManager, reachable, aiShip);
            Debug.Log($"[AI] Retreating: HP {aiShip.currentHealth}/{aiShip.maxHealth}, moving to {safeTile}.");
            AIUtilities.TryMove(gridManager, aiShip, safeTile);
            return;
        }

        if (bestExpectedDamage < 0f)
        {
            bestTile = AIUtilities.ClosestReachableTileToEnemy(gridManager, reachable, enemyShip, aiShip.anchor);
            Debug.Log($"[AI] No attack position found; approaching enemy at {bestTile}.");
        }

        Debug.Log($"[AI] Chose move {bestTile} with score {bestScore:F1}.");
        AIUtilities.TryMove(gridManager, aiShip, bestTile);
    }
}
