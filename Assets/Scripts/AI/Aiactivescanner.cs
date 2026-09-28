using System.Collections.Generic;
using UnityEngine;

// Decides whether each of the AI's ships should run an active scan during
// the Search phase, and which of the four cardinal facings to aim it at.
// Named AIActiveScanPlanner rather than AISearchPlanner -- a file of that
// name already exists from a teammate's parallel work on the shared branch
// and hasn't been reconciled with this port yet, so reusing the name here
// would collide with theirs.
public static class AIActiveScanPlanner
{
    // How strongly a facing that agrees with AIEnemyMemory's predicted
    // enemy position outweighs one that merely reveals more unknown fog.
    // Deliberately large relative to a single cone's cell count so a real
    // prediction always wins over pure exploration, while unknown-fog
    // coverage still breaks ties between two equally plausible facings
    // (or decides things entirely once nothing has been seen yet).
    private const float BearingWeight = 100f;

    private static readonly Vector2Int[] CardinalForwards =
    {
        Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down
    };

    public static void RunActiveScans(AITurnContext context, GridManager gridManager)
    {
        foreach (ShipInstance ship in context.MyShips)
        {
            TryActiveScan(ship, context, gridManager);
        }
    }

    // A ship with no non-passive Cone layer at all simply has nothing to
    // activate -- not currently true for either Wolf or Athena, but keeps
    // this safe for any future ship card that omits one.
    private static bool TryActiveScan(ShipInstance ship, AITurnContext context, GridManager gridManager)
    {
        VisionLayer layer = FindActiveConeLayer(ship);
        if (layer == null)
        {
            return false;
        }

        VisionResolver.GetBowAndFacing(ship, out Vector2Int bow, out Vector2Int _);
        Vector2Int bestForward = ChooseBestForward(ship, layer, bow, context.MyFog, gridManager);

        if (!gridManager.ActivateActiveScan(ship, bestForward))
        {
            return false;
        }

        gridManager.ConfirmActiveScan();
        Debug.Log($"[AI] {ship.shipType} ran an active scan facing {bestForward}.");
        return true;
    }

    // Mirrors GridManager.ActivateActiveScan's own layer lookup, so the
    // layer's range is known before anything is actually activated -- same
    // deliberate small duplication AIAttackPlanner uses to mirror
    // CombatResolver's checks for hypothetical reasoning.
    private static VisionLayer FindActiveConeLayer(ShipInstance ship)
    {
        foreach (VisionLayer layer in ship.visionLayers)
        {
            if (!layer.isPassive && layer.shape == ShapeType.Cone)
            {
                return layer;
            }
        }
        return null;
    }

    // Scores each of the 4 cardinal facings by how well it agrees with
    // AIEnemyMemory's predicted enemy position, using currently-unknown
    // fog coverage as the tiebreaker (or the sole factor if nothing has
    // ever been seen this match).
    private static Vector2Int ChooseBestForward(
        ShipInstance ship, VisionLayer layer, Vector2Int bow, FogGrid myFog, GridManager gridManager)
    {
        bool hasPrediction = AIEnemyMemory.TryGetNearestPrediction(ship.owner, bow, out Vector2Int predictedCell);
        Vector2Int towardPrediction = hasPrediction
            ? new Vector2Int(
                System.Math.Sign(predictedCell.x - bow.x),
                System.Math.Sign(predictedCell.y - bow.y))
            : Vector2Int.zero;

        Vector2Int best = CardinalForwards[0];
        float bestScore = float.NegativeInfinity;

        foreach (Vector2Int candidate in CardinalForwards)
        {
            HashSet<Vector2Int> coneCells = VisionResolver.GetConeCells(bow, candidate, layer.range);

            int unknownCoverage = 0;
            foreach (Vector2Int cell in coneCells)
            {
                // GetConeCells doesn't bounds-check, and an off-board cell
                // would read as "unknown" -- which would wrongly reward
                // facings that point at the edge of the map.
                if (!gridManager.IsInBounds(cell))
                {
                    continue;
                }

                if (!myFog.IsKnown(cell))
                {
                    unknownCoverage++;
                }
            }

            float score = unknownCoverage;

            if (hasPrediction)
            {
                // Dot product of two direction "sign vectors": highest
                // when the candidate facing matches the bearing to the
                // prediction. A diagonal bearing ties two cardinal
                // facings, and unknown-fog coverage breaks that tie.
                int alignment = candidate.x * towardPrediction.x + candidate.y * towardPrediction.y;
                score += alignment * BearingWeight;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }
}