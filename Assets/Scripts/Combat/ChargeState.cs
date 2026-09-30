using System;

// Runtime mutable state for one weapon or defense slot.
// WeaponProfile/DefenseProfile are immutable definitions; this is what actually changes.
// One ChargeState exists per profile on a ship instance, created by ShipInstance.InitializeCharges().
[Serializable]
public class ChargeState
{
    public string profileId;        // matches WeaponProfile.id or DefenseProfile.id

    // Current remaining ammo/uses. -1 = infinite (mirrors a null ammo/uses on the profile,
    // stored as a plain int here because ChargeState is mutable runtime data).
    public int remaining;

    // Maximum this slot can hold. -1 = infinite (same sentinel as remaining).
    // Used by Recharge() to cap replenishment — e.g. mines recharge up to 2, never beyond.
    public int maxCapacity;

    // Turns until this slot recharges after being spent. 0 = ready now.
    public int turnsUntilRecharge;

    public ChargeState(string profileId, int remaining, int maxCapacity = -1)
    {
        this.profileId      = profileId;
        this.remaining      = remaining;
        this.maxCapacity    = maxCapacity;
        this.turnsUntilRecharge = 0;
    }

    public bool IsReady => turnsUntilRecharge == 0 && remaining != 0;

    // Called by TickRecharge when turnsUntilRecharge just decremented to 0.
    // Increments remaining by 1, capped at maxCapacity (-1 = no cap).
    // Does nothing if remaining is already at or above the cap, or if
    // remaining is the infinite sentinel (-1).
    public void Recharge()
    {
        if (remaining == -1) return;                          // infinite — nothing to recharge
        if (maxCapacity != -1 && remaining >= maxCapacity) return; // already at cap
        remaining++;
    }
}
