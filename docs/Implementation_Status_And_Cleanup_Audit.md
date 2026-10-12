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

Deployment is a **one-time pre-match step**. It is not
one of the five repeating battle phases.

## 1. Main status table

| System | Classification | Current evidence | Required work or limit |
| --- | --- | --- | --- |
| Grid and occupancy | IMPLEMENTED | `GridManager.tiles` stores `Tile.Occupant`; `CanPlaceShip` checks full-footprint bounds, passable terrain, and live occupancy. | Assigned deployment zones and draft collisions are checked by `DeploymentService` through the shared grid placement query. |
| Terrain and routes | IMPLEMENTED / LEGACY DEBT | `MapDefinition`, three terrain types, and read-only eight-direction weighted Dijkstra routes exist. | Legacy `MoveShip` still uses Chebyshev distance and does not charge terrain cost. |
| Player A movement | IMPLEMENTED / PROTOTYPE | `PreviewMove`, provisional snapshots, full-footprint validation, and atomic confirmation; controller uses arrows, drag, Q/E, Escape/C, and Space. | Escape/C discard uncommitted previews only. A phase-advance guard rejects an invalid Move confirmation before Staging. |
| Pre-match deployment | IMPLEMENTED / PROTOTYPE | `TestMap.asset` authors neutral west/east zones. `DeploymentService` stores private drafts by player and roster slot; Player A arranges and confirms, while a deterministic local routine drafts and confirms B through the same legality operations. Both formations commit atomically before `StartMatch`. | Prototype text/Gizmo UI; LAN transport and AI deployment strategy are future work. |
| Turn cycle | IMPLEMENTED | `Move -> Staging -> Search -> Battle -> End`; player switches only at End to Move; `PhaseChanged` drives reactions. Pending defense and completed match results block advancement. | End still waits for Space. |
| Passive vision | IMPLEMENTED | `GridManager.RefreshPassiveVision` rebuilds both players' passive fog after deployment, committed ship/plane movement, domain changes, plane removal, and ship destruction; Search and End also refresh it. Wolf Absolute Vision works in both domains. | Previews do not affect fog. Interactive Play mode verification remains outstanding. |
| Active scan | IMPLEMENTED / PROTOTYPE | Explicit Search scan preview and confirmation, one confirmed fleet scan per Search phase, active marks clear at End. | Input and preview use keys/Gizmos. Mine reveal uses detected ship cells rather than all scanned cells. |
| Combat attack | IMPLEMENTED | Battle/turn, one-attack, charge, domain, occupied-cell range, fog, and terrain LOS gates; d20, armor, ammo, death cleanup. `RequestAttack` / `SubmitDefense` return status and outcome; finalization emits an event. | Player B defense strategy remains automatic pass. |
| Defensive Measures | IMPLEMENTED / PROTOTYPE | Ready domain-matching choices, saving throw, charge use, avoidance, and Crash Dive next-Move lock are resolved centrally. | Player A uses a keyboard prompt; AI defense strategy and LAN adapter remain future work. |
| Mines | IMPLEMENTED / LIMITS | Staging stern deployment, 600 flat damage for the SwordFish card, trigger on moved footprint, charge recharge at End. | Trigger code does not filter mine owner; active scan reveal is limited as noted above. |
| Recon planes | IMPLEMENTED / LIMITS | Carrier launch, separate Staging movement candidate, confirmation/cancel, passive Absolute halo, sortie refund for immediate undeploy, fuel expiry at End. | Launch rejects an Impassable destination although later movement crosses terrain; no plane combat. |
| Match / win condition | IMPLEMENTED / PROTOTYPE | After combat death or a complete mine movement commit, loss of all live ships produces a winner or simultaneous-loss draw in `MatchState`; shared actions and phase advancement stop. | Fleet elimination is the only victory rule; there is no production game-over screen. |
| End-phase / match statistics | IMPLEMENTED / PROTOTYPE | End clears active marks, ticks recharge and plane fuel, refreshes fog, then stores and logs a structured turn summary. Immediate victory stores a partial summary without End ticks. | Console presentation only; detailed event history and match analytics remain future work. |
| Human input | PROTOTYPE / TEMPORARY | `TestShipController` polls legacy Unity `Input` for Player A, including deployment and defense prompts. | Production UI/commands remain absent. |
| AI | IMPLEMENTED / PROTOTYPE | Player B is event-driven and uses fog-known enemies for target selection; movement execution uses `MoveShip`. Its Battle sequence pauses and resumes around Player A defense responses. | AI defense selection is not implemented; movement semantics still differ. |
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
`FootprintUtil`. `GridManager.CanPlaceShip` rejects out-of-bounds,
Impassable, and other-ship occupied footprint cells. It does not implement
the previously documented one-tile exclusion zone. No
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

### 2.2 Deployment: current pre-match flow

`GridManager.Start` builds these requested rosters:

| Player | Cards in roster order | Setup |
| --- | --- | --- |
| A | Wolf, Athena, SwordFish, Carrier, Cruiser | Player arranges each slot in the assigned west zone with the text list, board click/drag, and Q/E rotation. |
| B | Wolf, Athena | Local deterministic routine searches legal candidates in the assigned east zone and confirms the full formation. |

`TestMap.asset` defines nonoverlapping full-height five-column `west` and
`east` zones. `MapDefinition` holds neutral zone IDs and rectangular regions;
match setup validates zone geometry and binds each player to one distinct
authored zone. `DeploymentService.TryPlace` and `CanPlace` address player,
zone ID, roster slot, anchor, and rotation. They use `GridManager.CanPlaceShip`
for the full footprint, then check the assigned zone and other drafts.
Invalid requests retain the last legal draft. Drafts do not occupy tiles or
enter `PlayerState.ships`; opposing drafts remain private until completion.

`DeploymentService.CanConfirm` requires every roster slot to be legal. Both
players must confirm; it revalidates both formations before writing any ship
to occupancy or the live lists. `GridManager.ConfirmDeployment` then refreshes
passive vision and starts exactly one Player A Move. Phase advancement is
blocked beforehand. The deterministic Player B routine lives in
`DeploymentService` and calls the same legality query and placement command;
it is local setup, not AI strategy. LAN transport is not implemented.

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
| Staging | For A, confirms any remaining valid provisional movement (normal Space input already committed it), then snapshots the acting player's plane positions. Mine/plane deployment is explicit; the controller commits owned plane previews before advancing from Staging. |
| Search | Clears acting player's plane `deployedThisTurn` flags, recomputes both passive fog grids, resets active scan preview/limit. |
| Battle | Resets live ships' per-phase attack flags. |
| End | Resets attack flags, clears active marks and scan state, ticks acting player's recharge and plane fuel, removes exhausted planes, refreshes passive fog, then stores and logs one turn summary. Space advances to the next Move. |

Mine recharge is at **End**, not Staging. `TickRecharge` decrements
weapon and defense countdowns if set, but only mine charges call
`Recharge` at zero in the current implementation. Plane sorties do not
automatically recharge. The initial Move phase is a serialized initial
state until both deployments commit; `TurnManager.StartMatch` then emits
`PhaseChanged(Move)` once.

`MatchState.Result` records a winner after the opponent loses every live
ship, or a draw if both fleets are lost in one atomic mine commit. Combat
checks after finalization and before `AttackFinalized` listeners run;
movement checks after all triggered mines resolve. Terminal play blocks
shared commands and phase advancement. It snapshots a partial final turn
without applying End lifecycle ticks.

`MatchState.TurnSummaries` contains completed snapshots. Finalized attacks
contribute hit/miss, damage, defense, and sink counts; mine triggers record
damage and losses; End adds mine-charge restoration and plane expiry, then
logs the summary. Rejected or pending attacks do not count. Full event
history, production presentation, and additional victory rules remain
future work.

### 2.4 Fog, passive vision, and active Search

`FogManager` owns two `FogGrid` instances. Each grid stores passive and
active cell-state dictionaries. `Unknown`, `Marked`, and `Identified`
are ordered strengths; `FogGrid.Upgrade` does not downgrade knowledge.
Both Marked and Identified pass the combat known-target gate.
`VisionResolver` owns stateless halo/cone geometry, domain filters,
surface-only restrictions, and supercover terrain LOS. It excludes
Impassable intermediate cells; Normal and Costly are transparent. Plane
Absolute halo vision ignores terrain LOS.

`GridManager.RefreshPassiveVision` asks `FogManager` to rebuild both
players' passive layers immediately after the atomic deployment commit,
confirmed provisional ship movement and rotation (after mine resolution),
legacy `MoveShip`, a Wolf domain toggle or successful Crash Dive, plane
deployment/confirmed movement/undeployment/fuel expiry, and combat or mine
destruction. Search and End also refresh passive fog. The Wolf's passive
Absolute layer detects both target domains while its source is surfaced
or submerged. Other authored sensor restrictions still apply.

Active scans are distinct Search actions: the player previews a cone and
confirms, or the AI requests a scan on its Search event. Confirmation
writes temporary Marked cells and uses one fleet scan for that Search
phase. End clears both active layers. Destroyed-ship cell marks are
cleared immediately through `FogManager.ClearMarksForShip`. A passive
refresh resets only passive knowledge; active marks persist until End.
Ship and plane previews do not change live positions or fog. `GridView`
only renders current state and preview markers.

**KNOWN ISSUE — Mine reveal:** `FogManager.RunActiveSearch` compares
enemy mine positions with `VisionScanResult.DetectedCells`. That list
contains detected enemy **ship** cells, not all scanned cone cells. A
mine in an otherwise empty scanned cell is not marked by this path.
The intent to reveal mines with active sonar is not generally met.
Confirm desired mine-visibility rules before correcting the shared
scan operation.

### 2.5 Combat and Defensive Measures

`CombatResolver.RequestAttack`, reached through `GridManager.Combat`, is
the authoritative attack path. It rejects a dead or foreign target, an
attacker who already attacked, wrong phase/turn, an unready weapon,
incompatible target domain, no occupied-cell pair in Chebyshev range,
unknown targets, and shots blocked by Impassable terrain. One pending
attack is allowed at a time, and phase advancement waits for its response.

Ready defenses matching the defender's current domain are offered after
those checks. A `Both` defense is available in either domain. Player A
uses number keys, 0 to pass, and Enter to submit; the prompt includes
only its target and available defense IDs. Player B passes automatically
in the local controller. A chosen defense spends a use and rolls first.
"Hit Avoided" skips the weapon roll and damage; "Fail" continues to the
weapon roll. Successful Crash Dive submerges the Wolf and blocks movement
and rotation during its next Move phase, while domain toggling remains
available. Mines do not use this defense flow.

Every finalized attack spends the attacker's opportunity and finite ammo
once, including a miss or successful defense. Otherwise the resolver
applies armor, subtracts health, and removes a sunk ship from occupancy
and the live roster while clearing its fog marks. `AttackFinalized` reports
the structured outcome and resumes Player B's remaining attacks.

```text
effectiveDamage = round(rawDamage * (1 - armor * 0.0015))
```

Zero damage stays zero; a nonzero result is clamped to at least one.
`RequestAttack` returns rejected, pending, or finalized. `SubmitDefense`
rejects invalid choices without changing the pending attack and returns
an outcome on success. `CanShipAttack` is a broad alive/phase/turn/attack
flag query; it does not validate a particular weapon or target.

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
then stores a candidate separately from `PlaneUnit.position`; it does not
check terrain, ships, or intermediate route. Invalid previews keep the last
valid candidate. `ConfirmPlaneMove` rechecks and commits a valid candidate,
then refreshes passive fog; cancel discards it. A newly
deployed plane can be undeployed that Staging with a sortie refund.
Fuel decreases at the owning player's End; zero-fuel planes are removed.

The launch restriction and later terrain-independent movement should be
reviewed as a gameplay rule. `GridView` and Player A selection show the
candidate position, while vision uses the committed position.

### 2.7 Input, AI boundary, and rendering

`TestShipController` is the only Player A input path in the sample
scene. It polls Unity's legacy `Input`. Tab cycles ships and, during
Staging, owned planes; clicking a friendly unit selects it. Move uses
arrows/drag and Q/E previews, Escape/C clear all uncommitted ship previews,
D toggles an active Wolf's domain, and Space confirms then advances.
Enter is not a movement commit key. Staging uses M
for a mine, P then click for a plane, and C on a selected new plane
for undeploy/refund or on an older plane to cancel its movement preview.
Search uses S or clicking the selected ship to
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
known enemy planes are magenta diamonds. Terrain, authored zones,
provisional footprints, and blocked/clear scan-cone cells also use
Gizmos. `DrawDebugHalos` and `DrawMovementRanges` are absent.
`GridView` reads zone geometry and visible drafts from deployment state. The view
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

**Specific boundary work still required:**

1. Extend the current turn aggregates into detailed event history if replay,
   analytics, or production match summaries require it.
2. For future LAN use, introduce stable runtime entity/action IDs and
   serializable command/result data before sending commands over a
   network. Deployment requests already use player, zone ID, and roster
   slot; movement and combat APIs and AI memory still use in-process
   `ShipInstance` references. This is a future networking prerequisite,
   not a request to implement networking now.

The current controller duplicates a small pre-attack flag check, while
`CombatResolver` remains the final authority. Avoid adding more
input-specific legality paths. `CanPlaceShip` is the common footprint check;
`DeploymentService` applies authored zone and draft-collision policy during
pre-match setup. The existing AI has some planning checks for hypothetical
attacks;
its final requests still use `CombatResolver`.

## 5. Requires Unity runtime / Inspector verification

- Run the sample scene to check actual input sequencing, Gizmo visibility,
  deployment, passive vision before Search, plane preview/confirmation,
  scan rendering, and whether direct phase advances expose invalid
  provisional state. Source and EditMode checks establish code behavior,
  not observed interactive Play mode behavior. An Untitled scene with only
  Main Camera is not `Assets/Scenes/SampleScene.unity`.
- Inspect the duplicate serialized `TurnManager` component on the grid
  GameObject. The grid, input, and AI references point to the separate
  component, but a Play mode check is needed for any side effect.
- Locate the user-reported ship sprites, tile art, and other art if they
  are outside this checkout; verify Unity import settings and whether a
  different scene or branch integrates them.
- Confirm any additional victory rules, mine ownership/visibility,
  defense timing and side effects, and plane launch terrain. Source
  reveals current behavior but does not settle intended rules.
- Check any external or Inspector-only use of public compatibility
  properties and the input-actions asset before cleanup.

## 6. Executive summary

### Implemented

The checked-in source has a 30 by 15 sample grid, authoritative ship
occupancy, authored deployment zones with Player A manual drafts and
deterministic local Player B setup, atomic pre-match confirmation, weighted
provisional Player A movement, five-phase turn events, per-player fog with
always-current passive detection and explicit Search scans, terrain LOS,
attack validation/d20/armor/ammo/death cleanup, responsive defenses,
contact mines, reconnaissance planes, and a Player B AI command path.
End handles attack-flag reset, active-mark cleanup, acting ship recharge,
plane fuel/removal, passive-fog refresh, and a completed turn summary.
Fleet elimination produces an immediate match result and partial summary.

### Unfinished / Required Next

- Detailed event history, production result UI, and any victory rules beyond
  fleet elimination.
- Player-facing input, fog, scan, movement, ship/tile, mine, and plane
  presentation. No production ship/tile art is integrated in this
  checkout; reported external art needs location/import verification.

### Known Debt — Outside Current Scope

AI movement execution still uses legacy distance-based `MoveShip`
semantics. Player B currently passes on defense; its strategy, deployment
algorithm, and movement migration are outside this mechanics work. The
local deterministic formation is not AI behavior. Future LAN support will
require stable runtime identifiers and serializable commands/results.

### Cleanup / Handoff

Remove stale references to nonexistent `DebugRecomputeFog`; retain
`MoveShip`, prototype input, useful Gizmos, and diagnostic guards while
they have active dependencies. Review unused compatibility properties,
reserved fields, and logs incrementally. Keep new gameplay legality in
shared services and views read-only.

### Requires Runtime / Inspector Verification

Sample-scene input, deployment, passive-vision timing, plane previews and
Gizmo behavior, the duplicate `TurnManager`'s effect, external art
availability/import, Inspector-only dependencies, and the unresolved
mine/plane design rules need verification beyond static source
inspection.

### Focused verification already performed

The passive-vision change passed a C# build and seven temporary Unity
EditMode tests covering Wolf vision in both domains, immediate legacy move
and domain refresh, terrain LOS, ship and plane preview isolation, committed
movement, and active-mark preservation. The temporary test files, folder,
and metadata were removed after the run. Interactive Unity Play mode was
not exercised for that change.

The End-phase change passed a C# build and six isolated Unity EditMode tests
covering lifecycle totals and one End summary, pending and rejected attacks,
successful and failed defenses, immediate combat and legacy-mine victory,
simultaneous mine draw, partial summaries without End ticks, and action/phase
guards after game over. The isolated test project and outputs were removed.
Interactive Unity Play mode was not exercised for this change.
