using System.Collections.Generic;
using UnityEngine;

// Decides *when* vision runs and who it runs for. The rules themselves live in VisionResolver.
public class FogManager
{
    private readonly GridManager gridManager;
    private readonly FogGrid playerAFog = new FogGrid();
    private readonly FogGrid playerBFog = new FogGrid();

    public FogManager(GridManager gridManager)
    {
        this.gridManager = gridManager;
    }

    public FogGrid GetFogGrid(PlayerId owner)
    {
        return owner == PlayerId.PlayerA ? playerAFog : playerBFog;
    }

    // Rebuilt from scratch every time so fog is never sticky (Milestone 5 design decision).
    public void RecomputeAllPassive(MatchState match)
    {
        RecomputePassive(PlayerId.PlayerA, match);
        RecomputePassive(PlayerId.PlayerB, match);
    }

    public void RunActiveSearch(
        ShipInstance ship,
        VisionLayer layer,
        Vector2Int bow,
        Vector2Int forward,
        MatchState match)
    {
        if (ship == null || layer == null || layer.isPassive)
        {
            return;
        }

        FogGrid fog = GetFogGrid(ship.owner);
        List<ShipInstance> enemies = GetEnemyShips(ship.owner, match);
        VisionScanResult scan = VisionResolver.GetScanResult(
            ship, layer, enemies, gridManager, bow, forward);
        List<Vector2Int> detectedCells = scan.DetectedCells;

        // Active scans only ever Mark: they reveal that something is there, never what it is.
        LogScanSummary(
            "NON-PASSIVE",
            ship,
            layer,
            detectedCells,
            FogState.Marked,
            $"bow={bow} forward={forward}");

        foreach (Vector2Int cell in detectedCells)
        {
            fog.MarkActive(cell, FogState.Marked);
            Debug.Log($"[VISION][NON-PASSIVE][{ShapeLabel(layer)}][{VisionTypeLabel(layer)}] " +
                      $"ship={ship.shipType} owner={ship.owner} cell={cell} " +
                      "applied=Marked layer=active");
        }

        LogBlockedCells("NON-PASSIVE", ship, layer, scan.BlockedCells);
    }

    public void ClearAllActiveMarks()
    {
        playerAFog.ClearActiveMarks();
        playerBFog.ClearActiveMarks();
    }

    private void RecomputePassive(PlayerId owner, MatchState match)
    {
        FogGrid fog = GetFogGrid(owner);
        List<ShipInstance> enemies = GetEnemyShips(owner, match);
        fog.ResetPassive();

        foreach (var ship in match.GetPlayer(owner).ships)
        {
            foreach (var layer in ship.visionLayers)
            {
                if (!layer.isPassive) continue;

                // Absolute layers identify, Sensor layers only mark.
                FogState result = layer.visionType == VisionType.Absolute ? FogState.Identified : FogState.Marked;
                VisionResolver.GetBowAndFacing(
                    ship,
                    out Vector2Int bow,
                    out Vector2Int forward);
                VisionScanResult scan = VisionResolver.GetScanResult(
                    ship, layer, enemies, gridManager, bow, forward);
                List<Vector2Int> detectedCells = scan.DetectedCells;

                LogScanSummary(
                    "PASSIVE",
                    ship,
                    layer,
                    detectedCells,
                    result,
                    $"domain={ship.currentDomain}");

                foreach (Vector2Int cell in detectedCells)
                {
                    fog.MarkPassive(cell, result);
                    Debug.Log($"[VISION][PASSIVE][{ShapeLabel(layer)}][{VisionTypeLabel(layer)}] " +
                              $"ship={ship.shipType} owner={ship.owner} cell={cell} " +
                              $"applied={result} layer=passive");
                }

                LogBlockedCells("PASSIVE", ship, layer, scan.BlockedCells);
            }
        }
    }

    private static void LogBlockedCells(
        string activation,
        ShipInstance ship,
        VisionLayer layer,
        Dictionary<Vector2Int, Vector2Int> blockedCells)
    {
        foreach (KeyValuePair<Vector2Int, Vector2Int> blocked in blockedCells)
        {
            Debug.Log(
                $"[VISION][{activation}][{ShapeLabel(layer)}][{VisionTypeLabel(layer)}] " +
                $"ship={ship.shipType} owner={ship.owner} cell={blocked.Key} " +
                $"result=BLOCKED blocker={blocked.Value}");
        }
    }

    private static void LogScanSummary(
        string activation,
        ShipInstance ship,
        VisionLayer layer,
        List<Vector2Int> detectedCells,
        FogState appliedState,
        string context)
    {
        string cells = detectedCells.Count == 0
            ? "none"
            : string.Join(", ", detectedCells.ConvertAll(cell => cell.ToString()).ToArray());

        Debug.Log(
            $"[VISION][{activation}][{ShapeLabel(layer)}][{VisionTypeLabel(layer)}] " +
            $"ship={ship.shipType} owner={ship.owner} layer=\"{layer.id}\" " +
            $"range={layer.range} detects={layer.detects} sourceDomain={ship.currentDomain} " +
            $"applied={appliedState} cells={detectedCells.Count} [{cells}] {context}");
    }

    private static string ShapeLabel(VisionLayer layer)
    {
        return layer.shape.ToString().ToUpperInvariant();
    }

    private static string VisionTypeLabel(VisionLayer layer)
    {
        return layer.visionType.ToString().ToUpperInvariant();
    }

    private static List<ShipInstance> GetEnemyShips(PlayerId owner, MatchState match)
    {
        PlayerId enemy = owner == PlayerId.PlayerA ? PlayerId.PlayerB : PlayerId.PlayerA;
        return match.GetPlayer(enemy).ships;
    }
}