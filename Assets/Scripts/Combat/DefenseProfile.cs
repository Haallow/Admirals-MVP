using System;
using System.Collections.Generic;

// Immutable definition of a defensive scheme. One instance per defense slot on a ship card.
// Actual remaining uses are tracked separately in ChargeState.
[Serializable]
public class DefenseProfile
{
    public string id;

    // Null means infinite uses (e.g. Crash Dive and Deep Dive on the Wolf Class).
    public int? uses;

    // Which incoming weapon domain this defense can counter.
    public DomainType validAgainst;

    // Saving throw roll table. Only outcomeLabel matters here; damage field is unused (left 0).
    public List<RollTier> savingThrowTiers;

    // String id for a side-effect triggered on success, e.g. "BecomeSubSurfaceAndSkipNextMove".
    // Null if there's no side effect. Logic that reads this id doesn't exist yet — Milestone 5+.
    public string sideEffectId;

    public DefenseProfile(string id, int? uses, DomainType validAgainst,
        List<RollTier> savingThrowTiers, string sideEffectId = null)
    {
        this.id = id;
        this.uses = uses;
        this.validAgainst = validAgainst;
        this.savingThrowTiers = savingThrowTiers;
        this.sideEffectId = sideEffectId;
    }
}
