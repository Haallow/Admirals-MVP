using System.Collections.Generic;
using UnityEngine;

// Stateless vision rules: which enemy cells does a vision layer detect?
// Lives apart from FogGrid on purpose. FogGrid is state, this is behavior (AGENTS.md: data vs. behavior).
public static class VisionResolver
{
    // 1 => a 90-degree cone (one extra cell of width per side per cell of distance).
    // A single constant so the cone can be retuned without touching the geometry.
    public const int ConeSlope = 1;

    public static List<Vector2Int> GetDetectedCells(ShipInstance source, VisionLayer layer, List<ShipInstance> enemies)
    {
        GetBowAndFacing(source, out Vector2Int bow, out Vector2Int forward);
        return GetDetectedCells(source, layer, enemies, bow, forward);
    }

    public static List<Vector2Int> GetDetectedCells(
        ShipInstance source,
        VisionLayer layer,
        List<ShipInstance> enemies,
        Vector2Int bow,
        Vector2Int forward)
    {
        return GetScanResult(source, layer, enemies, null, bow, forward).DetectedCells;
    }

    public static VisionScanResult GetScanResult(
        ShipInstance source,
        VisionLayer layer,
        List<ShipInstance> enemies,
        GridManager gridManager,
        Vector2Int bow,
        Vector2Int forward)
    {
        var result = new VisionScanResult();

        // Safety net: fleet lists drop destroyed ships, but a ship killed mid-phase must never scan.
        if (source.currentHealth <= 0) return result;

        // Why here: a submerged ship loses surfaced-only sensors (e.g. the Wolf's Default Absolute Vision).
        if (layer.onlyWhileSurfaced && source.currentDomain != DomainType.Surface) return result;

        // Only shapes that exist are evaluated; anything else detects nothing instead of everything.
        if (layer.shape != ShapeType.Halo && layer.shape != ShapeType.Cone) return result;

        var sourceCells = source.GetOccupiedCells();

        // The cone only needs building once per layer, not once per enemy cell.
        HashSet<Vector2Int> coneCells = null;
        if (layer.shape == ShapeType.Cone)
        {
            coneCells = GetConeCells(bow, forward, layer.range);
        }

        foreach (var enemy in enemies)
        {
            if (enemy.currentHealth <= 0) continue;

            // A Surface-only layer cannot see a submerged ship, and vice versa.
            if (layer.detects != DomainType.Both && layer.detects != enemy.currentDomain) continue;

            foreach (var cell in enemy.GetOccupiedCells())
            {
                // Only cells inside the shape count, not the enemy's whole hull.
                bool inShape = layer.shape == ShapeType.Cone
                    ? coneCells.Contains(cell)
                    : IsWithinHalo(cell, sourceCells, layer.range);

                if (!inShape)
                {
                    continue;
                }

                if (gridManager == null ||
                    IsVisibleFromAnySource(
                        cell,
                        layer.shape == ShapeType.Cone
                            ? new List<Vector2Int> { bow }
                            : sourceCells,
                        gridManager,
                        out Vector2Int blocker))
                {
                    result.DetectedCells.Add(cell);
                }
                else
                {
                    result.BlockedCells[cell] = blocker;
                }
            }
        }

        return result;
    }

    // Enemy cells are always on the board, so the cone needs no bounds check: cells off the
    // board simply never match anything.
    public static HashSet<Vector2Int> GetConeCells(Vector2Int origin, Vector2Int forward, int range)
    {
        var cells = new HashSet<Vector2Int>();
        Vector2Int perpendicular = new Vector2Int(-forward.y, forward.x);

        // Starts at 1: the cone begins one cell past the bow, not on it.
        for (int d = 1; d <= range; d++)
        {
            int halfWidth = d * ConeSlope;
            for (int l = -halfWidth; l <= halfWidth; l++)
            {
                cells.Add(origin + forward * d + perpendicular * l);
            }
        }

        return cells;
    }

    // Facing is derived from the footprint itself instead of re-implementing FootprintUtil's
    // rotation convention, so the cone can never disagree with how the hull is drawn.
    // Assumes the anchor is the stern cell (footprints run outward from (0,0)).
    // A 1-cell ship has no intrinsic facing, so it defaults to +X.
    public static void GetBowAndFacing(ShipInstance ship, out Vector2Int bow, out Vector2Int forward)
    {
        bow = ship.anchor;
        int furthest = -1;

        foreach (var cell in ship.GetOccupiedCells())
        {
            int dist = Mathf.Max(Mathf.Abs(cell.x - ship.anchor.x), Mathf.Abs(cell.y - ship.anchor.y));
            if (dist > furthest)
            {
                furthest = dist;
                bow = cell;
            }
        }

        Vector2Int delta = bow - ship.anchor;
        forward = new Vector2Int(System.Math.Sign(delta.x), System.Math.Sign(delta.y));
        if (forward == Vector2Int.zero) forward = Vector2Int.right;
    }

    // A Halo is the footprint expanded by range, which is one homogeneous region
    // even for a long ship. The LOS check later chooses a clear source-to-target ray.
    private static bool IsWithinHalo(Vector2Int cell, List<Vector2Int> sourceCells, int range)
    {
        foreach (var src in sourceCells)
        {
            int dist = Mathf.Max(Mathf.Abs(cell.x - src.x), Mathf.Abs(cell.y - src.y));
            if (dist <= range) return true;
        }
        return false;
    }

    private static bool IsVisibleFromAnySource(
        Vector2Int target,
        List<Vector2Int> sources,
        GridManager gridManager,
        out Vector2Int blocker)
    {
        blocker = default(Vector2Int);
        bool hasBlocker = false;

        foreach (Vector2Int source in sources)
        {
            if (!TryGetFirstBlockingCell(source, target, gridManager, out Vector2Int candidateBlocker))
            {
                return true;
            }

            if (!hasBlocker)
            {
                blocker = candidateBlocker;
                hasBlocker = true;
            }
        }

        return false;
    }

    public static bool TryGetFirstBlockingCell(
        Vector2Int source,
        Vector2Int target,
        GridManager gridManager,
        out Vector2Int blocker)
    {
        blocker = default(Vector2Int);
        foreach (Vector2Int cell in GetSupercoverCells(source, target))
        {
            if (cell == source || cell == target)
            {
                continue;
            }

            if (gridManager.GetTerrainType(cell) == TerrainType.Impassable)
            {
                blocker = cell;
                return true;
            }
        }

        return false;
    }

    // Supercover Bresenham: corner ties include both side cells and the diagonal
    // cell, so vision cannot pass between two corner-adjacent blockers.
    private static IEnumerable<Vector2Int> GetSupercoverCells(
        Vector2Int source,
        Vector2Int target)
    {
        int dx = target.x - source.x;
        int dy = target.y - source.y;
        int stepX = System.Math.Sign(dx);
        int stepY = System.Math.Sign(dy);
        int nx = Mathf.Abs(dx);
        int ny = Mathf.Abs(dy);
        int x = source.x;
        int y = source.y;
        int ix = 0;
        int iy = 0;

        yield return source;

        while (ix < nx || iy < ny)
        {
            long lhs = (long)(1 + 2 * ix) * ny;
            long rhs = (long)(1 + 2 * iy) * nx;

            if (lhs == rhs && ix < nx && iy < ny)
            {
                int oldX = x;
                int oldY = y;
                x += stepX;
                ix++;
                yield return new Vector2Int(x, oldY);
                y += stepY;
                iy++;
                yield return new Vector2Int(oldX, y);
                yield return new Vector2Int(x, y);
            }
            else if (lhs < rhs && ix < nx)
            {
                x += stepX;
                ix++;
                yield return new Vector2Int(x, y);
            }
            else
            {
                y += stepY;
                iy++;
                yield return new Vector2Int(x, y);
            }
        }
    }
}