using System.Collections.Generic;
using UnityEngine;

// Decides which of the AI's ships runs the fleet's one active scan per turn,
// and which direction to aim it.
//
// Selection priority (Stage 4 search coordination):
//   1. Only ships whose active Cone layer detects Surface or Both are candidates.
//      A SubSurface-only cone (e.g. Athena's sonar) reveals nothing on the surface,
//      so choosing it would waste the fleet's single scan.
//   2. Among candidates, Scout-role ships go first (Wolf is the current Scout).
//   3. Among ships at the same role priority, the one whose best facing covers the
//      most currently-unknown fog cells wins.
//
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

    // Entry point called by AIController during the Search phase.
    // Picks exactly one ship and runs its scan; does not loop the rest.
    public static void RunActiveScans(AITurnContext context, GridManager gridManager)
    {
        ShipInstance scanner = SelectScanner(context, gridManager);
        if (scanner == null)
        {
            Debug.Log("[AI][Scan] No surface-capable scanner available this turn.");
            return;
        }

        TryActiveScan(scanner, context, gridManager);
    }

    // ---- Scanner selection --------------------------------------------------

    // Returns the best ship to use the fleet's scan this turn, or null if
    // no ship has a useful (surface-capable) active cone layer.
    private static ShipInstance SelectScanner(AITurnContext context, GridManager gridManager)
    {
        ShipInstance bestShip = null;
        VisionLayer bestLayer = null;
        Vector2Int bestForward = default;
        float bestScore = float.NegativeInfinity;
        bool bestIsScout = false;

        foreach (ShipInstance ship in context.MyShips)
        {
            VisionLayer layer = FindSurfaceActiveConeLayer(ship);
            if (layer == null)
                continue; // no surface-capable active cone on this ship

            VisionResolver.GetBowAndFacing(ship, out Vector2Int bow, out Vector2Int _);
            ScoreFacing(ship, layer, bow, context.MyFog, gridManager,
                        out Vector2Int forward, out float score);

            bool isScout = FleetRoles.Get(ship.shipType).Role == FleetRole.Scout;

            // Scout beats non-Scout regardless of score.
            // Within the same scout tier, higher fog-coverage score wins.
            bool better = false;
            if (bestShip == null)
            {
                better = true;
            }
            else if (isScout && !bestIsScout)
            {
                better = true;
            }
            else if (isScout == bestIsScout && score > bestScore)
            {
                better = true;
            }

            if (better)
            {
                bestShip    = ship;
                bestLayer   = layer;
                bestForward = forward;
                bestScore   = score;
                bestIsScout = isScout;
            }
        }

        if (bestShip != null)
        {
            Debug.Log($"[AI][Scan] Selected scanner: {bestShip.shipType} " +
                      $"(role={FleetRoles.Get(bestShip.shipType).Role}, " +
                      $"layer={bestLayer.id}, detects={bestLayer.detects}, " +
                      $"facing={bestForward}, score={bestScore:F0})");
        }

        return bestShip;
    }

    // ---- Per-ship scan execution --------------------------------------------

    // Activates and confirms the scan for the chosen ship. Returns true on
    // success. GridManager still acts as the hard gate (phase check, fleet
    // scan limit) so a false return here is a genuine unexpected rejection.
    private static bool TryActiveScan(ShipInstance ship, AITurnContext context, GridManager gridManager)
    {
        VisionLayer layer = FindSurfaceActiveConeLayer(ship);
        if (layer == null)
            return false;

        VisionResolver.GetBowAndFacing(ship, out Vector2Int bow, out Vector2Int _);
        ScoreFacing(ship, layer, bow, context.MyFog, gridManager,
                    out Vector2Int bestForward, out float _);

        if (!gridManager.ActivateActiveScan(ship, bestForward))
        {
            Debug.LogWarning($"[AI][Scan] ActivateActiveScan rejected for {ship.shipType} -- unexpected.");
            return false;
        }

        gridManager.ConfirmActiveScan();
        Debug.Log($"[AI][Scan] {ship.shipType} scanned facing {bestForward}.");
        return true;
    }

    // ---- Layer helpers ------------------------------------------------------

    // Returns the first active (non-passive) Cone layer that can detect
    // Surface or Both-domain targets, or null if none exists.
    // SubSurface-only cones are excluded: they reveal nothing on the
    // surface and would waste the fleet's single scan per turn.
    private static VisionLayer FindSurfaceActiveConeLayer(ShipInstance ship)
    {
        foreach (VisionLayer layer in ship.visionLayers)
        {
            if (!layer.isPassive
                && layer.shape == ShapeType.Cone
                && layer.detects != DomainType.SubSurface)
            {
                return layer;
            }
        }
        return null;
    }

    // ---- Facing scorer ------------------------------------------------------

    // Scores all four cardinal facings and returns the best one plus its
    // score. Extracted from the old ChooseBestForward so SelectScanner can
    // compare scores across ships without activating anything.
    //
    // Score = unknown fog cells covered by the cone in that direction,
    // plus BearingWeight * alignment with AIEnemyMemory's nearest
    // predicted enemy position (when a prediction exists).
    private static void ScoreFacing(
        ShipInstance ship,
        VisionLayer layer,
        Vector2Int bow,
        FogGrid myFog,
        GridManager gridManager,
        out Vector2Int bestForward,
        out float bestScore)
    {
        bool hasPrediction = AIEnemyMemory.TryGetNearestPrediction(
            ship.owner, bow, out Vector2Int predictedCell);

        Vector2Int towardPrediction = hasPrediction
            ? new Vector2Int(
                System.Math.Sign(predictedCell.x - bow.x),
                System.Math.Sign(predictedCell.y - bow.y))
            : Vector2Int.zero;

        bestForward = CardinalForwards[0];
        bestScore   = float.NegativeInfinity;

        foreach (Vector2Int candidate in CardinalForwards)
        {
            HashSet<Vector2Int> coneCells = VisionResolver.GetConeCells(bow, candidate, layer.range);

            int unknownCoverage = 0;
            foreach (Vector2Int cell in coneCells)
            {
                // GetConeCells doesn't bounds-check; off-board cells would
                // read as "unknown" and wrongly reward edge-facing directions.
                if (!gridManager.IsInBounds(cell))
                    continue;

                if (!myFog.IsKnown(cell))
                    unknownCoverage++;
            }

            float score = unknownCoverage;

            if (hasPrediction)
            {
                // Dot product of two sign-vectors: +1 when facing matches
                // the bearing, 0 when perpendicular, -1 when opposite.
                // Diagonal bearings tie two cardinals; fog coverage breaks it.
                int alignment = candidate.x * towardPrediction.x
                              + candidate.y * towardPrediction.y;
                score += alignment * BearingWeight;
            }

            if (score > bestScore)
            {
                bestScore   = score;
                bestForward = candidate;
            }
        }
    }
}
