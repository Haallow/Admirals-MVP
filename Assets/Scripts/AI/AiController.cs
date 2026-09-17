using UnityEngine;

// Barebone AI for PlayerB (the obstruction ship).
// Runs automatically once per phase when it's PlayerB's turn — no keypress needed.
public class AIController : MonoBehaviour
{
    [SerializeField] private GridManager gridManager;
    [SerializeField] private TurnManager turnManager;

    private Phase lastPhase;
    private PlayerId lastPlayer;
    private bool actedThisPhase;

    private void Update()
    {
        if (gridManager == null || turnManager == null)
        {
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

    // Step one tile toward the enemy's anchor, same king-move logic as human movement.
    private void DecideMove(ShipInstance aiShip, ShipInstance enemyShip)
    {
        int dx = Mathf.Clamp(enemyShip.anchor.x - aiShip.anchor.x, -1, 1);
        int dy = Mathf.Clamp(enemyShip.anchor.y - aiShip.anchor.y, -1, 1);
        Vector2Int candidate = aiShip.anchor + new Vector2Int(dx, dy);

        if (gridManager.MoveShip(aiShip, candidate, aiShip.rotationDegrees))
        {
            Debug.Log($"[AI] Moved to {aiShip.anchor}");
        }
        else
        {
            Debug.Log("[AI] Move blocked, staying put this turn.");
        }
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