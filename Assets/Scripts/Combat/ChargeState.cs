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

    // Turns until this slot recharges after being spent. 0 = ready now.
    // Recharge logic doesn't exist yet — stored here so the field is available when needed (Milestone 6+).
    public int turnsUntilRecharge;

    public ChargeState(string profileId, int remaining)
    {
        this.profileId = profileId;
        this.remaining = remaining;
        this.turnsUntilRecharge = 0;
    }

    public bool IsReady => turnsUntilRecharge == 0 && remaining != 0;
}
