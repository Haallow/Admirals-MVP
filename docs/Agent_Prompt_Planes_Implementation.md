# Admirals - Feature: Carrier Plane (Reconnaissance Unit)

You are working on the Unity project **Admirals**.

This is a Staging-phase deployment for the Carrier class. A plane is a
**board unit** that the Carrier places on a target cell during Staging. It
moves during the Movement phase like a ship, has absolute halo vision, and
disappears after a fixed number of turns (fuel). It is not a weapon and does
not attack anything in this implementation.

---

# 1. Read first

Before changing anything, read:

```text
docs/AGENTS.md
docs/DEVELOPER_GUIDE.md
docs/CHANGELOG.md
```

Then inspect:

```text
Assets/Scripts/Combat/MineProfile.cs       — reference pattern for a Staging profile
Assets/Scripts/Combat/MineTile.cs          — reference pattern for a non-ship board entity
Assets/Scripts/Ships/ShipInstance.cs       — planes/planeCharges pattern to follow
Assets/Scripts/Ships/ShipData.cs           — BuildCarrierClass to extend
Assets/Scripts/Grid/GridManager.cs         — DeployMine as the Staging action pattern;
                                             HandlePhaseChanged for the fuel tick location
Assets/Scripts/Grid/TestShipController.cs  — HandleStagingInput to extend
Assets/Scripts/FogOfWar/VisionResolver.cs  — how passive halo vision works
Assets/Scripts/FogOfWar/FogManager.cs      — RecomputePassive to extend for plane vision
```

The source code is authoritative. Follow KISS/YAGNI.

---

# 2. What a plane is in this version

A plane is a **movable board unit** placed by the Carrier during Staging.

| Property | Value |
|---|---|
| Deployed | Staging phase, player clicks a destination cell within launch range of the Carrier |
| Launch range | Chebyshev distance from any Carrier hull cell to the target cell (suggest 4) |
| Movement | Moves during Move phase, own movement range (suggest 5 — planes are fast) |
| Vision | Passive absolute halo, range TBD by design (suggest 3) |
| Fuel / lifetime | N turns after deployment (suggest 3), then removed |
| Domain | Air — does not interact with Surface/SubSurface weapon domains |
| Attacks | None in this version |
| Shootable | No in this version |
| Footprint | 1 cell |
| Count | 1 plane per Carrier per match (no recharge) |

**Skipped for now (future milestone):**
- Plane weapons (air-to-sea missiles)
- Shootable by ships (sea-to-air missiles)
- Air-to-air combat

---

# 3. Exact scope

Implement only:

1. `PlaneProfile.cs` — definition (count, vision range, fuel turns).
2. `PlaneUnit.cs` — live board unit (owner, position, fuel remaining,
   vision layer). Not a `MonoBehaviour`. Similar to `MineTile` but movable
   and persistent across turns.
3. `ShipInstance.planes` / `planeCharges` — charge tracking on the Carrier.
4. `MatchState.planes` — list of live `PlaneUnit` objects on the board.
5. `GridManager.DeployPlane(ship, targetCell)` — Staging action, places the
   plane on the board. Tile occupancy: planes do NOT block ship movement —
   they share the same cell as ships freely (they are at altitude).
6. `GridManager.HandlePhaseChanged` — fuel tick at End; remove planes at 0.
7. Provisional movement for planes: planes enter the Move phase preview
   system and can be moved by the owning player during Move, same as ships.
   Planes do NOT participate in the exclusion zone check — they are airborne.
8. `FogManager.RecomputeAllPassive` — include plane vision layers in the
   passive recompute pass, same as ships.
9. `GridView.DrawPlanes()` — Gizmo marker per live plane.
10. `TestShipController.HandleStagingInput` — `P` key enters targeting mode,
    next left-click places the plane on the clicked cell.
11. `TestShipController` Move phase — planes owned by the current player are
    included in the Tab cycle and can be moved like ships.

Do NOT implement:

```text
plane weapons
plane health / being shot down
sea-to-air or air-to-air missiles
plane recharge after the sortie is spent
```

---

# 4. Data — `PlaneProfile.cs`

New plain class, `Assets/Scripts/Combat/PlaneProfile.cs`.

```csharp
[Serializable]
public class PlaneProfile
{
    public string id;
    public int? count;        // 1 for Carrier
    public int launchRange;   // max Chebyshev distance from Carrier hull to deploy cell (suggest 4)
    public int movementRange; // cells the plane can move per Move phase (suggest 5)
    public int visionRange;   // passive absolute halo range (suggest 3)
    public int fuelTurns;     // turns the plane stays on the board (suggest 3)

    public PlaneProfile(string id, int? count, int launchRange, int movementRange,
        int visionRange, int fuelTurns) { ... }
}
```

---

# 5. Board entity — `PlaneUnit.cs`

New plain class, `Assets/Scripts/Combat/PlaneUnit.cs`.

```csharp
public class PlaneUnit
{
    public PlayerId owner;
    public Vector2Int position;
    public int fuelRemaining;     // decrements each End phase; removed at 0
    public int movementRange;     // copied from PlaneProfile at deploy time
    public VisionLayer visionLayer; // absolute halo, built at deploy time

    public PlaneUnit(PlayerId owner, Vector2Int position, int fuelTurns,
        int movementRange, int visionRange)
    {
        this.owner         = owner;
        this.position      = position;
        this.fuelRemaining = fuelTurns;
        this.movementRange = movementRange;
        // Build the passive absolute halo layer at deploy time.
        this.visionLayer = new VisionLayer(
            id:                "Plane Recon",
            shape:             ShapeType.Halo,
            range:             visionRange,
            detects:           DomainType.Both,
            visionType:        VisionType.Absolute,
            isPassive:         true,
            onlyWhileSurfaced: false
        );
    }

    // Returns the single occupied cell for movement and vision calculations.
    public List<Vector2Int> GetOccupiedCells()
    {
        return new List<Vector2Int> { position };
    }
}
```

---

# 6. `ShipInstance` changes

Add alongside mines:

```csharp
public List<PlaneProfile> planes       = new List<PlaneProfile>();
public List<ChargeState>  planeCharges = new List<ChargeState>();
```

Extend `InitializeCharges` — populate `planeCharges` from `planes`, `count`
as `starting` and `maxCapacity`, same pattern as `mineCharges`.

Do NOT add `planeCharges` to `TickRecharge` — planes have no recharge.

Extend `LogStatBlock` — add `[PLANE]` log per profile.

---

# 7. `MatchState` changes

Add alongside mines:

```csharp
public List<PlaneUnit> planes = new List<PlaneUnit>();
```

---

# 8. `ShipData.BuildCarrierClass`

Add one `PlaneProfile` after vision layers:

```csharp
ship.planes = new List<PlaneProfile>
{
    new PlaneProfile(
        id:            "Recon Plane",
        count:         1,
        launchRange:   4,     // must deploy within 4 cells of any Carrier hull cell
        movementRange: 5,     // can fly up to 5 cells per Move phase
        visionRange:   3,
        fuelTurns:     3
    )
};
```

---

# 9. `GridManager` changes

### Deploy — `GridManager.DeployPlane(ship, targetCell)`

Public method, called during Staging.

1. Phase must be Staging.
2. `ship.owner` must equal `turnManager.CurrentPlayer`.
3. Find plane charge with `remaining > 0` (same check as `DeployMine`).
4. Validate `targetCell`: in bounds; not Impassable terrain.
5. **Launch range check:** minimum Chebyshev distance from any of `ship.GetOccupiedCells()`
   to `targetCell` must be ≤ `profile.launchRange`. Use hull-cell distance,
   not anchor-only — consistent with weapon range and mine stern checks.
   Log reason and return false if out of range.
6. Planes do **not** check for ship occupancy — they fly over ships.
7. Create `PlaneUnit(ship.owner, targetCell, profile.fuelTurns, profile.movementRange, profile.visionRange)`.
8. Add to `match.planes`.
9. Decrement `planeCharge.remaining`.
10. Log deployment including the launch cell and range used.

### Plane occupancy in the tile dictionary

Planes do **not** use `Tile.Occupant`. They are not ships. Their position is
stored only in `PlaneUnit.position`. This means:
- Planes do not block ship movement or placement.
- The exclusion zone check does not apply to planes.
- Vision resolves from `PlaneUnit.position` directly.

### Movement — planes enter the provisional system

When `HandlePhaseChanged(Move)` snapshots provisional states for the acting
player's ships, also create a `ProvisionalMovementState` for each of that
player's live `PlaneUnit` objects — but using a wrapper.

**Simpler alternative (KISS):** Give `PlaneUnit` its own `anchorAtTurnStart`
and `movementRange` field (suggest range 4 — planes are fast). During Move,
the player can Tab to a plane the same way they Tab to ships. Arrow keys /
drag preview the plane's move using `gridManager.PreviewMove` directly — but
`PreviewMove` currently requires a `ShipInstance`. 

Two approaches:

**Option A (recommended — less change):** Treat plane movement separately.
During Move, planes are moved via a dedicated `PreviewPlanMove` /
`ConfirmPlanMove` path that updates `PlaneUnit.position` directly without
using the `ProvisionalMovementState` / `Tile.Occupant` system. The plane
movement is committed by pressing Space like ship movement.

**Option B:** Wrap `PlaneUnit` in a minimal `ShipInstance` shell for the
duration of movement only. Not recommended — leaks plane concerns into the
ship data model.

Use Option A. It is the smallest change.

### Fuel tick — `HandlePhaseChanged(End)`

After `TickRecharge`, iterate `match.planes`:
- Decrement `plane.fuelRemaining` by 1 for the acting player's planes only
  (same per-player rule as `TickRecharge`).
- If `fuelRemaining <= 0`: remove from `match.planes`, log removal.

### Passive vision — `FogManager.RecomputeAllPassive`

Currently iterates `match.GetPlayer(owner).ships` for passive layers.
After the ships loop, add a loop over `match.planes` owned by the same player:

```text
for each PlaneUnit plane owned by `owner`:
    treat plane.visionLayer exactly like a ship's passive VisionLayer
    use plane.GetOccupiedCells() as the source
    run VisionResolver.GetScanResult with no gridManager (no LOS blocking
    for planes — they see over terrain)
    write detected cells to fog with MarkPassive
```

Planes see over terrain — pass `null` as the `gridManager` parameter to
`VisionResolver.GetScanResult` so LOS blocking is skipped. This is the
existing overload behavior documented in the codebase.

---

# 10. `GridView` changes

Add `DrawPlanes()` called from `OnDrawGizmos`.

Draw each `PlaneUnit` in `gridManager.Match.planes` as a distinct marker —
suggest a small diamond wireframe or a cross, different from the mine X marker.
Color by owner. Show fuel remaining as part of the Gizmo label if possible,
or just the marker.

---

# 11. `TestShipController` changes

### Staging input

Extend `HandleStagingInput`:

```csharp
// P key — enter plane placement targeting mode.
// Next left-click places the plane on the clicked cell.
if (Input.GetKeyDown(KeyCode.P))
{
    planePlacementPending = true;
    Debug.Log("Plane placement mode: click a cell to deploy.");
}

if (planePlacementPending && Input.GetMouseButtonDown(0))
{
    planePlacementPending = false;
    Vector2Int cell = GetMouseGridCell();
    if (gridManager.DeployPlane(ship, cell))
        Debug.Log($"Plane deployed to {cell}.");
    // DeployPlane logs its own rejection reason.
}
```

Add `private bool planePlacementPending`. Clear on phase change.

### Move phase input

During Move, Tab cycles through the current player's ships. Extend the Tab
cycle to also include that player's live `PlaneUnit` objects. The selected
item is either a `ShipInstance` or a `PlaneUnit`. Arrow keys move the
selected plane via the dedicated plane movement path.

**Simplest approach:** add a `currentPlaneIndex` and a separate `isControllingPlane`
bool. When Tab cycles past the last ship, it switches to controlling planes.
Arrow key input during plane control calls `GridManager.PreviewPlaneMove` /
commits via Space like ships.

If this adds too much complexity to `TestShipController`, defer plane movement
input and make planes stationary for the first pass. Deployment works, vision
works, fuel ticks work — movement is added in the next iteration.

---

# 12. AI changes

No AI plane deployment in this version. The AI has no targeting logic for
choosing a reconnaissance cell. Skip entirely — the plane system works for
the human player. Note as future work.

---

# 13. Build order

**Step 1 — Data layer**
`PlaneProfile.cs`, `PlaneUnit.cs`. `ShipInstance.planes`/`planeCharges`,
`InitializeCharges`, `LogStatBlock`. `MatchState.planes`. `BuildCarrierClass`.
Verify: Carrier deploy log shows `[PLANE] Recon Plane | Remaining: 1`.

**Step 2 — Deploy action**
`GridManager.DeployPlane`. `P` key + click in `TestShipController`.
Verify: pressing `P` then clicking a valid cell during Staging logs the deploy
and the plane appears in `match.planes`. Clicking Impassable terrain is
rejected with a log.

**Step 3 — Vision**
Extend `FogManager.RecomputeAllPassive` to include plane vision layers.
Verify: entering Search after deploying a plane causes the plane's halo to
mark enemy cells in the fog log. Vision range matches `visionRange: 3`.

**Step 4 — Fuel tick**
`HandlePhaseChanged(End)` fuel tick, removal at 0.
Verify: plane disappears from `match.planes` after 3 of the owning player's
End phases. Log shows fuel countdown and removal message.

**Step 5 — Gizmo**
`GridView.DrawPlanes()`.
Verify: plane marker appears at deploy cell and disappears on removal.

**Step 6 — Movement (optional, can defer)**
Provisional plane movement via `PreviewPlaneMove` / Tab cycle extension.
Verify: plane can be repositioned during Move phase. Fuel still ticks
correctly after movement.

---

# 14. Verification

## Test 1: Carrier starts with 1 sortie
Deploy log shows `Recon Plane | Remaining: 1`.

## Test 2: Deploy on valid cell
Press `P`, click a passable cell in Staging. Plane appears in `match.planes`
at the correct position. Sortie count drops to 0.

## Test 3: Deploy on Impassable rejected
Press `P`, click an Impassable tile. Rejected with log. Sortie count unchanged.

## Test 3b: Deploy out of launch range rejected
Press `P`, click a cell more than 4 tiles from any Carrier hull cell.
Rejected with "out of launch range" log. Sortie count unchanged.

## Test 3c: Deploy at edge of launch range accepted
Click a cell exactly 4 tiles from the nearest Carrier hull cell.
Plane deploys successfully.

## Test 4: Second deploy rejected
After deploying, press `P` again. Rejected with "no sorties remaining".

## Test 5: Plane vision runs at Search
Advance to Search. Confirm `[VISION][PASSIVE][HALO][ABSOLUTE]` log entries
appear for the plane's position. Enemy cells within range 3 are marked.

## Test 6: Vision sees over terrain
Place an Impassable tile between the plane and an enemy. Confirm the enemy
is still detected — planes bypass terrain LOS.

## Test 7: Fuel ticks correctly
Deploy a plane. Advance through 3 of the owning player's End phases. Confirm
fuel logs: 3 → 2 → 1 → 0 → removal log. Plane gone from `match.planes`.

## Test 8: Gizmo matches state
Plane marker visible after deploy. Gone after fuel expires.

## Test 9: Existing systems unaffected
Ship movement, mines, fog for ships, and combat all behave identically to
before this feature.

---

# 15. File list

| Action | File |
|---|---|
| New | `Assets/Scripts/Combat/PlaneProfile.cs` |
| New | `Assets/Scripts/Combat/PlaneUnit.cs` |
| Modify | `Assets/Scripts/Ships/ShipInstance.cs` |
| Modify | `Assets/Scripts/Ships/ShipData.cs` |
| Modify | `Assets/Scripts/Match/MatchState.cs` |
| Modify | `Assets/Scripts/Grid/GridManager.cs` |
| Modify | `Assets/Scripts/FogOfWar/FogManager.cs` |
| Modify | `Assets/Scripts/Grid/GridView.cs` |
| Modify | `Assets/Scripts/Grid/TestShipController.cs` |

`AIController` is not modified. No AI plane deployment in this version.

---

# 16. Known debt

- Plane movement input in `TestShipController` may be deferred to a follow-up
  if Tab cycle extension adds too much complexity. Stationary planes still
  provide vision value.
- No AI plane deployment. Future AI planner needed.
- Plane weapons, shootability, and air combat are explicitly out of scope and
  tracked separately.

---

# 17. After implementation

Add a `CHANGELOG.md` entry. Do not update `AGENTS.md` or `DEVELOPER_GUIDE.md`
— those get one combined update at end of project.
