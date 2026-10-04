using System.Collections.Generic;
using UnityEngine;

// One of our own ships as the commander sees it.
public class FleetMember
{
    public ShipInstance Ship;
    public FleetRole Role;
    public float StaticFragility;    // per class, from FleetRoles
    public float HpFraction;         // 0..1
    // A wounded ship drops back regardless of class:
    // 1 - (1 - static) * hpFraction  -> equals static at full health, approaches 1 as HP falls.
    public float EffectiveFragility;
}

// Plain per-turn data holder, same idea as AITurnContext: no decisions in here.
// Deliberately contains NO real enemy ShipInstances, only contacts, so it stays fog-honest.
public class FleetSnapshot
{
    public PlayerId Owner;
    public FleetStance CurrentStance;
    public readonly List<FleetMember> Members = new List<FleetMember>();
    public List<AIContact> Contacts = new List<AIContact>();

    public int OwnAlive;
    public int OwnStartCount;
    public int EnemyAliveEstimate;   // starting count minus confirmed kills

    public float FleetHpFraction;    // living ships only: current HP / max HP
    public float MaterialScore;      // ~1.0 = even; higher = winning, lower = losing

    public Vector2Int FleetCentroid;
    public Vector2Int HomeCentroid;  // where the fleet stood when the commander first looked
    public float MapProgress;        // 0 = at home, 1 = at map center, along the home->center line

    public AIContact NearestContact;
    public int NearestContactDistance = -1;   // -1 = no contacts
}