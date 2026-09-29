using System.Collections.Generic;
using UnityEngine;

// AI-side memory of where enemies were actually last seen, kept separate
// from FogGrid on purpose -- FogGrid is deliberately non-sticky (a cell
// reverts to Unknown the instant nothing currently detects it, per
// AGENTS.md's fog design) and that stays untouched. This exists purely to
// give AIActiveScanPlanner an informed guess of where to aim a future scan:
// the last cell a ship was seen at, a rough "it was heading this way"
// direction inferred from the sighting before that, and how many of the
// ship's own full turns have passed since, so the guess expands outward
// the longer it stays hidden instead of assuming it just stood still.
// This never feeds back into FogGrid or the combat fog gate -- it only
// biases where AIActiveScanPlanner points a future scan.
public static class AIEnemyMemory
{
    private class Sighting
    {
        public Vector2Int LastCell;
        public Vector2Int PreviousCell;
        public int MovementRange;
        public int TurnsSinceSeen;
    }

    private static readonly Dictionary<PlayerId, Dictionary<ShipInstance, Sighting>> memory =
        new Dictionary<PlayerId, Dictionary<ShipInstance, Sighting>>();

    // livingEnemies is every enemy ship still alive; knownEnemies is the
    // subset currently visible in fog. A living ship missing from
    // knownEnemies keeps its last sighting but ages it -- fog already
    // forgets instantly, this is deliberately the opposite.
    //
    // advanceTurn controls whether hidden ships actually age this call.
    // AiController calls this from both Move and Battle phase (Battle can
    // see something Move didn't yet, thanks to the Search phase in
    // between), but a hidden ship's clock should only tick once per real
    // turn, not once per phase -- so only the Move-phase call passes true.
    public static void Remember(
        PlayerId owner,
        List<ShipInstance> livingEnemies,
        List<ShipInstance> knownEnemies,
        bool advanceTurn)
    {
        if (!memory.TryGetValue(owner, out Dictionary<ShipInstance, Sighting> sightings))
        {
            sightings = new Dictionary<ShipInstance, Sighting>();
            memory[owner] = sightings;
        }

        // Forget ships that are dead entirely -- no point predicting where
        // a destroyed ship "might be" now.
        var toForget = new List<ShipInstance>();
        foreach (ShipInstance tracked in sightings.Keys)
        {
            if (!livingEnemies.Contains(tracked))
            {
                toForget.Add(tracked);
            }
        }
        foreach (ShipInstance dead in toForget)
        {
            sightings.Remove(dead);
        }

        foreach (ShipInstance enemy in livingEnemies)
        {
            if (knownEnemies.Contains(enemy))
            {
                // A fresh sighting always refreshes, regardless of
                // advanceTurn -- new information is new information.
                if (sightings.TryGetValue(enemy, out Sighting existing))
                {
                    // Only shift the heading on a genuinely new observation
                    // (the once-per-turn Move call, or a ship that had been
                    // hidden and is now re-seen). The Battle-phase call sees
                    // the same, unmoved enemy as Move did; shifting again
                    // would overwrite PreviousCell with LastCell and erase
                    // the heading every single turn.
                    bool newObservation = advanceTurn || existing.TurnsSinceSeen > 0;
                    if (newObservation)
                    {
                        existing.PreviousCell = existing.LastCell;
                    }

                    existing.LastCell = enemy.anchor;
                    existing.MovementRange = enemy.movementRange;
                    existing.TurnsSinceSeen = 0;
                }
                else
                {
                    sightings[enemy] = new Sighting
                    {
                        LastCell = enemy.anchor,
                        PreviousCell = enemy.anchor,
                        MovementRange = enemy.movementRange,
                        TurnsSinceSeen = 0
                    };
                }
            }
            else if (advanceTurn && sightings.TryGetValue(enemy, out Sighting hidden))
            {
                hidden.TurnsSinceSeen++;
            }
        }
    }

    // Best guess at the nearest currently-plausible enemy cell to `from`.
    // Extrapolates from the last observed heading, capped by how far that
    // ship could actually have moved (its own movementRange per hidden
    // turn) so the guess never claims a physically impossible position.
    public static bool TryGetNearestPrediction(PlayerId owner, Vector2Int from, out Vector2Int predictedCell)
    {
        predictedCell = default;
        if (!memory.TryGetValue(owner, out Dictionary<ShipInstance, Sighting> sightings) || sightings.Count == 0)
        {
            return false;
        }

        int bestDist = int.MaxValue;
        bool found = false;

        foreach (Sighting sighting in sightings.Values)
        {
            Vector2Int predicted = Predict(sighting);
            int dist = Mathf.Max(Mathf.Abs(predicted.x - from.x), Mathf.Abs(predicted.y - from.y));
            if (dist < bestDist)
            {
                bestDist = dist;
                predictedCell = predicted;
                found = true;
            }
        }

        return found;
    }

    // Simple dead-reckoning: project the last observed heading forward by
    // however many turns it's been hidden, then clamp the displacement to
    // what that ship's own movement budget could actually cover over that
    // many turns. A ship only ever seen once (no observed heading yet) is
    // assumed to have stayed put -- the least presumptuous guess available.
    private static Vector2Int Predict(Sighting sighting)
    {
        if (sighting.TurnsSinceSeen <= 0)
        {
            return sighting.LastCell;
        }

        Vector2Int heading = sighting.LastCell - sighting.PreviousCell;
        if (heading == Vector2Int.zero)
        {
            return sighting.LastCell;
        }

        Vector2Int rawOffset = heading * sighting.TurnsSinceSeen;
        int maxDisplacement = sighting.MovementRange * sighting.TurnsSinceSeen;
        int rawDist = Mathf.Max(Mathf.Abs(rawOffset.x), Mathf.Abs(rawOffset.y));

        if (rawDist > maxDisplacement && rawDist > 0)
        {
            float scale = maxDisplacement / (float)rawDist;
            rawOffset = new Vector2Int(
                Mathf.RoundToInt(rawOffset.x * scale),
                Mathf.RoundToInt(rawOffset.y * scale));
        }

        return sighting.LastCell + rawOffset;
    }
}