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
        }

        // Mine reveal: if an active scan covers a cell that contains an enemy mine,
        // mark it in the scanner's fog. Only active scans reveal mines — passive fog
        // is never updated with mine positions per the locked design rules.
        PlayerId enemyOwner = ship.owner == PlayerId.PlayerA ? PlayerId.PlayerB : PlayerId.PlayerA;
        foreach (MineTile mine in match.mines)
        {
            if (mine.owner != enemyOwner) continue; // only reveal enemy mines
            if (!detectedCells.Contains(mine.position)) continue;

            fog.MarkActive(mine.position, FogState.Marked);
            Debug.Log($"[MINE][FOG] {ship.owner} active scan revealed enemy mine at {mine.position}.");
        }
    }

    public void ClearAllActiveMarks()
    {
        playerAFog.ClearActiveMarks();
        playerBFog.ClearActiveMarks();
    }

    public void ClearMarksForCells(IEnumerable<Vector2Int> cells)
    {
        if (cells == null) return;
        playerAFog.ClearCells(cells);
        playerBFog.ClearCells(cells);
    }

    public void ClearMarksForShip(ShipInstance ship)
    {
        if (ship == null) return;
        ClearMarksForCells(ship.GetOccupiedCells());
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
                }
            }
        }

        // Plane vision: planes owned by this player contribute passive absolute halo vision.
        // Planes see over terrain (no LOS blocking).
        foreach (PlaneUnit plane in match.planes)
        {
            if (plane.owner != owner) continue;

            VisionLayer layer = plane.visionLayer;
            if (layer == null || !layer.isPassive) continue;

            FogState result = layer.visionType == VisionType.Absolute ? FogState.Identified : FogState.Marked;
            VisionScanResult scan = VisionResolver.GetScanResult(plane, layer, enemies);
            List<Vector2Int> detectedCells = scan.DetectedCells;

            string cells = detectedCells.Count == 0
                ? "none"
                : string.Join(", ", detectedCells.ConvertAll(cell => cell.ToString()).ToArray());

            Debug.Log(
                $"[VISION][PASSIVE][{ShapeLabel(layer)}][{VisionTypeLabel(layer)}] " +
                $"plane owner={plane.owner} layer=\"{layer.id}\" " +
                $"range={layer.range} detects={layer.detects} " +
                $"applied={result} cells={detectedCells.Count} [{cells}] pos={plane.position}");

            foreach (Vector2Int cell in detectedCells)
            {
                fog.MarkPassive(cell, result);
            }
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