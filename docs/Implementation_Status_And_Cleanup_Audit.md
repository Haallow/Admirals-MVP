# Admirals — Implementation Status and Cleanup Audit

**Audit basis:** Current `Assets/Scripts` source, checked-in Unity scene and
assets, then `AGENTS.md` and `docs/ARCHITECTURE.md` where consistent with
source. `docs/CHANGELOG.md` is historical context only. Statuses below
describe code and serialized configuration; they do not claim a Unity Play
mode test passed.

This is a handoff document for the local naval combat prototype. It separates
implemented mechanics from requirements, bugs, prototype surfaces, and
later integration debt. The intended command direction remains:

```text
Human input / AI / future LAN input
    -> shared gameplay operations
    -> authoritative match and board state
```

Deployment in the intended design is a **one-time pre-match step**. It is not
one of the five repeating battle phases.

## 1. Main status table

| System | Classification | Current evidence | Required work or limit |
| --- | --- | --- | --- |
| Grid and occupancy | IMPLEMENTED | `GridManager.tiles` stores `Tile.Occupant`; placement and mutation are in `GridManager`. | `CanPlaceShip` checks bounds and other ships, but not terrain passability or a deployment zone. |
| Terrain and routes | IMPLEMENTED / LEGACY DEBT | `MapDefinition`, three terrain types, and read-only eight-direction weighted Dijkstra routes exist. | Legacy `MoveShip` still uses Chebyshev distance and does not charge terrain cost. |
| Player A movement | IMPLEMENTED / PROTOTYPE | `PreviewMove`, provisional snapshots, full-footprint validation, and atomic confirmation; controller uses arrows, drag, Q/E, Escape/C, and Space. | Escape/C discard uncommitted previews only. A phase-advance guard rejects an invalid Move confirmation before Staging. |
| Pre-match deployment | PARTIALLY IMPLEMENTED / UNFINISHED | `DeploymentService` auto-places hardcoded rosters at hardcoded anchors using `CanPlaceShip`. | Player-controlled arrangement, authoritative zones, confirmation, and a pre-match lifecycle are absent. |
| Turn cycle | IMPLEMENTED | `Move -> Staging -> Search -> Battle -> End`; player switches only at End to Move; `PhaseChanged` drives reactions. | No game-over transition or pending-defense interruption. |
| Passive vision | IMPLEMENTED / KNOWN ISSUE | Per-player passive fog is recomputed at Search, at End, and on plane deploy/undeploy. | Intended always-current Absolute vision is not maintained after all relevant state changes. |
| Active scan | IMPLEMENTED / PROTOTYPE | Explicit Search scan preview and confirmation, one confirmed fleet scan per Search phase, active marks clear at End. | Input and preview use keys/Gizmos. Mine reveal uses detected ship cells rather than all scanned cells. |
| Combat attack | IMPLEMENTED | Battle/turn, one-attack, charge, domain, occupied-cell range, fog, and terrain LOS gates; d20, armor, ammo, death cleanup. | No defense choice/resolution; no structured combat result or event record. |
| Defensive Measures | UNFINISHED / REQUIRED | `DefenseProfile` tables, charge states, and `sideEffectId` data exist. | No eligibility/selection/roll/side-effect execution or pause before attack finalization. |
| Mines | IMPLEMENTED / LIMITS | Staging stern deployment, 600 flat damage for the SwordFish card, trigger on moved footprint, charge recharge at End. | Trigger code does not filter mine owner; active scan reveal is limited as noted above. |
| Recon planes | IMPLEMENTED / LIMITS | Carrier launch, Staging movement, passive Absolute halo, sortie refund for immediate undeploy, fuel expiry at End. | Launch rejects an Impassable destination although later movement crosses terrain; no plane combat. |
| Match / win condition | UNFINISHED / REQUIRED | `MatchState` owns players, live ships, mines, and planes; dead ships leave occupancy and live roster. | No victory rule, central evaluator, game-over state, or result transition. |
| End-phase / match statistics | UNFINISHED / REQUIRED | End clears active marks, resets attack flags, ticks ship recharge and owned plane fuel, removes expired planes, then recomputes passive fog. | No structured attack/hit/damage/defense/sinking statistics or turn summary. |
| Human input | PROTOTYPE / TEMPORARY | `TestShipController` polls legacy Unity `Input` for Player A. | Production UI/commands and defense/deployment interactions are absent. |
| AI | IMPLEMENTED / OUTSIDE CURRENT SCOPE | Player B is event-driven and uses fog-known enemies for target selection; movement execution uses `MoveShip`. | Known AI integration debt: movement semantics differ. Do not edit `Assets/Scripts/AI/` in current mechanics work. |
| Art, sprites, and tiles | REQUIRES VERIFICATION / NOT INTEGRATED | No image, sprite atlas, model, or prefab art files were found under this checkout's `Assets`; sample scene has `GridView`, not ship/tile renderers. | User-reported available art may be outside this checkout. Confirm its location in Unity/Inspector, then integrate as needed. |
| Board visualization | PROTOTYPE / TEMPORARY | `GridView.OnDrawGizmos` draws board, terrain, zones, ships, fog contacts, previews, cones, mines, and planes. | Keep useful diagnostics until equivalent rendering/feedback exists. |

## 2. Current implementation and required mechanics

### 2.1 Board, terrain, placement, and movement

`GridManager` builds a default 30 by 15 rectangular
`Dictionary<Vector2Int, Tile>`. In `SampleScene.unity`, it references
`Assets/Scripts/Grid/TestMap.asset`, which serializes 30 by 15 with an
empty terrain-entry list. The checked-in sample therefore uses Normal
terrain. `MapDefinition` can supply sparse Costly and Impassable cells;
unspecified cells stay Normal. Normal costs 1 to enter, Costly costs at
least 2, and Impassable is untraversable and blocks vision/attack LOS.
`Tile.IsValid` is reserved and has no current board-shape role.

`ShipInstance` is a plain serializable runtime object. Its anchor,
quarter-turn rotation, and footprint offsets derive occupied cells via
`FootprintUtil`. `GridManager.CanPlaceShip` rejects out-of-bounds cells
and cells occupied by another ship. It does not implement the previously
documented one-tile exclusion zone, nor check passable terrain. No
controller or view should become a second placement authority.

`GridPathfinder` computes read-only, eight-direction Dijkstra paths over
passable, unoccupied anchor cells, charging each entered cell's cost.
`MovementPathResult` reports reachability, route, cost, and budget fit.
`GridManager.PreviewMove` calculates from a `ProvisionalMovementState`
snapshot and separately checks the full candidate footprint, including
terrain and other provisional footprints. Invalid previews leave the last
valid candidate unchanged. `ConfirmProvisionalMovement` validates all
candidates, rejects overlapping destinations, commits ship occupancy
together, and then resolves mines. `CancelProvisionalMovement` clears all
previews. Player A uses this path.

`GridManager.MoveShip` remains for AI execution. It validates Chebyshev
distance from `anchorAtTurnStart` and calls `CanPlaceShip`, then mutates
occupancy and resolves mines. It does not use weighted routes, full-route
terrain cost, or provisional multi-ship confirmation. This distinction is
**known AI integration debt — outside the current mechanics-development
scope**. New movement APIs should be reusable by a later AI or LAN command
adapter, without requiring current AI implementation work.

During Move, Escape or C clears every provisional ship preview and stops an
active drag. Actual anchors and tile occupancy were never moved by preview,
so this restores the Move-start formation. Space confirms valid previews
before advancing; committed anchors are no longer cancelable in Staging.
`TurnManager.PhaseAdvanceRequested` lets `GridManager` reject leaving Move
when its read-only confirmation check fails. This also covers callers that
invoke `AdvancePhase` directly. On a successful direct transition, the
Staging phase handler performs the existing atomic confirmation.

### 2.2 Deployment: current setup and unfinished pre-match flow

`GridManager.Start` builds these requested rosters:

| Player | Cards in roster order | Auto-deployment |
| --- | --- | --- |
| A | Wolf, Athena, SwordFish, Carrier, Cruiser | X=1; Y starts at 1 and advances by 3 after each successful placement; rotation 0° |
| B | Wolf, Athena | X=`width - 3`; same Y rule; rotation 180° |

`DeploymentService.DeployAll` creates each ship via `ShipFactory`,
assigns owner and initial anchor, calls `CanPlaceShip`, writes occupancy,
and appends a valid ship to its owner's live list. An invalid candidate is
skipped with a warning. `PlayerState.fleetRoster` is requested card data;
`PlayerState.ships` is the live deployed set.

**UNFINISHED / REQUIRED — Player-Controlled Deployment:** Provide a
one-time pre-match state before the repeating Move cycle. The target human
flow is a simple text list of the player's ships near that side of the map:
select a ship name, place it in that player's legal zone, move/rotate it
while arranging, then confirm all legal placements to start battle.
`GridView.startingZoneWidth` currently controls only a blue/red Gizmo
overlay; it is **not authoritative deployment-zone data**. No grid or
match API currently defines a player's zone, checks a candidate against it,
or holds a pending formation before confirmation.

Keep zone definitions and legal placement queries in shared gameplay code,
with command operations for preview/place/rotate/confirm. Human UI should
only select and submit these commands. The same queries and commands should
be callable later by AI deployment and LAN input; implementing either
adapter is outside this audit. Deployment must remain outside the
`Move -> Staging -> Search -> Battle -> End` loop.

### 2.3 Turn phases and End lifecycle

`TurnManager.AdvancePhase` runs generic phase-advance guards, changes the
phase if none reject, switches player only on End to Move, logs the state,
then raises `PhaseChanged`. It has no
occupancy, fog, combat, mine, or plane implementation. The sample scene
references one serialized `TurnManager` from grid, human input, and AI;
another `TurnManager` component is also serialized on the grid GameObject
and is not referenced by those fields.

| Entered phase | Current `GridManager` reaction |
| --- | --- |
| Move | Clears provisional states; snapshots Player A's ships when A is acting. AI separately snapshots and moves B ships on its Move event. |
| Staging | For A, confirms any remaining valid provisional movement (normal Space input already committed it), then snapshots the acting player's plane positions. Mine/plane deployment is an explicit action. |
| Search | Clears acting player's plane `deployedThisTurn` flags, recomputes both passive fog grids, resets active scan preview/limit. |
| Battle | Resets live ships' per-phase attack flags. |
| End | Resets attack flags, clears active marks and scan state, ticks acting player's `ShipInstance.TickRecharge`, decrements owned plane fuel and removes planes at zero, then recomputes passive fog. |

Mine recharge is at **End**, not Staging. `TickRecharge` decrements
weapon and defense countdowns if set, but only mine charges call
`Recharge` at zero in the current implementation. Plane sorties do not
automatically recharge. The initial Move phase is a serialized initial
state; it is not entered by a startup `PhaseChanged` call.

**UNFINISHED / REQUIRED — Match / Win Condition:** There is no authoritative
victory evaluator, game-over state, or transition. The exact victory rule
is not established by checked-in source or the stated requirements and
must be confirmed. A central match-result evaluator should consume live
match state or structured outcomes after attacks, mines, and other causes
of destruction. Do not scatter win checks through controllers, views, or
each damage source, and do not put the rule inside `TurnManager`.

**UNFINISHED / REQUIRED — End-Phase / Match Statistics:** Current combat
and mine operations mutate state and log text; no structured event/result
records or counters were found for attacks requested/resolved, hits,
misses, damage dealt/received, defenses used, ships sunk, sink
attribution, weapon/effect, or mine damage/kills. Future combat/match
services should produce structured results with attacker, defender,
source/effect, and outcome information. End can finalize and report a turn
summary; a match-result system can use the same data for an end-game
summary, HUD/console log, LAN synchronization/debugging, or replay tools.
This is a requirement, not an existing End behavior.

### 2.4 Fog, passive vision, and active Search

`FogManager` owns two `FogGrid` instances. Each grid stores passive and
active cell-state dictionaries. `Unknown`, `Marked`, and `Identified`
are ordered strengths; `FogGrid.Upgrade` does not downgrade knowledge.
Both Marked and Identified pass the combat known-target gate.
`VisionResolver` owns stateless halo/cone geometry, domain filters,
surface-only restrictions, and supercover terrain LOS. It excludes
Impassable intermediate cells; Normal and Costly are transparent. Plane
Absolute halo vision ignores terrain LOS.

Passive ship layers are rebuilt on Search entry. They are also rebuilt
when a plane is deployed or undeployed, and at End after fuel changes.
Active scans are distinct Search actions: the player previews a cone and
confirms, or the AI requests a scan on its Search event. Confirmation
writes temporary Marked cells and uses one fleet scan for that Search
phase. End clears both active layers. Destroyed-ship cell marks are
cleared immediately through `FogManager.ClearMarksForShip`.

**KNOWN ISSUE / REQUIRED — Passive Absolute Vision Lifecycle:** The
intended rule is passive Absolute visibility at all times in Move,
Staging, Search, Battle, and End. Current passive fog is **not** refreshed
on every ship move/rotation, Wolf domain toggle, plane movement, or ship
destruction; initial deployment also does not cause a passive refresh.
The view simply reads the last stored fog state. Therefore an enemy
inside valid current Absolute vision can remain unidentified, or a
previously visible cell can remain stale, until another recompute.
The correction belongs at relevant authoritative state-change paths
with a shared fog refresh operation, preserving active scan as a
Search-only explicit action. Include source/target movement, rotation,
domain, deployment, plane lifecycle, and destruction in that lifecycle
review. Do not move geometry into `FogGrid` or make the view recompute
gameplay knowledge.

**KNOWN ISSUE — Mine reveal:** `FogManager.RunActiveSearch` compares
enemy mine positions with `VisionScanResult.DetectedCells`. That list
contains detected enemy **ship** cells, not all scanned cone cells. A
mine in an otherwise empty scanned cell is not marked by this path.
The intent to reveal mines with active sonar is not generally met.
Confirm desired mine-visibility rules before correcting the shared
scan operation.

### 2.5 Combat and unfinished Defensive Measures

`CombatResolver.ResolveAttack`, reached through `GridManager.Combat`,
is the authoritative attack path. It rejects a dead target, an attacker
who has already attacked, wrong phase/turn (when a `TurnManager` is
present), an unready weapon charge, incompatible target domain, no
occupied-cell pair in Chebyshev range, an unknown target, or all in-range
pairs blocked by Impassable terrain. It then marks the ship's attack
opportunity, rolls one d20 against the weapon's `RollTier` table, consumes
one finite ammo even on a miss, applies armor, subtracts health, and
removes a sunk ship from occupancy/live roster while clearing its marks.

```text
effectiveDamage = round(rawDamage * (1 - armor * 0.0015))
```

Zero damage stays zero; a nonzero result is clamped to at least one.
`ResolveAttack` returns true for a resolved miss, false for rejected
requests. `CanShipAttack` checks alive/phase/turn/attack flag, but not a
particular weapon or target.

`DefenseProfile` stores eligibility domain, limited or infinite uses,
saving-throw tiers, and an optional string `sideEffectId`.
`ShipInstance.InitializeCharges` creates defense `ChargeState` entries.
Wolf's Crash Dive uses `BecomeSubSurfaceAndSkipNextMove` as a string ID;
there is no side-effect execution layer, pending attack, defense selection
flow, defense roll, or defense consumption in `ResolveAttack`. These
profiles are data, not a completed mechanic.

**UNFINISHED / REQUIRED — Defensive Measures:** After an attack request
passes its opening validations, the defending side must be able to choose
an eligible, available defense before final damage/outcome is committed.
For the current mechanics-only prototype, a human adapter can use
number keys to select and Enter/Return to confirm. Shared combat
operations should (1) expose eligible defenses, (2) validate a submitted
choice and charge, (3) roll and resolve its result, (4) execute defined
side effects, (5) let that result affect the incoming attack, and
(6) finalize damage and structured outcome. The keyboard layer should
only submit the choice. The same mechanism must be callable later by AI
and LAN input. The exact timing of attack roll versus defense choice and
individual defense effects need explicit design confirmation before
implementation; the present complete-in-one-call `ResolveAttack` path
cannot ask for a choice between validation and finalization.

### 2.6 Mines and reconnaissance planes

`SwordFishClass` has a Contact Mine profile with two charges, 600 flat
damage, and recharge time 2. `GridManager.DeployMine` requires Staging,
the acting owner, a charge, and an in-bounds passable empty cell one step
behind the stern that contains no existing mine. `ResolveMinesFor`
triggers mines under a moved ship's footprint, removes them, combines
flat damage without armor/defense, and performs death cleanup. It does
not check whether a mine belongs to the moving ship. Mine charges tick
on the owner's End and restore one at countdown zero, capped at two.

`CarrierClass` has one Recon Plane sortie with launch range 4, movement
range 5, Absolute halo range 3, and fuel 3. `DeployPlane` validates
Staging, owner, charge, in-bounds and passable destination, and range
from any hull cell. It does not require the destination to be free of
ships. The plane lives in `MatchState.planes`, not tile occupancy;
deployment immediately recomputes passive fog. Planes cannot move on
their deployment turn. In a later Staging, `PreviewPlaneMove` checks
owner, phase, bounds, and Chebyshev distance from the Staging snapshot,
then immediately mutates plane position; it does not check terrain,
ships, or intermediate route. `ConfirmPlaneMove` only logs. A newly
deployed plane can be undeployed that Staging with a sortie refund.
Fuel decreases at the owning player's End; zero-fuel planes are removed.

The launch restriction and later terrain-independent movement should be
reviewed as a gameplay rule. Plane movement currently does not refresh
passive fog until a later lifecycle trigger, contributing to the
passive-vision issue.

### 2.7 Input, AI boundary, and rendering

`TestShipController` is the only Player A input path in the sample
scene. It polls Unity's legacy `Input`. Tab cycles ships and, during
Staging, owned planes; clicking a friendly unit selects it. Move uses
arrows/drag and Q/E previews, Escape/C clear all uncommitted ship previews,
D toggles an active Wolf's domain, and Space confirms then advances.
Enter is not a movement commit key. Staging uses M
for a mine, P then click for a plane, and C on a selected new plane
for undeploy/refund. Search uses S or clicking the selected ship to
preview a scan, Q/E or mouse aiming, Enter to confirm, Escape/C to
cancel, and Space to auto-confirm a pending preview before advancing.
Battle uses 1/2/3 to select a weapon and click to request an attack.
The controller reads `Tile.Occupant` on battle clicks; fog legality is
still enforced by `CombatResolver`. Space calls `TurnManager.AdvancePhase`
before the controller's Player A turn guard, so this adapter can advance
Player B's phases. This is a prototype input limitation.

Player B's `AIController` is event-driven from `PhaseChanged`. It builds
`AITurnContext`, uses fog-known enemies and separate sighting memory
for decisions, moves through legacy `MoveShip`, requests one active
scan, and requests attacks through `CombatResolver`. It has no AI
Staging mine/plane action. The actual class names are
`AIMovementPlanner` and `AIActiveScanPlanner` despite their filenames.
The scanner tries ships in roster order and confirms the first
successful scan; it does not globally compare all scanner ships.
AI implementation and AI-specific cleanup are **outside current scope**.
Mechanics work should expose reusable operations that a later AI
adapter can call.

`GridView` is read-only Gizmo visualization. It draws Player A ships
in cyan; enemy ship cubes are red when Identified, Marked cells use
light-orange contact cubes with gold wireframes, and Unknown enemy
ships are hidden unless reveal mode is active. Own mines use yellow
markers; enemy mines are otherwise hidden except for generic contact
markers if their cell is Marked. Own planes are green diamonds;
known enemy planes are magenta diamonds. Terrain, starting zones,
provisional footprints, and blocked/clear scan-cone cells also use
Gizmos. `DrawDebugHalos` and `DrawMovementRanges` are absent.
`GridView.startingZoneWidth` is presentation data only. The view
reads Player A fog, not the current player's fog.

## 3. Art and asset integration

**Checked-in asset inventory:** `Assets/Scripts/Grid/TestMap.asset`;
the sample and recovery/template scenes; render pipeline, volume,
renderer, and input settings assets. A recursive file inventory under
`Assets`, including ignored files, found no PNG/JPEG/PSD/SVG/TGA,
sprite atlas, FBX, or prefab art files. `Assets/Data` is empty.
The sample scene contains `GridView` but no ship/tile
`SpriteRenderer`, `Tilemap`, or gameplay Canvas references in the
inspected serialization. No runtime source references sprite/tile
renderers or asset-loading APIs. Thus ship sprites and tile art are
**not present or integrated in this checkout**.

The reported availability of ship sprites, tile art, and other art
assets may refer to files outside this repository or a different
working copy. Their location, import status, and appearance require
Unity/Inspector or external-asset verification; do not equate that
report with runtime integration. Generic Unity render pipeline
settings are present but are not gameplay art.

| Gizmo surface | Current purpose | Keep until |
| --- | --- | --- |
| Ship cubes and terrain tiles | Only checked-in board/occupancy presentation | Ship and tile rendering is integrated and verified |
| `DrawSensorContact` and fog culling | Distinguishes Marked, Identified, and Unknown during fog debugging | Player-facing contact/fog presentation exists |
| `DrawProvisionalShips` | Shows pending movement without mutating occupancy | A production movement preview exists |
| `DrawDebugCones` | Shows scan shape and terrain-blocked cells | A scan preview conveys those states |
| `DrawStartingZones` | Visual starting-side overlay, not deployment legality | Pre-match deployment UI and authoritative zones exist |
| `DrawMines` / `DrawPlanes` | Only current markers for these entities | Their production renderers exist |

Do not remove a Gizmo solely because an art file becomes available.
Keep diagnostic views where they remain the practical way to verify
unfinished mechanics; disable or label them as development-only after
replacement.

## 4. Focused cleanup and architecture health

### 4.1 Meaningful cleanup candidates

| Item | Finding and dependency | Direction |
| --- | --- | --- |
| `GridManager.TestShip` / `ObstructionShip` | First-ship compatibility properties still exist, but no current `Assets/Scripts` caller was found. The comment claiming AI uses them is stale. | Remove only after checking tests/serialized tooling that might rely on public accessors. |
| `GridManager.DebugRecomputeFog` | No such method or `[ContextMenu]` exists in current source. | Remove its old cleanup task; there is nothing to delete. |
| `MoveShip` | Live AI execution dependency. | Label/understand as LEGACY; retain until later AI integration changes. Do not edit AI during current mechanics work. |
| `TestShipController` | Sole human input adapter; keyboard and mouse interactions are prototype only. | Keep until all actions, including deployment/defense, have replacement adapters. A boundary comment can explain its temporary role. |
| `GridView` Gizmos | Only current gameplay visualization in this checkout and useful for verification. | Treat as DEBUG/prototype view; replace incrementally, preserving read-only behavior. |
| `FogManager`, combat, deployment, and phase logs | Console feedback currently substitutes for UI and structured results. `ShipInstance.LogStatBlock` is called by deployment. | Add structured outcomes before trimming logs; retain useful rejection and data-integrity diagnostics. |
| `Tile.IsValid` and `ShipInstance.movementRange = 3` | Reserved board flag is unused; card builders override the placeholder move value. | Low-priority readability cleanup, only when touching those areas. |
| `Assets/_Recovery` | Three recovery scenes exist; `EditorBuildSettings.asset` lists only `SampleScene.unity`. | Verify no manual recovery need before any deletion. |
| `Assets/Settings/InputSystem_Actions.inputactions` | Runtime controller uses legacy `Input`, but project settings reference an input-actions GUID. | Do not classify the asset as dead or delete without Inspector/package verification. |

Existing assertions and warnings have value: `GridManager.PlaceShip`
asserts in-bounds cells, `CombatResolver.RollWeapon` warns on an
unmatched tier, and provisional confirmation warns on failure.
Prefer concise boundary comments such as `PROTOTYPE`, `DEBUG`, or
`LEGACY` for non-obvious dependencies and limitations. Do not add
comments that merely restate a method's name.

### 4.2 Architecture health

**Generally aligned with shared mechanics:** `MatchState` owns live
entities, `GridManager` owns tile occupancy and board mutations,
`CombatResolver` owns final attack legality, `FogManager` owns per-player
knowledge lifecycle, `VisionResolver` owns detection/LOS geometry, and
`GridView` reads rather than mutates state. Human and AI callers already
request grid/combat actions. These boundaries can support later human,
AI, and LAN adapters without a whole-project rewrite.

**Specific boundary work required:**

1. Add a shared pre-match deployment-zone definition and query/command
   API; the current zone width is view-only.
2. Extend combat with a pending-attack/defense choice and finalization
   boundary, rather than implementing defense rolls in keyboard input.
3. Refresh passive fog on authoritative state changes so views and
   attack gating see current knowledge in every phase.
4. Produce structured combat/mine results and central match evaluation.
5. For future LAN use, introduce stable runtime entity/action IDs and
   serializable command/result data before sending commands over a
   network. Current APIs and AI memory use in-process
   `ShipInstance` references. This is a future networking prerequisite,
   not a request to implement networking now.

The current controller duplicates a small pre-attack flag check, while
`CombatResolver` remains the final authority. Avoid adding more
input-specific legality paths. `CanPlaceShip` is the common placement
check, but it needs deployment-zone policy for the intended pre-match
flow. The existing AI has some planning checks for hypothetical attacks;
its final requests still use `CombatResolver`.

## 5. Requires Unity runtime / Inspector verification

- Run the sample scene to check actual input sequencing, Gizmo visibility,
  scan rendering, and whether direct phase advances expose invalid
  provisional state. Static source inspection establishes the code paths,
  not their observed Play mode behavior.
- Inspect the duplicate serialized `TurnManager` component on the grid
  GameObject. The grid, input, and AI references point to the separate
  component, but a Play mode check is needed for any side effect.
- Locate the user-reported ship sprites, tile art, and other art if they
  are outside this checkout; verify Unity import settings and whether a
  different scene or branch integrates them.
- Confirm design rules for victory, mine ownership/visibility,
  defense timing and side effects, and plane launch terrain. Source
  reveals current behavior but does not settle intended rules.
- Check any external or Inspector-only use of public compatibility
  properties and the input-actions asset before cleanup.

## 6. Executive summary

### Implemented

The checked-in source has a 30 by 15 sample grid, authoritative ship
occupancy, hardcoded fleet auto-deployment, weighted provisional Player A
movement, five-phase turn events, per-player fog with passive and active
detection, terrain LOS, attack validation/d20/armor/ammo/death cleanup,
contact mines, reconnaissance planes, and a Player B AI command path.
End currently handles attack-flag reset, active-mark cleanup, acting
ship recharge, plane fuel/removal, and passive-fog recomputation.

### Unfinished / Required Next

- Defensive measure selection, eligibility, roll, side-effect execution,
  and attack finalization after a defender choice.
- Player-controlled, one-time pre-match deployment with authoritative
  legal zones and confirmation.
- Correct passive Absolute vision after relevant state changes in every
  phase, while keeping active scans specific to Search.
- An authoritative win-condition/game-over flow; the victory rule needs
  confirmation.
- Structured turn/match combat events and statistics, including damage,
  defenses, sink attribution, and mine outcomes.
- Player-facing input, fog, scan, movement, ship/tile, mine, and plane
  presentation. No production ship/tile art is integrated in this
  checkout; reported external art needs location/import verification.

### Known Debt — Outside Current Scope

AI movement execution still uses legacy distance-based `MoveShip`
semantics. AI-specific behavior, deployment, and movement migration are
outside the current mechanics-development scope; shared mechanics APIs
should be ready for a later AI adapter. Future LAN support will require
stable runtime identifiers and serializable commands/results.

### Cleanup / Handoff

Remove stale references to nonexistent `DebugRecomputeFog`; retain
`MoveShip`, prototype input, useful Gizmos, and diagnostic guards while
they have active dependencies. Review unused compatibility properties,
reserved fields, and logs incrementally. Keep new gameplay legality in
shared services and views read-only.

### Requires Runtime / Inspector Verification

Sample-scene input and Gizmo behavior, the duplicate `TurnManager`'s
effect, external art availability/import, Inspector-only dependencies,
and the unresolved victory/defense/mine/plane design rules need
verification beyond static source inspection.
