using System;

// Immutable definition of a mine that a ship can deploy during Staging.
// Actual remaining uses are tracked separately in ChargeState (mineCharges),
// following the same pattern as WeaponProfile/weaponCharges.
[Serializable]
public class MineProfile
{
    public string id;

    // How many mines this ship starts the match with, and the maximum it can
    // carry at any time (recharge never exceeds this cap).
    // Null means infinite (unlikely but kept consistent with weapon/defense convention).
    public int? count;

    // Flat damage applied on detonation. No roll, no armor reduction.
    public int damage;

    // Turns until one mine replenishes after being deployed.
    // 0 means no recharge (finite, one-time use).
    public int rechargeTime;

    public MineProfile(string id, int? count, int damage, int rechargeTime = 0)
    {
        this.id           = id;
        this.count        = count;
        this.damage       = damage;
        this.rechargeTime = rechargeTime;
    }
}
