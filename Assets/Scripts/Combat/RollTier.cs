using System;

// One row in a weapon or saving-throw roll table.
// e.g. Spear: D1-3 = Miss (0 dmg), D4-17 = Direct Hit (500 dmg), D18-20 = Catastrophic (750 dmg).
// For saving-throw tables (DefenseProfile), damage is left 0 — only outcomeLabel matters.
[Serializable]
public struct RollTier
{
    public int minRoll;
    public int maxRoll;
    public string outcomeLabel; // e.g. "Miss", "Direct Hit", "Catastrophic Hit", "Hit Avoided", "Fail"
    public int damage;          // 0 for saving-throw rows and misses

    public RollTier(int minRoll, int maxRoll, string outcomeLabel, int damage = 0)
    {
        this.minRoll = minRoll;
        this.maxRoll = maxRoll;
        this.outcomeLabel = outcomeLabel;
        this.damage = damage;
    }

}
