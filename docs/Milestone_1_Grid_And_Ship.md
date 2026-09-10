# Milestone 1 — Grid + Hardcoded Ship Placement

Goal from the master plan: **"Grid + place a hardcoded ship, see it occupy correct cells."**

This is the foundation everything else (movement, FoW, scanning, combat) builds on top of. Nothing here is player-facing yet — no input, no UI. The only success condition is: run the scene, a ship-shaped set of tiles lights up / logs correctly at the right grid coordinates.

---

## What "done" looks like

- A grid exists in the scene (data structure, optionally with visual debug tiles).
- One `ShipInstance` is placed at a hardcoded anchor + rotation via a hardcoded `ShipDefinition`-like footprint.
- The tiles the ship occupies are provably correct — either:
  - Debug-drawn in the Scene view (`Gizmos` / colored quads), or
  - Logged to Console (`Debug.Log`) listing exact `Vector2Int` cells, or both.
- If you nudge the anchor or rotation in the Inspector and re-run, the occupied cells change correctly.

---

## Relevant Unity APIs/features you'll use

| Need | Unity feature |
|---|---|
| Tile coordinates | `Vector2Int` (built-in struct, no need to write your own) |
| Grid container / manager | Plain `MonoBehaviour` attached to an empty GameObject in the scene |
| Storing tile lookup | `System.Collections.Generic.Dictionary<Vector2Int, Tile>` (plain C#, not Unity-specific) |
| Visualizing the grid without art | `OnDrawGizmos()` / `OnDrawGizmosSelected()` + `Gizmos.DrawWireCube` or `Gizmos.DrawCube` |
| Visualizing with actual sprites (optional, later) | `SpriteRenderer` on child GameObjects, or `Tilemap` + `Tilemap.SetTile` (Unity's built-in 2D tilemap package) |
| Converting grid coords to world position | Simple math: `new Vector3(pos.x * cellSize, pos.y * cellSize, 0)` — no special API needed |
| Hardcoding test data | Just C# fields / a `[SerializeField]` list set in the Inspector, or values set directly in `Start()` |
| Confirming correctness | `Debug.Log` / `Debug.Assert` in Console, and Gizmos in Scene view |
| Running/testing | Unity Editor Play Mode (no build needed for this milestone) |

You do **not** need Tilemap, NavMesh, physics, or any input system for this milestone. Keep it minimal — that's explicitly the point of the KISS/YAGNI note in the plan.

---

## Step-by-step build order

### Step 1 — Define `Tile` (plain data class)
Create `Assets/Scripts/Grid/Tile.cs`.
- Fields: `Vector2Int Position`, `ShipInstance Occupant` (nullable reference — `ShipInstance` doesn't exist yet, stub it or use `object`/placeholder for now if you want Tile working before Step 4).
- No MonoBehaviour needed — this is pure data, created and owned by `GridManager`.
- Optional: `bool IsValid` if you want non-rectangular boards later, default `true` for now.

**Check:** Compiles. No behavior to test yet.

---

### Step 2 — Define `GridManager` (MonoBehaviour)
Create `Assets/Scripts/Grid/GridManager.cs`.
- Fields: `int width`, `int height` (`[SerializeField]`, set in Inspector, e.g. 10x10).
- `Dictionary<Vector2Int, Tile> tiles` — built in `Awake()` or `Start()` by looping `x` in `0..width` and `y` in `0..height`, creating a `Tile` per coordinate.
- Methods:
  - `bool IsInBounds(Vector2Int pos)` → `pos.x >= 0 && pos.x < width && pos.y >= 0 && pos.y < height`
  - `bool IsOccupied(Vector2Int pos)` → look up tile, check `Occupant != null`
  - `Tile GetTile(Vector2Int pos)` → dictionary lookup, return null if missing
  - Leave `PlaceShip` / `RemoveShip` as stubs for now (real logic needs `ShipInstance`, coming in Step 4)

**Check:** Attach to an empty GameObject named `GridManager` in the scene. Enter Play Mode, confirm no errors, optionally `Debug.Log(tiles.Count)` to confirm it matches `width * height`.

---

### Step 3 — Visualize the grid (so you can actually see it)
In `GridManager`, add `OnDrawGizmos()`:
- Loop over all tiles, draw `Gizmos.DrawWireCube` at each tile's world position (`new Vector3(pos.x, pos.y, 0)` if cell size = 1).
- This runs in the Scene view even outside Play Mode — instant visual feedback with zero art needed.

**Check:** Open the Scene view, see a grid of wireframe squares matching `width x height`.

---

### Step 4 — Define footprint + rotation helper
Create `Assets/Scripts/Grid/FootprintUtil.cs` (static class).
- `List<Vector2Int> RotateOffsets(List<Vector2Int> offsets, int degrees)`:
  - 0°: return as-is
  - 90°: `(x, y) -> (-y, x)`
  - 180°: `(x, y) -> (-x, -y)`
  - 270°: `(x, y) -> (y, -x)`
- `List<Vector2Int> GetWorldCells(Vector2Int anchor, List<Vector2Int> offsets, int rotationDegrees)`:
  - Rotate offsets, then add `anchor` to each → returns final list of occupied `Vector2Int` cells.

**Check:** This is pure logic — good candidate for a quick manual test. In `Start()` of a temp test script, call it with a known footprint (e.g. `[(0,0),(1,0),(2,0)]`) and 90°, `Debug.Log` the result, verify by hand it matches expected rotated coordinates.

---

### Step 5 — Minimal `ShipInstance` (hardcoded stand-in for now)
Create `Assets/Scripts/Ships/ShipInstance.cs`.
- For this milestone, skip the full `ShipDefinition` ScriptableObject — just hardcode the footprint directly on `ShipInstance` or pass it in via a constructor/Inspector fields:
  - `Vector2Int anchor`
  - `int rotationDegrees`
  - `List<Vector2Int> footprintOffsets` (e.g. hardcode `[(0,0),(1,0),(2,0)]` for a 1x3 ship)
- Method: `List<Vector2Int> GetOccupiedCells()` → calls `FootprintUtil.GetWorldCells(anchor, footprintOffsets, rotationDegrees)`.

**Check:** Not tied to the grid yet — just confirm it returns the right list given hardcoded values.

---

### Step 6 — Implement real `PlaceShip` in `GridManager`
Back in `GridManager`:
- `void PlaceShip(ShipInstance ship, List<Vector2Int> cells)`:
  - For each cell, `GetTile(cell).Occupant = ship` (add bounds/occupied guard checks later — for this milestone, hardcoded data won't collide, so keep it simple).
- Add a `[SerializeField] ShipInstance testShip` or instantiate one directly in `GridManager.Start()` with hardcoded anchor/rotation, call `ship.GetOccupiedCells()`, then `PlaceShip(ship, cells)`.

**Check:** This is the actual milestone deliverable — see next step.

---

### Step 7 — Prove occupancy is correct
Two ways, do both:
1. **Debug.Log**: After placing, loop the returned cells and `Debug.Log($"Ship occupies {cell}")`. Manually verify against your hardcoded anchor + footprint + rotation.
2. **Gizmos**: In `GridManager.OnDrawGizmos()`, after drawing the base grid, loop tiles and if `tile.Occupant != null`, draw a filled `Gizmos.DrawCube` (different color) at that position.

**Check (the actual milestone pass condition):**
- Enter Play Mode.
- Scene view shows the grid with the ship's cells highlighted in a different color, in the exact shape/position/rotation you hardcoded.
- Change the hardcoded anchor or rotation value, re-enter Play Mode, confirm the highlighted cells move/rotate correctly.
- Console log matches what you see visually.

---

## Suggested file list for this milestone

```
Assets/Scripts/Grid/Tile.cs
Assets/Scripts/Grid/GridManager.cs
Assets/Scripts/Grid/FootprintUtil.cs
Assets/Scripts/Ships/ShipInstance.cs
```

Do **not** build yet in this milestone (per the master plan's Defer/ordering rules):
- `ShipDefinition` ScriptableObject (comes when you actually need data-driven ships — Step 7 of the master build order)
- Move/rotate input handling (Milestone 2)
- Collision/validation logic beyond what's trivially needed (`CanPlaceShip` becomes important in Milestone 2 when movement can conflict)
- Any UI, camera controls, or input system

---

## Next milestone preview
Once this is visually confirmed, Milestone 2 adds `CanPlaceShip` validation and lets you move/rotate the hardcoded ship (still no real input needed yet — can still be triggered by hardcoded key presses or Inspector buttons) to prove collision detection works before wiring in the turn/phase loop.
