using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Central strategy layer above the per-ship planners. STAGE 1: it builds a fog-honest picture
// of the fleet, picks a stance, and LOGS it. Nothing here steers a ship yet.
//
// Plain class, not a MonoBehaviour. AIController owns one instance and creates it lazily on
// its first phase event, so it is fresh every play session and never a static.
// Parametrized by PlayerId; no map size or "which side am I on" is hardcoded.
//
// Fog honesty: this class reads only context.MyShips, context.MyFog and its own counters.
// It never touches context.EnemyShips or context.KnownEnemies.
public class FleetCommander
{
    // Score bands (see FleetSnapshot.MaterialScore). Tunable; only the ordering matters in Stage 1.
    private const float RetreatScoreBelow = 0.35f;
    private const float RetreatHpBelow = 0.30f;
    private const float FallbackScoreBelow = 0.60f;
    private const float AdvanceScoreAtLeast = 0.90f;

    // The current stance's band is widened by this much so the score has to move clearly
    // past a threshold before the stance flips back (hysteresis).
    private const float HysteresisMargin = 0.08f;

    // Minimum turns a non-Hunt stance is held. Entering Retreat (a safety condition) and
    // leaving Hunt (information arrived) are never delayed.
    private const int MinDwellTurns = 2;

    private readonly GridManager gridManager;
    private readonly PlayerId owner;
    private readonly int startingFleetSize;

    private int enemyAliveEstimate;
    private Vector2Int homeCentroid;
    private bool homeSet;
    private int turnsInStance;

    public FleetStance CurrentStance { get; private set; } = FleetStance.Hunt;
    public FleetSnapshot LastSnapshot { get; private set; }
    public int EnemyAliveEstimate => enemyAliveEstimate;

    public FleetCommander(GridManager gridManager, PlayerId owner)
    {
        this.gridManager = gridManager;
        this.owner = owner;

        // fleetRoster (not the live ship list): the opponent could already have killed one of
        // our ships before our first Move. Both sides always field the same fleet size, so this
        // is also the honest starting estimate for the enemy.
        startingFleetSize = gridManager.Match.GetPlayer(owner).fleetRoster.Count;
        enemyAliveEstimate = startingFleetSize;
    }

    // Called by AIController after one of our attacks destroys its target.
    public void NoteKillConfirmed()
    {
        enemyAliveEstimate = Mathf.Max(0, enemyAliveEstimate - 1);
        Debug.Log($"[AI][Fleet] Kill confirmed. Enemy ships estimated alive: {enemyAliveEstimate}/{startingFleetSize}");
    }

    // Once per turn, at Move. Builds the snapshot, chooses a stance, logs it.
    public FleetSnapshot Evaluate(AITurnContext context, FleetStance? debugForceStance = null)
    {
        FleetSnapshot snapshot = BuildSnapshot(context);
        LastSnapshot = snapshot;

        if (snapshot.Members.Count == 0)
        {
            Debug.Log("[AI][Fleet] No living ships; nothing to evaluate.");
            return snapshot;
        }

        FleetStance desired = debugForceStance ?? ChooseStance(snapshot);
        if (debugForceStance.HasValue)
        {
            Debug.Log($"[AI][Fleet] Debug override active: forcing stance={debugForceStance.Value}");
        }
        FleetStance previous = CurrentStance;
        FleetStance chosen = desired;
        string held = "";

        if (desired != CurrentStance && IsLockedByDwell(desired))
        {
            held = $" (wanted {desired}, held by dwell {turnsInStance}/{MinDwellTurns})";
            chosen = CurrentStance;
        }

        string tag;
        if (chosen != CurrentStance)
        {
            CurrentStance = chosen;
            turnsInStance = 1;
            tag = $" (changed from {previous})";
        }
        else
        {
            turnsInStance++;
            tag = $" (turn {turnsInStance} in stance)";
        }

        Debug.Log($"[AI][Fleet] stance={CurrentStance}{tag}{held} " +
                $"score={snapshot.MaterialScore:F2} ownAlive={snapshot.OwnAlive}/{snapshot.OwnStartCount} " +
                $"enemyEst={snapshot.EnemyAliveEstimate} fleetHp={snapshot.FleetHpFraction:F2} " +
                $"contacts={snapshot.Contacts.Count} nearest={snapshot.NearestContactDistance} " +
                $"progress={snapshot.MapProgress:F2} centroid={snapshot.FleetCentroid} home={snapshot.HomeCentroid}");
        LogRoster(snapshot);
        LogContacts(snapshot);
        return snapshot;
    }

    // At Battle, after Search may have revealed more. Refreshes contacts only: no stance
    // change and no dwell tick, so stances stay once-per-turn.
    public FleetSnapshot RefreshContacts(AITurnContext context)
    {
        FleetSnapshot snapshot = BuildSnapshot(context);
        LastSnapshot = snapshot;

        Debug.Log($"[AI][Fleet] Battle refresh: stance={CurrentStance} contacts={snapshot.Contacts.Count} " +
                $"nearest={snapshot.NearestContactDistance}");
        LogContacts(snapshot);
        return snapshot;
    }

    // Stage 4: Target prioritization for coordinated battle phase.
    // Returns known enemies sorted by priority: Threat × Fragility.
    // High-threat fragile targets (low HP, dangerous weapons) go first.
    public List<ShipInstance> PrioritizeTargets(List<ShipInstance> knownEnemies, GridManager gridManager)
    {
        if (LastSnapshot == null || knownEnemies.Count == 0)
            return new List<ShipInstance>(knownEnemies);

        var prioritized = new List<(ShipInstance enemy, float priority)>();

        foreach (ShipInstance enemy in knownEnemies)
        {
            float threat = CalculateThreat(enemy, gridManager);
            float fragility = enemy.maxHealth > 0
                ? 1f - Mathf.Clamp01((float)enemy.currentHealth / enemy.maxHealth)
                : 1f;

            // Priority = Threat × (1 + Fragility)
            // Fragile high-threat targets score highest.
            // +1 ensures even full-HP threats have nonzero priority.
            float priority = threat * (1f + fragility);
            prioritized.Add((enemy, priority));
        }

        // Sort descending by priority
        prioritized.Sort((a, b) => b.priority.CompareTo(a.priority));

        var result = new List<ShipInstance>();
        foreach (var (enemy, priority) in prioritized)
        {
            result.Add(enemy);
        }

        return result;
    }

    // Threat = sum of weapon expected values, with range multiplier.
    // Weapons currently in range of fleet centroid get 1.5× weight.
    private float CalculateThreat(ShipInstance enemy, GridManager gridManager)
    {
        if (LastSnapshot == null || enemy.weapons == null)
            return 0f;

        float threat = 0f;
        Vector2Int fleetPos = LastSnapshot.FleetCentroid;

        foreach (WeaponProfile weapon in enemy.weapons)
        {
            float expectedDamage = AIScoring.ExpectedValue(weapon);
            int distToFleet = gridManager.DistanceBetween(enemy.anchor, fleetPos);

            // Range multiplier: in-range weapons are more threatening
            float rangeMult = distToFleet <= weapon.weaponRange ? 1.5f : 1.0f;
            threat += expectedDamage * rangeMult;
        }

        return threat;
    }

    // ---- Stance choice -------------------------------------------------------

    private FleetStance ChooseStance(FleetSnapshot snapshot)
    {
        float score = snapshot.MaterialScore;
        float hp = snapshot.FleetHpFraction;
        FleetStance cur = CurrentStance;

        // Safety and "losing" conditions apply whether or not anything is currently visible.
        float retreatStay = cur == FleetStance.Retreat ? HysteresisMargin : 0f;
        if (score < RetreatScoreBelow + retreatStay || hp < RetreatHpBelow + retreatStay)
        {
            return FleetStance.Retreat;
        }

        float fallbackStay = cur == FleetStance.DefensiveFallback ? HysteresisMargin : 0f;
        if (score < FallbackScoreBelow + fallbackStay)
        {
            return FleetStance.DefensiveFallback;
        }

        if (snapshot.Contacts.Count == 0)
        {
            return FleetStance.Hunt;
        }

        float advanceStay = cur == FleetStance.Advance ? HysteresisMargin : 0f;
        if (score >= AdvanceScoreAtLeast - advanceStay)
        {
            return FleetStance.Advance;
        }

        // Flank is intentionally never chosen in Stage 1.
        return FleetStance.Hold;
    }

    private bool IsLockedByDwell(FleetStance desired)
    {
        if (CurrentStance == FleetStance.Hunt) return false;        // information arrived
        if (desired == FleetStance.Retreat) return false;           // safety override
        return turnsInStance < MinDwellTurns;
    }

    // ---- Snapshot ------------------------------------------------------------

    private FleetSnapshot BuildSnapshot(AITurnContext context)
    {
        var snap = new FleetSnapshot
        {
            Owner = owner,
            CurrentStance = CurrentStance,
            OwnStartCount = startingFleetSize,
            EnemyAliveEstimate = enemyAliveEstimate
        };

        int totalCurrent = 0;
        int totalMax = 0;
        int sumX = 0;
        int sumY = 0;

        foreach (ShipInstance ship in context.MyShips)
        {
            FleetRoleEntry entry = FleetRoles.Get(ship.shipType);
            float hpFraction = ship.maxHealth > 0
                ? Mathf.Clamp01((float)ship.currentHealth / ship.maxHealth)
                : 0f;

            snap.Members.Add(new FleetMember
            {
                Ship = ship,
                Role = entry.Role,
                StaticFragility = entry.StaticFragility,
                HpFraction = hpFraction,
                EffectiveFragility = 1f - (1f - entry.StaticFragility) * hpFraction
            });

            totalCurrent += ship.currentHealth;
            totalMax += ship.maxHealth;
            sumX += ship.anchor.x;
            sumY += ship.anchor.y;
        }

        snap.OwnAlive = snap.Members.Count;
        snap.FleetHpFraction = totalMax > 0 ? Mathf.Clamp01((float)totalCurrent / totalMax) : 0f;

        float ratio = (float)snap.OwnAlive / Mathf.Max(1, snap.EnemyAliveEstimate);
        snap.MaterialScore = ratio * (0.5f + 0.5f * snap.FleetHpFraction);

        if (snap.OwnAlive > 0)
        {
            snap.FleetCentroid = new Vector2Int(
                Mathf.RoundToInt((float)sumX / snap.OwnAlive),
                Mathf.RoundToInt((float)sumY / snap.OwnAlive));

            // Home = where the fleet stood the first time the commander looked (our first Move,
            // before anything has moved). Avoids hardcoding which edge we deploy on.
            if (!homeSet)
            {
                homeCentroid = snap.FleetCentroid;
                homeSet = true;
            }
        }

        snap.HomeCentroid = homeCentroid;
        snap.MapProgress = ComputeMapProgress(snap.FleetCentroid);

        snap.Contacts = AIContactBuilder.Build(gridManager, context.MyFog);
        FindNearestContact(snap);
        return snap;
    }

    private float ComputeMapProgress(Vector2Int centroid)
    {
        var center = new Vector2Int(gridManager.width / 2, gridManager.height / 2);
        Vector2 toCenter = center - homeCentroid;
        float lengthSquared = toCenter.sqrMagnitude;
        if (lengthSquared < 0.01f) return 0f;

        Vector2 moved = centroid - homeCentroid;
        return Mathf.Clamp(Vector2.Dot(moved, toCenter) / lengthSquared, -1f, 2f);
    }

    private void FindNearestContact(FleetSnapshot snap)
    {
        int best = int.MaxValue;
        foreach (AIContact contact in snap.Contacts)
        {
            foreach (Vector2Int contactCell in contact.Cells)
            {
                foreach (FleetMember member in snap.Members)
                {
                    foreach (Vector2Int shipCell in member.Ship.GetOccupiedCells())
                    {
                        int d = gridManager.DistanceBetween(shipCell, contactCell);
                        if (d < best)
                        {
                            best = d;
                            snap.NearestContact = contact;
                        }
                    }
                }
            }
        }

        snap.NearestContactDistance = best == int.MaxValue ? -1 : best;
    }

    // ---- Logging -------------------------------------------------------------

    private static void LogRoster(FleetSnapshot snap)
    {
        var sb = new StringBuilder("[AI][Fleet] roster:");
        foreach (FleetMember m in snap.Members)
        {
            sb.Append($" {m.Ship.shipType}={m.Role} frag={m.EffectiveFragility:F2}(hp {m.HpFraction:F2});");
        }
        Debug.Log(sb.ToString());
    }

    private static void LogContacts(FleetSnapshot snap)
    {
        for (int i = 0; i < snap.Contacts.Count; i++)
        {
            AIContact c = snap.Contacts[i];
            string candidates = string.Join(",", c.Candidates);
            string identified = c.IsIdentified ? string.Join(",", c.IdentifiedClasses) : "none";
            Debug.Log($"[AI][Fleet] contact#{i} cells={c.Cells.Count} centroid={c.Centroid} " +
                    $"minShips={c.MinShips} candidates=[{candidates}] identified={identified}");
        }
    }
}