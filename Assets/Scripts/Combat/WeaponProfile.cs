using System;
using System.Collections.Generic;

// Immutable definition of a weapon. One instance per weapon slot on a ship card.
// Actual remaining ammo is tracked separately in ChargeState so this definition never changes.
[Serializable]
public class WeaponProfile
{
    public string id;

    // Null means infinite ammo (e.g. a passive weapon).
    // Use int? so the Inspector can show "no limit" as distinct from 0.
    public int? ammo;

    // Which domain target this weapon can engage.
    public DomainType targetDomain;

    // Ordered roll table — look up by rolling 1d20 and finding the matching tier.
    public List<RollTier> rollTiers;

    public WeaponProfile(string id, int? ammo, DomainType targetDomain, List<RollTier> rollTiers)
    {
        this.id = id;
        this.ammo = ammo;
        this.targetDomain = targetDomain;
        this.rollTiers = rollTiers;
    }
}
