using System.Collections.Generic;
using UnityEngine;

// Extracted from GridManager. Pure visualization, reads GridManager's data,
// never mutates it. Owns the visual-only settings (zone colors/width) since
// nothing but drawing needs them.
//
// DrawDebugCones/DrawDebugHalos are no longer "debug" in the throwaway sense,
// per the roadmap they're becoming the real toggleable fog/scan visualization.
// Kept their original names for now; rename when the actual toggle UI lands.
public class GridView : MonoBehaviour
{
    [SerializeField] private GridManager gridManager;

    [Header("Starting Zones")]
    [SerializeField] private int startingZoneWidth = 5;
    [SerializeField] private Color playerZoneColor = new Color(0f, 0.35f, 1f, 0.18f);
    [SerializeField] private Color enemyZoneColor = new Color(1f, 0.1f, 0.1f, 0.18f);

    private void OnDrawGizmos()
    {
        if (gridManager == null) return;

        // TEMPORARY: Scene-view visualization only; does not affect gameplay state.
        DrawStartingZones();
        DrawMovementRanges();

        foreach (var kvp in gridManager.AllTiles)
        {
            Vector2Int pos = kvp.Key;
            Tile tile = kvp.Value;
            Vector3 worldPos = new Vector3(pos.x * gridManager.CellSize, pos.y * gridManager.CellSize, 0f);

            DrawTerrain(tile, worldPos);

            if (tile.Occupant == null) continue;
            if (HasProvisionalPreview(tile.Occupant)) continue;

            // TEMPORARY: Gizmo ship marker for visual verification only.
            Gizmos.color = tile.Occupant.owner == PlayerId.PlayerA ? Color.cyan : Color.red;
            Gizmos.DrawCube(worldPos, Vector3.one * gridManager.CellSize * 0.72f);
        }

        // TEMPORARY: Preview-only rendering; authoritative ship placement remains unchanged.
        DrawProvisionalShips();
        DrawDebugCones();
        DrawDebugHalos();
    }

    private void DrawMovementRanges()
    {
        // TEMPORARY: Shows reachable anchors to verify Dijkstra behavior in the Scene view.
        foreach (ProvisionalMovementState state in gridManager.ProvisionalMoves)
        {
            HashSet<Vector2Int> reachable =
                gridManager.CalculateReachablePreviewAnchors(state);
            Color rangeColor = state.Ship.owner == PlayerId.PlayerA
                ? new Color(0f, 0.8f, 1f, 0.18f)
                : new Color(1f, 0.2f, 0.2f, 0.18f);

            foreach (Vector2Int cell in reachable)
            {
                Vector3 worldPos = new Vector3(
                    cell.x * gridManager.CellSize,
                    cell.y * gridManager.CellSize,
                    0f);
                Gizmos.color = rangeColor;
                Gizmos.DrawCube(
                    worldPos,
                    Vector3.one * gridManager.CellSize * 0.9f);
                Gizmos.color = new Color(
                    rangeColor.r,
                    rangeColor.g,
                    rangeColor.b,
                    0.7f);
                Gizmos.DrawWireCube(
                    worldPos,
                    Vector3.one * gridManager.CellSize * 0.92f);
            }
        }
    }

    private bool HasProvisionalPreview(ShipInstance ship)
    {
        foreach (ProvisionalMovementState state in gridManager.ProvisionalMoves)
        {
            if (state.Ship == ship)
            {
                return true;
            }
        }

        return false;
    }

    private void DrawProvisionalShips()
    {
        // TEMPORARY: Visualizes provisional movement before confirmation; never moves the ship.
        foreach (ProvisionalMovementState state in gridManager.ProvisionalMoves)
        {
            Gizmos.color = state.Ship.owner == PlayerId.PlayerA
                ? new Color(0f, 1f, 1f, 0.8f)
                : new Color(1f, 0.25f, 0.25f, 0.8f);

            foreach (Vector2Int cell in state.GetPreviewCells())
            {
                Color previewColor = state.Ship.owner == PlayerId.PlayerA
                    ? new Color(0f, 1f, 1f, 0.8f)
                    : new Color(1f, 0.25f, 0.25f, 0.8f);
                Vector3 worldPos = new Vector3(
                    cell.x * gridManager.CellSize,
                    cell.y * gridManager.CellSize,
                    0f);
                Gizmos.color = previewColor;
                Gizmos.DrawCube(
                    worldPos,
                    Vector3.one * gridManager.CellSize * 0.72f);
                Gizmos.color = Color.white;
                Gizmos.DrawWireCube(
                    worldPos,
                    Vector3.one * gridManager.CellSize * 0.82f);
            }
        }
    }

    private void DrawTerrain(Tile tile, Vector3 worldPos)
    {
        // TEMPORARY: Terrain Gizmos are inspection aids; terrain logic lives in Tile/GridManager.
        switch (tile.TerrainType)
        {
            case TerrainType.Costly:
                Gizmos.color = new Color(0.85f, 0.55f, 0.1f, 0.7f);
                Gizmos.DrawCube(worldPos, Vector3.one * gridManager.CellSize * 0.96f);
                break;
            case TerrainType.Impassable:
                Gizmos.color = new Color(0.25f, 0.25f, 0.25f, 0.95f);
                Gizmos.DrawCube(worldPos, Vector3.one * gridManager.CellSize * 0.96f);
                break;
            default:
                Gizmos.color = Color.gray;
                Gizmos.DrawWireCube(worldPos, Vector3.one * gridManager.CellSize * 0.95f);
                break;
        }
    }

    private void DrawStartingZones()
    {
        // TEMPORARY: Starting-zone overlay is for board inspection only.
        int zoneWidth = Mathf.Clamp(startingZoneWidth, 0, gridManager.width / 2);

        for (int x = 0; x < gridManager.width; x++)
        {
            bool isPlayerZone = x < zoneWidth;
            bool isEnemyZone = x >= gridManager.width - zoneWidth;

            if (!isPlayerZone && !isEnemyZone) continue;

            Gizmos.color = isPlayerZone ? playerZoneColor : enemyZoneColor;

            for (int y = 0; y < gridManager.height; y++)
            {
                Vector3 worldPos = new Vector3(x * gridManager.CellSize, y * gridManager.CellSize, 0f);
                Gizmos.DrawCube(worldPos, Vector3.one * gridManager.CellSize * 0.98f);
            }
        }
    }

    private void DrawDebugCones()
    {
        // TEMPORARY: Cone overlay is visualization only and does not resolve scans.
        if (gridManager.Match == null) return;
        Gizmos.color = new Color(1f, 0.9f, 0f, 0.6f);

        foreach (var ship in gridManager.Match.AllShips())
        {
            foreach (var layer in ship.visionLayers)
            {
                if (layer.isPassive || layer.shape != ShapeType.Cone) continue;

                VisionResolver.GetBowAndFacing(ship, out Vector2Int bow, out Vector2Int forward);
                foreach (var c in VisionResolver.GetConeCells(bow, forward, layer.range))
                {
                    Gizmos.DrawWireCube(new Vector3(c.x * gridManager.CellSize, c.y * gridManager.CellSize, 0f), Vector3.one * gridManager.CellSize * 0.9f);
                }
            }
        }
    }

    private void DrawDebugHalos()
    {
        // TEMPORARY: Halo overlay is visualization only and does not calculate fog state.
        if (gridManager.Match == null) return;
        Gizmos.color = new Color(0f, 0.8f, 1f, 1f);

        foreach (var ship in gridManager.Match.AllShips())
        {
            foreach (var layer in ship.visionLayers)
            {
                if (!layer.isPassive || layer.shape != ShapeType.Halo) continue;

                var sourceCells = ship.GetOccupiedCells();

                for (int x = 0; x < gridManager.width; x++)
                {
                    for (int y = 0; y < gridManager.height; y++)
                    {
                        Vector2Int cell = new Vector2Int(x, y);
                        bool inRange = false;
                        foreach (var src in sourceCells)
                        {
                            int dist = Mathf.Max(Mathf.Abs(cell.x - src.x), Mathf.Abs(cell.y - src.y));
                            if (dist <= layer.range) { inRange = true; break; }
                        }
                        if (inRange)
                        {
                            Gizmos.DrawWireCube(new Vector3(x * gridManager.CellSize, y * gridManager.CellSize, 0f), Vector3.one * gridManager.CellSize * 0.85f);
                        }
                    }
                }
            }
        }
    }
}