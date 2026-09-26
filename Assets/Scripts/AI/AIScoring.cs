using UnityEngine;
// Shared scoring math used by both movement and attack planning. Kept
// separate from AIAttackPlanner/AIMovementPlanner so both can share the same
// notion of "how good is this weapon" without duplicating the formula.
public static class AIScoring
{
    // Average damage per d20 roll, derived from a weapon's roll-tier table.
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
