using System.Collections.Generic;
using UnityEngine;

public static class AIUtilities
{
    public static List<Vector2Int> GetReachableAnchors(GridManager gridManager, ShipInstance ship)
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

    public static void TryMove(GridManager gridManager, ShipInstance ship, Vector2Int destination)
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

    public static Vector2Int ClosestReachableTileToEnemy(GridManager gridManager, List<Vector2Int> reachable, ShipInstance enemyShip, Vector2Int fallback)
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

    public static Vector2Int FindSafestReachableTile(GridManager gridManager, List<Vector2Int> reachable, ShipInstance aiShip)
    {
        Vector2Int best = aiShip.anchor;
        float bestSafety = float.NegativeInfinity;

        foreach (Vector2Int tile in reachable)
        {
            float danger = DangerAtTile(gridManager, tile, aiShip);
            float nearestEnemyDistance = NearestEnemyDistance(gridManager, tile);
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

    public static float NearestEnemyDistance(GridManager gridManager, Vector2Int tile)
    {
        float nearest = float.MaxValue;
        foreach (Vector2Int enemyCell in gridManager.TestShip.GetOccupiedCells())
        {
            nearest = Mathf.Min(nearest, gridManager.DistanceBetween(tile, enemyCell));
        }

        return nearest == float.MaxValue ? 0f : nearest;
    }

    public static float ScoreAttack(GridManager gridManager, ShipInstance aiShip, ShipInstance enemyShip, Vector2Int attackTile, float expectedDamage)
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
        score -= DangerAtTile(gridManager, attackTile, aiShip) * 0.5f;
        score -= Mathf.Max(0, 20 - aiShip.currentHealth) * 0.3f;

        float counterDamage = BestExpectedDamageAtAnchor(gridManager, enemyShip, aiShip, enemyShip.anchor, FootprintUtil.GetWorldCells(attackTile, aiShip.footprintOffsets, aiShip.rotationDegrees));
        if (counterDamage >= 0f)
        {
            score -= counterDamage * 0.3f;
        }

        return score;
    }

    public static float DangerAtTile(GridManager gridManager, Vector2Int tile, ShipInstance aiShip)
    {
        float danger = 0f;
        List<Vector2Int> aiCells = FootprintUtil.GetWorldCells(tile, aiShip.footprintOffsets, aiShip.rotationDegrees);

        foreach (WeaponProfile weapon in gridManager.TestShip.weapons)
        {
            if (CanFireFromAnchor(gridManager, gridManager.TestShip, aiShip, weapon, gridManager.TestShip.anchor, aiCells))
            {
                danger += ExpectedValue(weapon);
            }
        }

        return danger;
    }

    public static float BestExpectedDamageAtAnchor(GridManager gridManager, ShipInstance attacker, ShipInstance target, Vector2Int attackerAnchor)
    {
        return BestExpectedDamageAtAnchor(gridManager, attacker, target, attackerAnchor, target.GetOccupiedCells());
    }

    public static float BestExpectedDamageAtAnchor(GridManager gridManager, ShipInstance attacker, ShipInstance target, Vector2Int attackerAnchor, List<Vector2Int> targetCells)
    {
        float bestDamage = -1f;

        foreach (WeaponProfile weapon in attacker.weapons)
        {
            if (CanFireFromAnchor(gridManager, attacker, target, weapon, attackerAnchor, targetCells))
            {
                bestDamage = Mathf.Max(bestDamage, ExpectedValue(weapon));
            }
        }

        return bestDamage;
    }

    public static bool CanFireFromAnchor(GridManager gridManager, ShipInstance attacker, ShipInstance target, WeaponProfile weapon, Vector2Int attackerAnchor, List<Vector2Int> targetCells)
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

    public static bool CanFire(GridManager gridManager, ShipInstance attacker, ShipInstance target, WeaponProfile weapon)
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

    public static float ExpectedValue(WeaponProfile weapon)
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
