using UnityEngine;

public static class AIAttackPlanner
{
    public static void DecideAttack(GridManager gridManager, ShipInstance aiShip, ShipInstance enemyShip)
    {
        WeaponProfile best = null;
        float bestScore = -1f;

        foreach (var weapon in aiShip.weapons)
        {
            if (!AIUtilities.CanFire(gridManager, aiShip, enemyShip, weapon))
            {
                continue;
            }

            float score = AIUtilities.ExpectedValue(weapon);
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
}
