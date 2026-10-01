using System.Collections.Generic;
using UnityEngine;

// Extracted from GridManager. Pure visualization, reads GridManager's data,
// never mutates it. Owns the visual-only settings (zone colors/width) since
// nothing but drawing needs them.
//
// DrawDebugCones is no longer "debug" in the throwaway sense,
// per the roadmap it's becoming the real toggleable fog/scan visualization.
// Kept its original name for now; rename when the actual toggle UI lands.
public class GridView : MonoBehaviour
{
    [SerializeField] private GridManager gridManager;

    [Header("Starting Zones")]
    [SerializeField] private int startingZoneWidth = 5;
    [SerializeField] private Color playerZoneColor = new Color(0f, 0.35f, 1f, 0.18f);
    [SerializeField] private Color enemyZoneColor = new Color(1f, 0.1f, 0.1f, 0.18f);

    [Header("Fog of War Visuals (Temporary Prototype)")]
    // TEMPORARY: Scene-view inspection toggle for development. In production, fog culling
    // will be handled by a dedicated Fog of War camera/shader system rather than Gizmo filtering.
    [Tooltip("If true, all enemy ships and objects are drawn regardless of Fog of War for development/debugging.")]
    [SerializeField] private bool revealAllInFog = false;
    // TEMPORARY: Placeholder color for sensor contacts until production UI radar blip / sonar ping art lands.
    [Tooltip("Color used to mark a tile detected by Sensor vision (something is there, but unknown).")]
    [SerializeField] private Color sensorMarkedColor = new Color(1f, 0.65f, 0.2f, 0.85f);

    private void OnDrawGizmos()
    {
        if (gridManager == null) return;

        // TEMPORARY: Scene-view visualization only; does not affect gameplay state.
        DrawStartingZones();

        // TEMPORARY: Dev bypass — shows all objects in Edit mode or when the reveal toggle is enabled.
        bool showAll = revealAllInFog || !Application.isPlaying;
        FogGrid playerAFog = gridManager.Fog?.GetFogGrid(PlayerId.PlayerA);

        foreach (var kvp in gridManager.AllTiles)
        {
            Vector2Int pos = kvp.Key;
            Tile tile = kvp.Value;
            Vector3 worldPos = new Vector3(pos.x * gridManager.CellSize, pos.y * gridManager.CellSize, 0f);

            DrawTerrain(tile, worldPos);

            FogState fogState = playerAFog != null ? playerAFog.GetState(pos) : FogState.Unknown;

            // TEMPORARY FOG VISUAL: Sensor vision contact marker (light orange). Visually tells the player
            // that something is detected on this tile, but identity remains unknown.
            // In final production, replace this Gizmo with a radar blip, sonar ping VFX, or contact icon.
            if (fogState == FogState.Marked && (!showAll || tile.Occupant == null))
            {
                DrawSensorContact(worldPos);
            }

            if (tile.Occupant == null || tile.Occupant.currentHealth <= 0) continue;
            if (HasProvisionalPreview(tile.Occupant)) continue;

            if (tile.Occupant.owner == PlayerId.PlayerA)
            {
                // Friendly ships are always drawn in cyan
                Gizmos.color = Color.cyan;
                Gizmos.DrawCube(worldPos, Vector3.one * gridManager.CellSize * 0.72f);
            }
            else
            {
                // TEMPORARY FOG VISUAL: Enemy ship rendering gated by Fog of War.
                // In production, full 3D ship models will be shown/hidden via mesh renderers or shaders.
                if (showAll)
                {
                    // Dev mode: all enemy ships drawn in red regardless of fog.
                    Gizmos.color = Color.red;
                    Gizmos.DrawCube(worldPos, Vector3.one * gridManager.CellSize * 0.72f);
                }
                else if (fogState == FogState.Identified)
                {
                    // Absolute vision: draws identified enemy ship cells in red.
                    Gizmos.color = Color.red;
                    Gizmos.DrawCube(worldPos, Vector3.one * gridManager.CellSize * 0.72f);
                }
                // If Marked: DrawSensorContact was already drawn above; the ship itself is NOT drawn.
                // If Unknown: neither sensor contact nor ship cube is drawn (prevents "Red Cube Fog Trap").
            }
        }

        // TEMPORARY: Preview-only rendering; authoritative ship placement remains unchanged.
        DrawProvisionalShips();
        DrawDebugCones();
        DrawMines();
        DrawPlanes();
    }

    private bool HasProvisionalPreview(ShipInstance ship)
    {
        bool showAll = revealAllInFog || !Application.isPlaying;
        if (ship.owner != PlayerId.PlayerA && !showAll)
        {
            return false;
        }

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
        bool showAll = revealAllInFog || !Application.isPlaying;

        // TEMPORARY: Visualizes provisional movement before confirmation; never moves the ship.
        foreach (ProvisionalMovementState state in gridManager.ProvisionalMoves)
        {
            // In normal gameplay, never draw provisional previews for enemy ships —
            // prevents enemy ships from flashing in fog when they move.
            if (state.Ship.owner != PlayerId.PlayerA && !showAll)
            {
                continue;
            }

            Color previewColor = state.Ship.owner == PlayerId.PlayerA
                ? new Color(0f, 1f, 1f, 0.8f)
                : new Color(1f, 0.25f, 0.25f, 0.8f);

            foreach (Vector2Int cell in state.GetPreviewCells())
            {
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
        // TEMPORARY: Cone overlay is visualization only; LOS classification uses
        // the same VisionResolver helper as the authoritative scan.
        if (gridManager.ActiveScanPreview == null) return;

        ActiveScanPreviewState preview = gridManager.ActiveScanPreview;
        foreach (var c in preview.GetConeCells())
        {
            bool blocked = VisionResolver.TryGetFirstBlockingCell(
                preview.Bow,
                c,
                gridManager,
                out _);
            Gizmos.color = blocked
                ? new Color(1f, 0.15f, 0.05f, 0.6f)
                : new Color(1f, 0.9f, 0f, 0.6f);

            Gizmos.DrawWireCube(
                new Vector3(c.x * gridManager.CellSize, c.y * gridManager.CellSize, 0f),
                Vector3.one * gridManager.CellSize * 0.9f);
        }
    }

    // TEMPORARY FOG VISUAL: Prototype Gizmo marker for sensor contacts (light orange cube with wireframe).
    // In production, replace this with a proper 2D/3D radar blip, sonar ping VFX, or audio cue.
    private void DrawSensorContact(Vector3 worldPos)
    {
        // Light orange contact marker showing presence without revealing identity
        Gizmos.color = sensorMarkedColor;
        Gizmos.DrawCube(worldPos, Vector3.one * gridManager.CellSize * 0.72f);
        Gizmos.color = new Color(1f, 0.85f, 0.35f, 1f);
        Gizmos.DrawWireCube(worldPos, Vector3.one * gridManager.CellSize * 0.82f);
    }

    private void DrawMines()
    {
        // Draws all live mines from MatchState.
        // Own mines: solid yellow X wireframe — the deployer always sees their mines.
        // TEMPORARY FOG VISUAL: Enemy mines are hidden in fog unless revealAllInFog is enabled.
        // When pinged by an active sonar scan, the mine's cell is Marked in FogGrid and
        // rendered via DrawSensorContact. In production, this will use visibility shaders / UI layer.
        if (gridManager.Match == null) return;

        bool showAll = revealAllInFog || !Application.isPlaying;
        float size = gridManager.CellSize * 0.5f;

        foreach (MineTile mine in gridManager.Match.mines)
        {
            if (mine.owner != PlayerId.PlayerA && !showAll)
            {
                continue;
            }

            Vector3 center = new Vector3(
                mine.position.x * gridManager.CellSize,
                mine.position.y * gridManager.CellSize,
                0f);

            // Color by owner: yellow = PlayerA, orange = PlayerB.
            Gizmos.color = mine.owner == PlayerId.PlayerA
                ? new Color(1f, 0.95f, 0f, 1f)
                : new Color(1f, 0.55f, 0f, 1f);

            // Draw an X using two crossed wire cubes rotated 45 degrees,
            // approximated with two thin wire cubes along the diagonals.
            Gizmos.DrawWireCube(center, new Vector3(size, size * 0.15f, 0f));
            Gizmos.DrawWireCube(center, new Vector3(size * 0.15f, size, 0f));
        }
    }

    private void DrawPlanes()
    {
        // Draws all live planes from MatchState as diamond wireframe markers.
        // Distinct from the mine X marker — a diamond shape indicates airborne unit.
        // Color by owner: green = PlayerA, magenta = PlayerB.
        // TEMPORARY FOG VISUAL: Enemy planes stay hidden in fog unless revealAllInFog is enabled or detected.
        if (gridManager.Match == null) return;

        bool showAll = revealAllInFog || !Application.isPlaying;
        float size = gridManager.CellSize * 0.4f;
        FogGrid playerAFog = gridManager.Fog?.GetFogGrid(PlayerId.PlayerA);

        foreach (PlaneUnit plane in gridManager.Match.planes)
        {
            if (plane.owner != PlayerId.PlayerA && !showAll)
            {
                if (playerAFog == null || !playerAFog.IsKnown(plane.position))
                {
                    continue;
                }
            }

            Vector3 center = new Vector3(
                plane.position.x * gridManager.CellSize,
                plane.position.y * gridManager.CellSize,
                0f);

            // Color by owner.
            Gizmos.color = plane.owner == PlayerId.PlayerA
                ? new Color(0f, 1f, 0.4f, 1f)    // green
                : new Color(1f, 0.2f, 0.8f, 1f);  // magenta

            // Draw a diamond shape using four lines.
            Vector3 top    = center + new Vector3(0, size, 0);
            Vector3 right  = center + new Vector3(size, 0, 0);
            Vector3 bottom = center + new Vector3(0, -size, 0);
            Vector3 left   = center + new Vector3(-size, 0, 0);

            Gizmos.DrawLine(top, right);
            Gizmos.DrawLine(right, bottom);
            Gizmos.DrawLine(bottom, left);
            Gizmos.DrawLine(left, top);

            // Inner smaller diamond for visibility.
            float inner = size * 0.5f;
            Vector3 iTop    = center + new Vector3(0, inner, 0);
            Vector3 iRight  = center + new Vector3(inner, 0, 0);
            Vector3 iBottom = center + new Vector3(0, -inner, 0);
            Vector3 iLeft   = center + new Vector3(-inner, 0, 0);

            Gizmos.DrawLine(iTop, iRight);
            Gizmos.DrawLine(iRight, iBottom);
            Gizmos.DrawLine(iBottom, iLeft);
            Gizmos.DrawLine(iLeft, iTop);
        }
    }
}