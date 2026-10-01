# Admirals Developer Guide

This document describes the current Unity prototype as implemented in the
repository. It focuses on Milestone 5, Fog of War, but includes the earlier
systems that create ships, place them, advance turns, and resolve attacks.
The source code is authoritative. Where older planning/status documents differ
from the source, the discrepancy is called out below.

## Scope and implementation status

The current project is a local, turn-based naval combat prototype:

- Unity version: `6000.5.10f1`.
- The board is a rectangular `30 x 15` grid by default.
- Ships are plain C# runtime objects. They are not GameObjects or
  `ScriptableObject` assets.
-   `GridManager` owns the authoritative tile occupancy, board operations, match
  setup, and phase reaction. `CombatResolver` owns combat resolution and
  `GridView` owns Gizmo rendering.
- `MatchState` owns both player rosters and their live `ShipInstance` lists.
- Fog is maintained separately for Player A and Player B.
- Input and AI are still prototype controllers driven by Unity lifecycle
  callbacks and console/Gizmo verification.

The older `PROJECT_STATUS.md` describes Fog of War as an empty placeholder.
That is stale relative to the current source. The current source contains
passive detection, active cone detection, fog gating in `ResolveAttack`, and
active-mark clearing. The current source also contains `Staging` and `End`
phases, although the status document's earlier phase summary lists only
`Move`, `Search`, and `Battle`.

The remaining known gaps are:

- AI movement execution still uses the legacy `MoveShip` path rather than the provisional movement system (though AI decision planners are fog-aware and control all living Player B ships).
- Fog state is internal runtime state with Gizmo visualization; there is no production UI or sprite-based fog shroud.
- Defense rolls and defense side effects are not implemented. Armor reduction (`ApplyArmor`) and ammo consumption are implemented; mine recharge is implemented.
- There is no win-condition / game-over flow.
- Ship models/sprites are not yet imported; rendering is Gizmo-based (`GridView`). Verification Gizmos remain (`DrawDebugCones`, `DrawSensorContact`, `DrawMines`, `DrawPlanes`), while `DrawDebugHalos` and `DrawMovementRanges` have been removed.

---

## Recent milestone history and current direction

### Milestones 1–4: grid foundation

- The board uses `Dictionary<Vector2Int, Tile>` with Gizmo-based visualization.
- Ships support placement, movement, rotation, and collision validation through
  `CanPlaceShip` and `MoveShip`.
- The turn loop expanded from three phases to
  `Move → Staging → Search → Battle → End`.
- The full ship data model includes `WeaponProfile`, `DefenseProfile`,
  `RollTier`, `VisionLayer`, and `ChargeState`, initially implemented for Wolf
  Class and then Athena.

### Barebone Move + Attack + AI

- `CombatResolver` performs ammo/domain/range validation, d20 rolls, damage,
  and ship destruction.
- `TestShipController` supports click-to-aim targeting and number-key weapon
  selection.
- `AIController` uses greedy expected-value scoring and moves/fires
  automatically for the prototype Player B flow.
- Movement validates against `anchorAtTurnStart`, preventing repeated moves in
  one phase from exceeding the movement budget.

### Milestone 5: Fog of War and fleets

- `PlayerState`, `MatchState`, and `DeploymentService` replaced the former
  single-ship assumptions with automatically deployed fleets for both players.
- `FogState`, `FogGrid`, `FogManager`, and `VisionResolver` provide
  non-sticky, per-player fog. Passive Halo vision writes `Identified` for
  Absolute layers and `Marked` for Sensor layers; active Cone scans during
  Search write marks only.
- Attacks are gated by known target cells. Tab cycles Player A ships and
  number keys select weapons.
- End-to-end verification covered submerged detection through `Both`-domain
  sensors, destroyed-ship cleanup, asymmetric detection ranges, deployment
  validation, and the multi-move range bug.

### Housekeeping completed

- Attack range uses nearest occupied attacker and target cells rather than
  anchors only.
- `AIController.actedThisPhase` is set before acting.
- `DrawDebugHalos` and `DrawMovementRanges` Gizmos have been completely removed
  from `GridView`.
- Temporary `[Fog]` logs, `FogGrid.Describe`, and `Debug Cone Counts` were
  removed.
- Ammo deduction is implemented: `CombatResolver.ResolveAttack` decrements
  `weaponCharge.remaining` after a successful d20 roll. The `-1` infinite
  sentinel is never decremented. The outcome log includes remaining ammo count.
- Armor reduction is implemented: `CombatResolver.ApplyArmor(rawDamage, armor)`
  reduces damage by 0.15% per armor point.
- One attack per ship per Battle phase limit is implemented: `ShipInstance.hasAttackedThisPhase`
  prevents multiple attacks by the same vessel in a single Battle phase.
- `GridManager.CanAdvancePhase(Phase)` is a pure read-only pre-check called by
  `TestShipController` before every `AdvancePhase()` call. For `Move` it
  mirrors the validation loop of `ConfirmProvisionalMovement` without
  committing; for all other phases it returns true unconditionally.
- Ship placement enforces a one-tile exclusion zone:
  `GridManager.HasClearExclusionZone` rejects any candidate footprint whose
  cells are within Chebyshev distance 1 of any other ship's cells.
- Click-to-select active ship is implemented: left-clicking any friendly ship in
  any phase immediately selects it.
- Sunk ship fog mark cleanup: `FogManager.ClearMarksForShip` clears occupied
  cells from both passive and active fog upon ship destruction in combat or from
  mine detonations, preventing lingering orange contact markers.
- Fixed enemy ship movement flash in fog: `GridView` filters provisional ship
  footprints by owner and fog state.

### Current refactor boundary

The former all-in-one `GridManager` was split into three responsibilities:

| Component | Responsibility |
| --- | --- |
| `GridManager` | Board construction, occupancy, placement, movement, match setup, and phase wiring. |
| `CombatResolver` | Attack legality, nearest-cell range checks, d20 resolution, damage, and destruction cleanup. |
| `GridView` | Grid, starting-zone, cone, plane, mine, and contact Gizmo rendering; reads `GridManager` state without mutating it. |

Controllers call `GridManager` for board operations and
`GridManager.Combat` for attacks. `GridView` receives a `GridManager` reference
in the scene and is intentionally separate from gameplay authority.

### Implemented core decisions

- **Terrain & Movement**: Dijkstra pathfinding (8-direction weighted) with
  `Normal`, `Costly`, and `Impassable` terrain. Movement is provisional: keyboard
  and pointer/drag input preview candidates; `Escape`/`C` cancels; `Space`
  commits valid previews atomically and advances out of `Move`.
- **Vision LOS (Phase 9A)**: Supercover Bresenham LOS traversal
  (`VisionResolver.TryGetFirstBlockingCell`). Only `Impassable` terrain blocks;
  corner-to-corner gaps are blocked conservatively.
- **Line-of-Fire (Phase 9B)**: `CombatResolver.HasClearLineOfFire` reuses
  supercover Bresenham to gate attacks across all in-range cell pairs.
- **Active Scanning**: Player-activated during Search (`S` or clicking active ship);
  rotated via `Q`/`E` or mouse click/drag; confirmed via `Enter` or `Space` on advance;
  cancelled via `Escape`/`C`. **Fleet Active Scan Limit = 1 per player per Search phase**.
  Sonar domain detects `DomainType.Both` (Surface and SubSurface).
- **Fog of War Visuals**: Absolute vision (`Identified`) renders solid red cubes
  for enemy ships. Sensor vision (`Marked`) renders a light orange tile contact
  marker (`sensorMarkedColor`) with golden wireframe (red ship cube is hidden).
  Unrevealed cells (`Unknown`) are hidden completely. `revealAllInFog` inspector
  toggle allows developer inspection.
- **Staging — Mines**: `SwordFishClass` deploys `Contact Mine` (600 flat damage)
  one cell behind stern with `M`. Mines recharge (1 mine per 2 turns, cap 2).
  Mines trigger on confirmed movement and are revealed as `Marked` by active sonar.
- **Staging — Reconnaissance Planes**: `CarrierClass` deploys `Recon Plane` with
  `P` + click within launch range 4. Airborne unit (flies over ships/terrain).
  Click-selectable and Tab-cycleable in Staging. Movement is locked on the turn
  of deployment and unlocked in subsequent Staging phases. `C` key cancels placement
  or undeploys freshly deployed plane, refunding the carrier's sortie charge.
  Planes provide passive absolute halo vision (range 3, sees over terrain LOS) and
  consume fuel each End phase (removed at 0 fuel).
- **AI System Overhaul**: Modular planner architecture (`AIController`, `AiTurnContext`,
  `AIMovementPlanner1`, `AIAttackPlanner`, `AIScoring`, `AiActiveScanner`, `AiEnemyMemory`).
  Fog-aware decision making, controls all living Player B ships, runs active scans, and
  predicts targets with dead reckoning. (Legacy debt: movement execution still calls `MoveShip`).

---

## 1. Architecture overview

### Major systems

| System | Main implementation | Responsibility |
| --- | --- | --- |
| Grid | `GridManager`, `Tile`, `FootprintUtil` | Builds the board, stores occupancy, validates placement, and moves ships. |
| Terrain | `MapDefinition`, `TerrainType`, `Tile` | Defines reusable normal, costly, and impassable map data loaded into runtime cells. |
| Ships | `ShipInstance`, `ShipFactory`, `ShipData`, `ShipType` | Defines runtime ship state and builds hardcoded ship cards. |
| Match | `MatchState`, `PlayerState`, `DeploymentService` | Owns both players, their fleet rosters, live ships, and initial deployment. |
| Turns | `TurnManager`, `Phase`, `PlayerId` | Advances the phase sequence and identifies the acting player. |
| Fog of War | `FogManager`, `FogGrid`, `FogState`, `VisionResolver` | Computes each player's detected enemy cells and stores their knowledge. |
| Combat data | `WeaponProfile`, `DefenseProfile`, `RollTier`, `ChargeState`, `DomainType`, `VisionLayer` | Stores weapon, defense, roll-table, charge, domain, and vision definitions. |
| Combat behavior | `CombatResolver` via `GridManager.Combat` | Performs the current attack validation and d20 damage resolution. |
| Human input | `TestShipController` | Moves Player A ships, selects weapons, toggles domain, and requests attacks. |
| AI | `AIController` | Prototype Player B movement and greedy weapon selection. It is not fog-aware yet. |
| Deployment | `DeploymentService` | Creates each roster's ships, assigns ownership/placement, and adds them to the grid. |

### Ownership and authority

```text
MatchState
├── playerA : PlayerState
│   └── ships : List<ShipInstance>
└── playerB : PlayerState
    └── ships : List<ShipInstance>

GridManager
├── tiles : Dictionary<Vector2Int, Tile>       authoritative occupancy
├── Match : MatchState                          authoritative match reference
└── Fog : FogManager
    ├── Player A FogGrid
    │   ├── passive layer
    │   └── active layer
    └── Player B FogGrid
        ├── passive layer
        └── active layer
```

`MatchState` is authoritative for which ships are alive and which player owns
them. `GridManager.tiles` is authoritative for which ship occupies a board
cell. A ship's `anchor`, `rotationDegrees`, and `footprintOffsets` are the
authoritative inputs used to calculate its occupied cells; the tile dictionary
must be kept in sync by `PlaceShip`, `RemoveShip`, and `MoveShip`.

`FogGrid` is authoritative only for one player's knowledge. It does not own
ships, calculate geometry, or remember scan coverage. `FogManager` owns the two
fog grids and decides when they are rebuilt or cleared. `VisionResolver` is
stateless behavior used by `FogManager`.

### Terrain foundation

`MapDefinition` is the reusable authoring asset for a map's dimensions and
coordinate-specific terrain entries. When assigned to `GridManager`, its
dimensions are authoritative and its entries are copied into runtime `Tile`
objects during `BuildGrid`. Without an assigned asset, the existing serialized
`GridManager.width` and `height` are preserved and every tile defaults to
`Normal`.

`TerrainType` currently has three values:

- `Normal`: passable with movement cost `1`.
- `Costly`: passable with a configurable integer cost of at least `2`.
- `Impassable`: not passable; its movement cost is `0`.

`Tile.IsPassable`, `Tile.MovementCost`, and the corresponding
`GridManager` terrain queries expose this data. `GridPathfinder` consumes
terrain cost for movement. Vision line-of-sight (Phase 9A) and attack
line-of-fire (Phase 9B) both treat only `Impassable` terrain as blocking;
`Normal` and `Costly` terrain are transparent to both.

### Runtime flow

```text
GridManager.Awake()
    ├── BuildGrid()
    └── new FogManager()

GridManager.Start()
    ├── new PlayerState(PlayerA, roster)
    ├── new PlayerState(PlayerB, roster)
    ├── new MatchState(playerA, playerB)
    └── DeploymentService.DeployAll(match, gridManager)

TurnManager.AdvancePhase()
    ├── changes CurrentPhase
    ├── switches CurrentPlayer after End
    ├── logs the new state
    └── invokes PhaseChanged

GridManager.HandlePhaseChanged()
    ├── Move    → snapshot provisional states for current player's ships
    ├── Staging → confirm provisional ship movement atomically;
    │             snapshot plane positions for Staging movement;
    │             mine deployment is player-activated (M key → DeployMine);
    │             plane deployment (P key → DeployPlane, C/Esc/RMB cancels placement);
    │             planes are click-selectable or Tab-cycled; newly deployed planes activate immediately (movement locked on deploy turn);
    │             C key on freshly deployed plane undeploys it and refunds sortie;
    │             plane movement allowed in subsequent Staging phases (arrows/click → PreviewPlaneMove, C reverts moved plane)
    ├── Search  → Fog.RecomputeAllPassive(match); clear deployedThisTurn flags on acting player's planes (unlocking movement for next Staging)
    │             (active scan is player-activated: S → ActivateActiveScan,
    │              Q/E → RotateActiveScan, UI confirms → ConfirmActiveScan,
    │              Escape/C → CancelActiveScan)
    └── End     → Fog.ClearAllActiveMarks()
```

The event is deliberately one-way. `TurnManager` knows only that it raises
`PhaseChanged`; it does not know about fog, ships, or the grid.

---

## 2. File-by-file map

### `Assets/Scripts/Grid/`

#### `GridManager.cs` — `GridManager : MonoBehaviour`

The scene-level coordinator for board occupancy, placement, movement, match
setup, and phase-driven fog updates. Combat and visualization are delegated to
`CombatResolver` and `GridView`.

Important members:

- `width`, `height`, `cellSize`: serialized board settings.
- `tiles`: private coordinate-to-`Tile` dictionary.
- `match`: the runtime `MatchState`.
- `turnManager`: serialized event source.
- `Fog`: the `FogManager` created in `Awake`.
- `BuildGrid`, `GetTile`, `IsInBounds`, and `IsOccupied`: board access.
- `GetTerrainType`, `IsTerrainPassable`, and `GetTerrainMovementCost`: terrain
  data queries only; they do not alter movement rules.
- `CanPlaceShip`, `PlaceShip`, `RemoveShip`, `MoveShip`: placement/movement
  path. `CanPlaceShip` enforces `HasClearExclusionZone` (one-tile buffer).
- `PreviewMove`, `ConfirmProvisionalMovement`, `CancelProvisionalMovement`, `CanAdvancePhase`:
  provisional movement system.
- `DeployMine`, `ResolveMinesFor`: Staging mine deployment and movement triggering.
- `DeployPlane`, `PreviewPlaneMove`, `ConfirmPlaneMove`, `UndeployPlane`: Staging plane
  deployment, movement locking/unlocking, and sortie refund.
- `ActiveScanPreview`, `ActivateActiveScan`, `SetActiveScanForward`, `RotateActiveScan`,
  `ConfirmActiveScan`, `CancelActiveScan`: active scan cone preview and fleet scan limit
  (`fleetScannedThisPhase`).
- `ResetShipAttackStates`: resets `hasAttackedThisPhase` across all ships at `Battle` and `End`.
- `Combat`: `CombatResolver` facade used by controllers for attacks.
- `HandlePhaseChanged`: phase-to-fog, plane fuel tick, and mine recharge integration.

#### `GridView.cs` — `GridView : MonoBehaviour`

Visualization component that reads `GridManager` state and renders the board,
starting zones, ship footprints, active scan cones, mines, planes, and sensor
contacts through Gizmos. It does not own occupancy or mutate gameplay state.

Key visualization behaviors:

- Friendly ships: always rendered in green.
- Enemy ships:
  - Solid red cube if in `Identified` (Absolute vision).
  - Light orange sensor contact marker (`sensorMarkedColor`) with golden wireframe if in `Marked` (Sensor vision); the red ship cube is not drawn.
  - Hidden completely if `Unknown`, eliminating click traps.
- Developer toggle: `revealAllInFog` serialized field (enabled by default outside Play mode) reveals all units for inspection.
- Mines: `DrawMines` renders Player A mines in yellow, Player B mines in orange (enemy mines hidden unless `Marked` or `revealAllInFog`).
- Planes: `DrawPlanes` renders airborne diamond wireframes (green for Player A, magenta for Player B; enemy planes hidden unless known).
- Active scan cone: `DrawDebugCones` renders the active scan cone with terrain blocking visualization.
- Obsolete Gizmos: `DrawDebugHalos` and `DrawMovementRanges` have been completely removed.
- Defensively hides destroyed ship occupants on the frame of destruction.

#### `FootprintUtil.cs` — `FootprintUtil`

Stateless grid geometry utility. `RotateOffsets` applies the project's
clockwise 0/90/180/270-degree convention to local footprint offsets.
`GetWorldCells` rotates every offset and adds the ship anchor.

#### `Tile.cs` — `Tile`

Plain data object for one grid coordinate. `Position` is immutable after
construction, `Occupant` is the current ship reference, and `IsValid` remains
a reserved flag for future non-rectangular maps. `TerrainType`,
`MovementCost`, and `IsPassable` describe the loaded terrain without changing
movement behavior.

#### `MapDefinition.cs` — `MapDefinition`

Reusable `ScriptableObject` map asset containing width, height, and sparse
coordinate-to-terrain entries. Unspecified coordinates default to `Normal`.
Costly entries normalize to a cost of at least `2`; impassable entries report
no movement cost.

#### `TerrainType.cs` — `TerrainType`

Small enum containing `Normal`, `Costly`, and `Impassable`.

#### `GridPathfinder.cs` — `GridPathfinder`

Stateless 8-direction Dijkstra traversal over the runtime grid. It rejects
out-of-bounds, impassable, and other-ship-occupied cells, charges the terrain
cost of each entered destination cell, and reconstructs the cheapest route.
The moving ship's own occupied cells are allowed so its current footprint does
not block its read-only route calculation.

#### `MovementPathResult.cs` — `MovementPathResult`

Small path result containing reachability, total terrain cost, ordered cells,
and whether the route fits the supplied movement budget. This is a calculation
result only; it does not mutate ship anchors, rotations, or tile occupancy.

#### `ProvisionalMovementState.cs` — `ProvisionalMovementState`

Stores one ship's movement-phase snapshot and current candidate anchor,
rotation, path, cost, and validity. `GridManager` owns these states. Previewing
does not change the authoritative ship or tile occupancy. A rejected preview
does not overwrite the last valid candidate, so pressing into an impassable,
out-of-bounds, occupied, or over-budget destination leaves the ship ready to
confirm its previous valid position.

#### `TestShipController.cs` — `TestShipController : MonoBehaviour`

Temporary Player A input controller.

- **Any phase**: Left-clicking any friendly ship immediately selects it as the active ship (`currentShipIndex`).
- **Move phase**: Arrow keys or mouse click/drag for provisional movement; `Q`/`E` rotates; `Escape`/`C` cancels; `Space` commits and advances; submarine `D` key toggles domain (Surface/SubSurface) when active.
- **Staging phase**:
  - `M` deploys a contact mine behind the stern (`DeployMine`).
  - `P` enters plane placement mode (left-click deploys, `C`/`Escape`/RMB cancels).
  - Left-clicking friendly planes selects them; Tab cycles through ships then planes.
  - Newly deployed plane immediately activates; movement is locked on deploy turn and allowed in subsequent Staging phases; `C` undeploys plane and refunds sortie charge (`UndeployPlane`).
- **Search phase**: `S` or clicking the active ship opens active scan cone; mouse drag/click aims cone; `Q`/`E` rotates cone; `Enter` confirms (or `Space` auto-confirms on advance); `Escape`/`C` cancels. Enforces 1 fleet scan per player per Search phase.
- **Battle phase**: Number keys select weapon slot; left-clicking an enemy cell attacks (`ResolveAttack`); clicking friendly ships switches selection; pre-attack check rejects attack if ship has already attacked this phase.
- **Phase advance**: `Space` advances phases after checking `gridManager.CanAdvancePhase()`.

---

### `Assets/Scripts/Ships/`

#### `ShipInstance.cs` — `ShipInstance`

Serializable runtime state for one ship. It stores identity, ownership,
placement, movement snapshot, health, armor, current domain, profile
definitions, and mutable charge state.

`GetOccupiedCells` delegates all footprint rotation to `FootprintUtil`.
`InitializeCharges` sets health to `maxHealth`, creates one `ChargeState` per
weapon, and one per defense. Null ammo/uses become `-1`, meaning unlimited.
`LogStatBlock` is a console verification helper, not game logic.

#### `ShipFactory.cs` — `ShipFactory`

Creates a blank `ShipInstance`, dispatches to the matching `ShipData` builder,
assigns `shipType`, and returns the populated instance.

#### `ShipData.cs` — `ShipData`

Hardcoded ship-card builders:

- `BuildWolfClass`: two-cell submarine; movement range 6 (testing value);
  three weapons (MRK-1 Torpedo, Spear Anti-Ship Missile, Hippocampus Torpedo),
  three defenses (Crash Dive, Acoustic Decoys, Deep Dive), passive sonar/
  absolute vision, and active search sonar.
- `BuildAthenaClass`: three-cell surface ship; movement range 4; three weapons
  (Deck Gun, Anti-Ship Missile, Anti-Submarine Rocket), two defenses (Evasive
  Manoeuvres, Chaff & Flares), passive absolute radar/sonar, and active
  sub-surface search sonar.
- `BuildSwordFishClass`: two-cell surface ship; movement range 8; three weapons
  (Deck Gun, Anti-Ship Missile, Anti-Submarine Rocket), two defenses (Evasive
  Manoeuvres, Chaff & Flares), passive absolute radar/sonar, and active
  sub-surface search sonar with range 5. Present in the factory but not in the
  current deployment roster.
- `BuildCruiserClass`: four-cell surface ship; movement range 5; no weapons
  defined yet; two defenses; passive absolute radar/sonar and active sonar.
  Present in the factory but not in the current deployment roster.
- `BuildCarrierClass`: five-cell surface ship; movement range 5; two weapons
  (Deck Gun, Anti-Ship Missile with 5 ammo); two defenses; passive absolute
  radar/sonar and active sonar. Present in the factory but not in the current
  deployment roster.

Each builder assigns definitions, then calls `InitializeCharges`.

#### `ShipType.cs` — `ShipType`

The card identifiers: `WolfClass`, `AthenaClass`, `SwordFishClass`,
`CruiserClass`, `CarrierClass`.

---

### `Assets/Scripts/Match/`

#### `PlayerState.cs` — `PlayerState`

Owns one player's `owner` identifier, requested `fleetRoster`, and live
`ships` list. Destroyed ships are removed from `ships`; the roster is not
currently updated.

#### `MatchState.cs` — `MatchState`

Owns both `PlayerState` objects. `GetPlayer` maps a `PlayerId` to its player.
`AllShips` returns a new combined list, used by debug drawing and any logic
that needs both sides.

#### `DeploymentService.cs` — `DeploymentService`

Creates and places each roster entry. Player A starts at `anchorX = 1`,
rotation `0`; Player B starts at `gridManager.width - 3`, rotation `180`.
The 180-degree rotation makes Player B's footprints face back toward Player A.
Each new ship receives its owner, anchor, rotation, and
`anchorAtTurnStart`, is placed using `PlaceShip`, and is appended to the
player's live list. `DeployFleet` calls `CanPlaceShip` before `PlaceShip`;
ships that fail the placement check are skipped with a warning log rather
than placed at an invalid position.

---

### `Assets/Scripts/Turns/`

#### `TurnManager.cs` — `TurnManager : MonoBehaviour`

Stores `currentPlayer` and `currentPhase`, initially Player A / Move.
`AdvancePhase` follows `Move → Staging → Search → Battle → End`. Transitioning
from `End` to `Move` also calls `SwitchPlayer`. It then invokes
`PhaseChanged` with the new phase.

#### `Phase.cs` and `PlayerId.cs`

The phase and player enums used throughout the project.

---

### `Assets/Scripts/FogOfWar/`

#### `FogState.cs` — `FogState`

Ordered knowledge states: `Unknown = 0`, `Marked = 1`, `Identified = 2`.
The numeric ordering is intentional because `FogGrid.Upgrade` keeps the
strongest state written to a cell.

#### `FogGrid.cs` — `FogGrid`

Stores one player's passive and active knowledge in two dictionaries. It has
no geometry or ship references. `GetState` returns the stronger value from the
two layers; `IsKnown` checks for any non-`Unknown` result. `ResetPassive` and
`ClearActiveMarks` implement non-sticky passive recomputation and end-of-turn
active cleanup.

#### `FogManager.cs` — `FogManager`

Owns Player A and Player B `FogGrid` instances. It performs passive
recomputation on both sides, runs active scans for the acting player, and
clears both active layers. It delegates detection shape/domain rules to
`VisionResolver`.

#### `VisionResolver.cs` — `VisionResolver`

Stateless geometry and filtering implementation. It handles passive halos,
active cones, living-ship checks, `detects` domain filtering, and
`onlyWhileSurfaced`. It also provides supercover Bresenham line-of-sight
raycasting (`TryGetFirstBlockingCell`) used for both vision LOS and combat line-of-fire.

#### `ActiveScanPreviewState.cs` — `ActiveScanPreviewState`

Holds the temporary preview state for a ship's active scan cone during `Phase.Search`:
the scanning `Ship`, the active `VisionLayer`, and the current `ForwardDirection`.
Provides `Rotate(int delta)` for quarter-turn steps and `SetForward(Vector2Int)` for
mouse-drag targeting.

---

### `Assets/Scripts/Combat/`

#### `CombatResolver.cs` — `CombatResolver`

Plain combat service created by `GridManager.Awake`. It owns authoritative attack
validation, nearest-cell range checks, terrain line-of-fire validation, d20 damage
resolution, armor reduction, ammo deduction, one-attack-per-phase tracking, and
destroyed-ship cleanup.

Key members & methods:

- `ResolveAttack(attacker, target, weaponId, targetTile)`: authoritative attack pipeline.
  Validates target alive, weapon ready, attacker has not attacked this Battle phase, phase is
  Battle and acting player's turn, domain match, range, target known in fog, and clear line of fire.
  On success: rolls d20, applies `ApplyArmor`, deducts ammo, marks `hasAttackedThisPhase = true`,
  applies damage, and removes destroyed ships while clearing their fog marks (`ClearMarksForShip`).
- `CanShipAttack(ship)`: public query returning whether a ship is eligible to attack.
- `ApplyArmor(rawDamage, armor)`: applies 0.15% reduction per armor point.
- `HasClearLineOfFire(attacker, target)`: checks for at least one unblocked attacker×target
  in-range cell pair using `VisionResolver.TryGetFirstBlockingCell`.
- `IsTargetKnown(attacker, target)`: checks if any cell of the target is known in the attacker's fog.

#### `DomainType.cs`

`Surface`, `SubSurface`, and `Both`. `Both` is used by profile definitions;
`ShipInstance.currentDomain` is intended to be a concrete runtime domain.

#### `VisionLayer.cs`

Definition for one scan layer: identifier, `Halo`/`Cone` shape, range, target
domain, `Sensor`/`Absolute` reveal type, passive/active flag, and optional
surfaced-only restriction.

#### `WeaponProfile.cs`

Immutable-ish weapon definition: id, nullable ammo, target domain, weapon range,
and d20 `RollTier` table. Runtime remaining ammo is in `ChargeState`.

#### `DefenseProfile.cs`

Definition for a defense: id, nullable uses, valid incoming domain, saving-throw
tiers, and a reserved string side-effect id. No defense execution exists yet.

#### `RollTier.cs`

Serializable value type representing an inclusive d20 interval, label, and
damage. Defense tables use the label and leave damage at zero.

#### `ChargeState.cs`

Mutable runtime uses for one weapon, defense, mine, or plane slot. `remaining == -1`
means infinite. `IsReady` is true when `turnsUntilRecharge == 0` and `remaining != 0`.
`remaining` is decremented by `CombatResolver.ResolveAttack` after each successful shot
(the `-1` sentinel is never decremented).

Recharge mechanics:

- `maxCapacity`: cap on replenished stock (-1 = infinite).
- `turnsUntilRecharge`: countdown until replenishment.
- `Recharge()`: increments `remaining` up to `maxCapacity`.
- `ShipInstance.TickRecharge()`: decrements countdowns on each End phase and calls
  `Recharge()` when hitting zero. Active for mines (`rechargeTime: 2`, `count: 2`).

#### `MineProfile.cs`

Immutable mine definition: `id`, nullable `count`, flat `damage`, and `rechargeTime`.
Runtime remaining uses and recharge countdown are tracked in `ShipInstance.mineCharges`.

#### `MineTile.cs`

A live mine on the board. Stores `owner`, `position`, and `damage` (copied
from the profile at deploy time). Owned by `MatchState.mines`. Removed from
the list immediately on detonation.

#### `PlaneProfile.cs`

Immutable reconnaissance plane definition: `id`, nullable `count`, `launchRange`,
`movementRange`, `visionRange`, and `fuelTurns`.

#### `PlaneUnit.cs`

A live airborne plane unit on the board: `owner`, `position`, `fuelRemaining`,
`movementRange`, `positionAtTurnStart`, `visionLayer` (range 3 absolute halo, sees over
terrain LOS), `launchedFrom` (for sortie refund), `profileId`, and `deployedThisTurn`.
Planes fly over terrain and ships without blocking occupancy or participating in exclusion zones.

---

### `Assets/Scripts/AI/`

#### `AiController.cs` — `AiController : MonoBehaviour`

Coordinates Player B's AI actions across phases using the `TurnManager.PhaseChanged` event:

- **Move phase**: Executes movement via `AiMovementPlanner1` and `GridManager.MoveShip`.
- **Search phase**: Executes the AI's single fleet active scan via `AiActiveScanner`.
- **Battle phase**: Evaluates and fires attacks via `AIAttackPlanner`.

Collaborating AI planner classes:

- `AiTurnContext.cs`: Builds and caches AI turn context (living ships, fog visibility, enemy predictions).
- `AiMovementPlanner1.cs`: Plans movement trajectories toward known enemy contacts or center-map patrol points.
- `AIAttackPlanner.cs`: Evaluates legal attacks using `CombatResolver` range, fog, and line-of-fire checks; chooses highest-scoring weapon.
- `AIScoring.cs`: Provides utility scoring for weapon damage potential, target priority, and scan value.
- `AiActiveScanner.cs`: Evaluates all living Player B ships to pick the single best ship and cone facing for the fleet's 1 scan per Search phase.
- `AiEnemyMemory.cs`: Dead-reckoning tracker for last-known enemy positions and headings.

---

## 3. Data model and class responsibilities

### `ShipInstance`

`ShipInstance` is the runtime object representing one deployed ship:

```text
ShipInstance
├── identity: shipType, owner
├── placement: anchor, rotationDegrees, footprintOffsets
├── movement: movementRange, anchorAtTurnStart
├── condition: maxHealth, currentHealth, armor
├── state: currentDomain, hasAttackedThisPhase
├── definitions: weapons, defenses, visionLayers, mines, planes
└── runtime charges: weaponCharges, defenseCharges, mineCharges, planeCharges
```

It owns data, not orchestration. Movement belongs to `GridManager`; detection
belongs to `VisionResolver`; attack resolution belongs to `GridManager`.

### `PlayerState` and `MatchState`

```text
MatchState
├── PlayerState PlayerA
│   ├── fleetRoster: requested card types
│   └── ships: deployed/live ShipInstance objects
└── PlayerState PlayerB
    ├── fleetRoster
    └── ships
```

The live `ships` list is the source used by fog and deployment. It is also the
list from which destroyed ships are removed. `MatchState.AllShips` is a
convenience aggregate, not a separate ownership store.

### `FogGrid`

Each player owns one logical fog view, but the implementation splits it into:

- `passive`: rebuilt on every `Search` transition.
- `active`: temporary marks created during the acting player's `Search`.

The visible result is the maximum state from both layers. This means an active
`Marked` result cannot downgrade a passive `Identified` result. Clearing active
marks does not remove passive knowledge until the next passive reset.

### Profiles and charges

`WeaponProfile`, `DefenseProfile`, and `VisionLayer` describe capabilities.
`ChargeState` holds mutable use/recharge state separately. This keeps a ship
card definition independent from one particular match's remaining ammunition.

---

## 4. Fog of War implementation

### Fog states

| State | Meaning in this implementation |
| --- | --- |
| `Unknown` | Neither passive nor active layer currently knows the cell. |
| `Marked` | A sensor has detected an enemy cell, but the ship identity/type is not revealed. |
| `Identified` | An absolute vision layer has detected the cell and identifies its contents according to the milestone's model. |

The current attack gate treats both `Marked` and `Identified` as known. `GridView`
visualizes these states distinctly:
- `Identified` (Absolute vision): draws the enemy ship cells inside absolute vision in solid red (`Color.red`).
- `Marked` (Sensor vision): marks the tile with a light orange contact indicator (`sensorMarkedColor`) to visually tell that something is there while keeping the identity unknown; the red enemy ship cube is not drawn.
- `Unknown`: enemy ships, mines, and planes remain hidden in fog.
A serialized `revealAllInFog` toggle in `GridView` allows bypassing fog culling in the Scene view for development.

### Passive detection

Passive and active detection are triggered when `TurnManager` changes to
`Phase.Search`.
The actual chain is:

```text
TurnManager.AdvancePhase()
  → PhaseChanged(Phase.Search)
  → GridManager.HandlePhaseChanged(Phase.Search)
  → FogManager.RecomputeAllPassive(match)
  → RecomputePassive(PlayerA, match)
  → RecomputePassive(PlayerB, match)
```

For each owner, `RecomputePassive`:

1. Gets that owner's `FogGrid`.
2. Gets the opposing player's current live `ships` list.
3. Clears the passive dictionary.
4. Iterates every living ship in the owner's live list.
5. Iterates that ship's passive `VisionLayer` definitions.
6. Converts `Absolute` to `Identified`, otherwise `Marked`.
7. Calls `VisionResolver.GetDetectedCells`.
8. Writes each returned enemy cell with `MarkPassive`.

`FogGrid.Upgrade` prevents a weaker result from replacing a stronger one.
Therefore a sensor cannot downgrade an absolute result, regardless of profile
iteration order.

Passive visibility is non-sticky: the passive dictionary is rebuilt from the
current ship positions at every `Search`. A previous position is forgotten
unless a current living ship detects it again.

### Vision layer filtering

`VisionResolver.GetDetectedCells` applies these filters before returning cells:

1. A dead source (`currentHealth <= 0`) detects nothing.
2. A layer with `onlyWhileSurfaced` detects nothing if the source is not
   `Surface`.
3. Shapes other than `Halo` or `Cone` are ignored.
4. Dead enemies are skipped.
5. A layer whose `detects` is not `Both` must equal the enemy's current domain.
6. Only enemy occupied cells inside the shape are returned.
7. Each candidate cell is tested for line-of-sight using
   `VisionResolver.TryGetFirstBlockingCell` (supercover Bresenham traversal).
   Only `Impassable` intermediate cells block visibility; `Normal` and `Costly`
   are transparent. A blocked cell is excluded from detected cells and recorded
   in `VisionScanResult.BlockedCells` with the first blocking coordinate.

### Halo geometry

For a halo, `VisionResolver` calculates the source's full occupied cells and
checks each enemy cell against every source cell using Chebyshev distance:

```text
distance = max(abs(enemy.x - source.x), abs(enemy.y - source.y))
```

The enemy cell is detected if any source cell has distance less than or equal
to `layer.range`. The nearest occupied cell is used, not only the anchor. This
matters for multi-cell ships: a long hull projects vision from all of its
cells.

### Active detection

Active scanning is player-activated during `Phase.Search`. Passive
recomputation still runs automatically on entering Search, but the cone scan
requires explicit player input:

```text
Player presses S during Search
  → GridManager.ActivateActiveScan(ship)
      → rejected if phase is not Search
      → rejected if fleet already scanned this phase (fleetScannedThisPhase)
      → finds the ship's first non-passive Cone layer
      → creates ActiveScanPreviewState (bow, forward)

Player presses Q / E
  → GridManager.RotateActiveScan(quarterTurns)
      → rotates ActiveScanPreviewState.Forward

Player presses Enter
  → GridManager.ConfirmActiveScan()
      → rejected if phase is not Search
      → rejected if fleet already scanned this phase
      → FogManager.RunActiveSearch(ship, layer, bow, forward, match)
      → MarkActive(cell, Marked) for each detected cell
      → clears ActiveScanPreviewState
      → sets fleetScannedThisPhase = true (fleet scan limit: 1)

Player presses Escape / C
  → GridManager.CancelActiveScan()
      → discards ActiveScanPreviewState with no fog change
```

Active scans deliberately write `Marked` even if the layer's definition says
`Absolute`; the rule is that active search reveals presence, not identity.

Fleet scan limit: exactly one confirmed active scan per player per Search phase.
Once any ship in the fleet confirms an active scan, no further active scans can be
activated until the player's next Search phase.

`activeScanPreview` and `fleetScannedThisPhase` are cleared at phase transitions: entering `Search` and entering `End`.

```text
Phase.End
  → GridManager.HandlePhaseChanged(End)
  → FogManager.ClearAllActiveMarks()
  → activeScanPreview = null
  → fleetScannedThisPhase = false
```

### `VisionResolver` geometry

The project uses integer grid coordinates. A forward vector is one of the
cardinal/diagonal unit vectors produced by the footprint-derived facing. For a
cone, the bow is the occupied cell furthest from the anchor in the forward
direction. The cone begins one cell beyond the bow.

```text
                 forward
                    →
          xxx       d = 1, lateral -1..1
        xxxxx       d = 2, lateral -2..2
      xxxxxxx       d = 3, lateral -3..3
```

The actual algorithm is:

```text
perpendicular = (-forward.y, forward.x)
for d = 1 .. range:
    halfWidth = d * ConeSlope
    for l = -halfWidth .. halfWidth:
        add origin + forward*d + perpendicular*l
```

`ConeSlope` is `1`. Before any duplicate coordinates are considered, distance
`d` contributes `2*d + 1` cells. Therefore:

- Range 2: `3 + 5 = 8` cells.
- Range 4: `3 + 5 + 7 + 9 = 24` cells.

`HashSet<Vector2Int>` removes any duplicate positions. `GetBowAndFacing`
derives the forward vector from the actual occupied footprint so the scan
cannot use a different rotation convention from ship placement. A one-cell
ship defaults to `Vector2Int.right`.

---

## 5. Turn and phase flow

The complete phase cycle is:

```text
Player A Move
    ↓
Player A Staging       phase advances only when CanAdvancePhase(Move) passes;
                       ships may not move adjacent to each other (one-tile
                       exclusion zone enforced in CanPlaceShip and preview checks);
                       SwordFish can deploy a mine with M key (DeployMine)
    ↓
Player A Search        passive recompute, then active scan
    ↓
Player A Battle        input/AI may request attacks
    ↓
Player A End           clear active marks
    ↓
Player B Move
    ↓
... repeat ...
```

`AdvancePhase` changes the enum first, switches player only during the
`End → Move` transition, logs, then raises the event. Subscribers receive the
new phase, not the old one.

`TestShipController` snapshots all Player A ships' anchors when it observes a
new Move phase. `AIController` snapshots only its current first ship on a new
Player B Move phase. The snapshot is used by `GridManager.MoveShip` as the
origin for the movement-range budget.

There is no Search input selection, and no End-phase cooldown or win check.

---

## 6. Combat and Fog interaction

### Human input path

```text
TestShipController.Update()
  → HandleAttackInput(ship)
  → clicked tile's Occupant
  → GridManager.Combat.ResolveAttack(attacker, target, weapon)
```

The controller performs only input-level checks: clicked tile exists, has an
occupant, and is not friendly; selected weapon index is valid. The authoritative
attack path is `CombatResolver.ResolveAttack`.

### `CombatResolver.ResolveAttack`

The current validation order is:

1. Reject a dead target (logs reason).
2. Reject if the attacker has already attacked this Battle phase (`attacker.hasAttackedThisPhase`, logs reason). Each ship may only attack once per Battle phase.
3. Reject if not currently in the Battle phase or not the attacker's turn (when `TurnManager` is present, logs reason).
4. Find the attacker's `ChargeState` by weapon id and require `IsReady` (logs reason).
5. Require the weapon target domain to match the target domain or be `Both` (logs reason).
6. Iterate all attacker occupied cells × all target occupied cells to find the
   minimum Chebyshev distance and collect every in-range pair.
7. Reject if weapon range is less than that minimum distance (logs reason).
8. Call `IsTargetKnown` — reject if no target cell is in the attacker's fog.
9. Call `HasClearLineOfFire` on the in-range pairs. At least one pair must have
   a clear supercover Bresenham path through intermediate cells; only
   `Impassable` terrain blocks. Logs `BLOCKED_LINE_OF_FIRE` with attacker,
   weapon, target, pair, and first blocker when every pair is blocked.
10. Mark `attacker.hasAttackedThisPhase = true`. Once a ship fires its chosen weapon, it cannot attack again that phase.
11. Roll one d20 with `RollWeapon`.
12. Apply armor reduction via `ApplyArmor(result.damage, target.armor)`:
   `effectiveDamage = round(rawDamage * (1 - armor * 0.0015))`. Miss
   (`rawDamage == 0`) is unchanged. Non-zero results are clamped to minimum 1.
   The log shows both raw and reduced values.
13. Subtract `effectiveDamage` from target health.
14. If health reaches zero, clear fog marks for the destroyed target and remove it from grid occupancy and its
    owner's live `ships` list.

`IsTargetKnown` gets the attacker's fog grid and returns true if any target
occupied cell has a non-`Unknown` state. It does not require every target cell
to be known. This means a multi-cell target can be attacked when only one of its
cells is detected.

`ResolveAttack` now decrements `weaponCharge.remaining` after `RollWeapon`
succeeds. The `-1` infinite sentinel is never touched. The console log includes
remaining ammo after the shot. Once remaining reaches zero, `IsReady` returns
false and subsequent shots are rejected at step 4. `hasAttackedThisPhase` is
cleared for all ships by `GridManager.ResetShipAttackStates` on transition into
`Phase.Battle` and `Phase.End`. It returns `true` when resolution was
performed (including a d20 miss). It returns `false` on any rejection, with a
log identifying the cause.

### AI attack path

```text
AiController.HandlePhaseChanged(Battle)
  → AIAttackPlanner.ExecuteFleetAttacks(context)
      → evaluates all living Player B ships
      → checks CanShipAttack(attacker)
      → chooses targets with known cells in Player B's fog
      → scores legal weapons via AIScoring and AIAttackPlanner.CanFire
      → calls GridManager.Combat.ResolveAttack(attacker, target, weaponId, targetTile)
```

`AIAttackPlanner` respects all authoritative combat gates: fog knowledge, one-attack-per-phase
limits, range, domain, and line-of-fire.

---

## 7. Destroyed ship behavior

When a resolved attack or mine detonation reduces `currentHealth` to zero:

```text
ResolveAttack / ResolveMinesFor
  → Debug.Log destroyed message
  → RemoveShip(target)
       clears every tile whose Occupant is target
  → match.GetPlayer(target.owner).ships.Remove(target)
  → FogManager.ClearMarksForShip(target)
       removes target occupied cells from both passive and active fog grids
```

Downstream effects of ship destruction:

- The occupied cells are immediately purged from both players' fog grids, eliminating lingering orange contact markers.
- Future passive and active fog loops no longer include it as a source or target.
- `MatchState.AllShips()` no longer returns it.
- The grid tile occupancy reports `null`.
- `GridView` defensively hides the occupant on the death frame to avoid visual glitches.
- `TestShipController` clamps its selected ship index against the smaller list.

---

## 8. AI implementation

### Architecture and flow

The AI system is event-driven, subscribing to `TurnManager.PhaseChanged` (it has no `Update` loop):

```text
TurnManager.PhaseChanged(phase)
  → AiController.HandlePhaseChanged(phase)
      ├── Phase.Move    → AIMovementPlanner1.ExecuteFleetMovement(context)
      ├── Phase.Search  → AiActiveScanner.ExecuteFleetActiveScan(context)
      └── Phase.Battle  → AIAttackPlanner.ExecuteFleetAttacks(context)
```

### Components

- **`AiTurnContext`**: Initialized at the start of each phase. Gathers living Player B ships,
  scans Player B's `FogGrid` for known enemy cells, queries `AiEnemyMemory`, and tracks
  which ships have acted.
- **`AIMovementPlanner1`**: Evaluates living Player B ships. If enemy contacts are known,
  calculates step paths toward the nearest target; if no enemies are known, moves toward
  center-map patrol waypoints. Calls `GridManager.MoveShip` to commit movement.
  *(Legacy debt: still uses MoveShip rather than provisional movement).*
- **`AiActiveScanner`**: Evaluates all living Player B ships with active cone scan layers,
  scores every ship and cardinal/diagonal forward facing based on predicted enemy positions
  from `AiEnemyMemory`, and executes the single best scan to satisfy the **1 fleet scan per
  Search phase** limit.
- **`AIAttackPlanner`**: Evaluates all living Player B ships that have not yet attacked this
  phase. For each ship, identifies known target cells in Player B's fog, validates range,
  domain, and line of fire (`HasClearLineOfFire`), scores weapons using `AIScoring.ExpectedDamage`,
  and fires the optimal attack via `CombatResolver.ResolveAttack`.
- **`AiEnemyMemory`**: Dead-reckoning component that records last-seen enemy locations, headings,
  and turns elapsed to predict enemy presence even after units move into unrevealed fog.

---

## 9. Deployment

`GridManager.Start` constructs both `PlayerState` objects with:

```text
Player A: WolfClass, AthenaClass
Player B: WolfClass, AthenaClass
```

`DeploymentService.DeployAll` then calls `DeployFleet` twice:

| Player | Anchor X | Rotation | Initial Y values |
| --- | ---: | ---: | --- |
| Player A | `1` | `0` | `1`, `4`, ... |
| Player B | `gridManager.width - 3` | `180` | `1`, `4`, ... |

For each card type, `DeployFleet`:

1. Calls `ShipFactory.CreateShip`.
2. Assigns `owner`.
3. Assigns anchor and rotation.
4. Copies the anchor into `anchorAtTurnStart`.
5. Calculates occupied cells.
6. Calls `GridManager.PlaceShip`.
7. Appends the ship to `PlayerState.ships`.
8. Logs the stat block.

Player B is rotated 180 degrees so the same footprint definitions face toward
the center of the map from the opposite side. `DeployFleet` calls
`CanPlaceShip` before `PlaceShip`; a ship that fails the placement check is
skipped with a warning and not added to the player's fleet.

---

## 10. Follow-the-code walkthroughs

### Flow A: starting a match

```text
Unity calls GridManager.Awake
  → BuildGrid creates every rectangular Tile
  → Fog = new FogManager

Unity calls GridManager.Start
  → create PlayerState A and B with Wolf/Athena rosters
  → create MatchState
  → DeploymentService.DeployAll
      → ShipFactory → ShipData → InitializeCharges
      → assign owner/anchor/rotation
      → PlaceShip
      → add to live fleet
  → subscribe HandlePhaseChanged to TurnManager.PhaseChanged
```

`TurnManager.Start` independently logs the serialized initial Player A / Move
state. Fog is not computed until the first transition into Search.

### Flow B: passive fog update

```text
AdvancePhase: Staging → Search
  → PhaseChanged(Search)
  → GridManager.HandlePhaseChanged
  → FogManager.RecomputeAllPassive
  → reset Player A passive dictionary
  → resolve Player A passive layers against Player B live ships
  → reset Player B passive dictionary
  → resolve Player B passive layers against Player A live ships
```

Each result is stored by enemy occupied cell, not as a full-board coverage map.
Active scanning does not run automatically on Search; it requires player input.

### Flow C: player-activated active scan

```text
Player presses S during Search
  → TestShipController.HandleActiveScanInput
  → GridManager.ActivateActiveScan(selectedShip)
      → rejected if not Search phase or fleet already scanned this phase
  → VisionResolver.GetBowAndFacing → derives bow and forward
  → ActiveScanPreviewState created (ship, layer, bow, forward)
  → GridView.DrawDebugCones renders the cone preview each frame

Player presses Q or E
  → GridManager.RotateActiveScan(±1)
  → ActiveScanPreviewState.Rotate updates Forward
  → preview updates in Scene view

Player presses Enter
  → GridManager.ConfirmActiveScan()
      → rejected if not Search phase or fleet already scanned this phase
  → FogManager.RunActiveSearch(ship, layer, bow, forward, match)
  → VisionResolver.GetScanResult builds cone, checks LOS per enemy cell
  → MarkActive(cell, Marked) for each unblocked detected cell
  → ActiveScanPreviewState cleared
  → fleetScannedThisPhase set to true (no more active scans this turn)
```

Active marks remain through Battle and are cleared at End.

### Flow D: Player A attacks

```text
Mouse click during Battle
  → convert screen position to Vector2Int
  → GetTile(clickedPos)
  → read Occupant
  → ResolveAttack
      → dead / charge / domain checks (each logs reason on rejection)
      → build in-range attacker×target cell pairs, check min distance
      → IsTargetKnown(Player A fog)
      → HasClearLineOfFire: test each in-range pair via TryGetFirstBlockingCell
          → at least one unblocked pair → proceed
          → all pairs blocked → log BLOCKED_LINE_OF_FIRE, return false
      → d20 tier and damage
      → destroy cleanup if HP <= 0
```

### Flow E: AI attacks

```text
Player B Battle Update
  → DecideAttack(ObstructionShip, TestShip)
  → CanFire each weapon
  → ExpectedValue each legal weapon
  → ResolveAttack
      → final checks, including Player B fog gate
      → roll and apply damage
```

The AI's selection is not based on fog, even though the final attack gate is.
Consequently it may select a globally known target that its fog does not know,
then receive `Target not known.` from `ResolveAttack`.

### Flow F: ship destruction

```text
successful ResolveAttack
  → target.currentHealth -= roll damage
  → RemoveShip(target) clears Tile.Occupant references
  → remove target from owning PlayerState.ships
  → future fog passes omit it
```

### Flow G: detecting a SubSurface ship

For a passive sensor:

- The source must be alive.
- A layer with `detects = Both` can see a `SubSurface` target.
- A `detects = Surface` layer cannot.
- A halo checks the nearest source hull cell.
- A `Sensor` layer writes `Marked`.
- An `Absolute` layer writes `Identified`.

For active Search Sonar:

- The source must be alive and have a non-passive cone layer.
- The cone is built from the source bow and rotation-derived forward vector.
- `detects = SubSurface` accepts only a submerged enemy.
- The result is written as `Marked`, never `Identified`.
- The mark is cleared when `End` is entered.

For absolute vision:

- A matching `Absolute` layer writes `Identified`.
- `onlyWhileSurfaced` is checked against the source's domain, not the target's.
- The current Wolf absolute layer is disabled when the Wolf submerges.

---

## 11. Important algorithms

### Footprint rotation

`FootprintUtil.RotateOffsets` normalizes any degree value into `[0, 359]` and
supports only quarter turns:

```text
0°   (x, y)
90°  (-y, x)
180° (-x, -y)
270° (y, -x)
```

Unsupported angles log a warning and use the unrotated offset. `GetWorldCells`
then adds the anchor to every rotated offset.

### Placement and movement

`CanPlaceShip` calculates candidate cells, rejects out-of-bounds cells, and
rejects occupants belonging to another ship. A ship may overlap itself, which
allows rotation and movement checks before removing its current cells.

`MoveShip` first checks Chebyshev distance from `anchorAtTurnStart`, then calls
`CanPlaceShip`. Only after both checks pass does it remove old occupancy,
mutate anchor/rotation, and place new occupancy. This is the project's atomic
movement path.

### Fog state transition

`MarkPassive` and `MarkActive` both call `Upgrade`. Since enum values are
ordered, writing `Identified` after `Marked` keeps `Identified`, and writing
`Marked` after `Identified` has no effect. The two layers are then combined by
`GetState` using the same ordering.

### Expected damage

`AIController.ExpectedValue` calculates:

```text
sum((tier width / 20) * tier damage)
```

It is a weapon-ranking helper only. It does not roll, consume ammunition, or
change state.

---

## 12. Class relationship diagram

```text
                         TurnManager
                             |
                     PhaseChanged event
                             |
                         GridManager
              _____________/ | \_______________
             /               |                  \
        MatchState          FogManager          TestShipController
        /        \          /       \                  |
 PlayerState  PlayerState FogGrid  FogGrid              |
      |            |          \      /                  |
      +-- ships ---+           VisionResolver            |
             |                       |                  |
       ShipInstance -----------------+          ResolveAttack
        /   |    \                                  |
       /    |     \                           WeaponProfile
  footprint profiles charges                         |
       |      |        |                         ChargeState
 FootprintUtil  VisionLayer                         |
       |         |                             RollTier / DomainType
       +---------+

DeploymentService → ShipFactory → ShipData → ShipInstance
AIController ------------------------------→ GridManager
```

---

## 13. Where should I make changes?

| Desired change | Primary location | Reason |
| --- | --- | --- |
| Change board dimensions or tile creation | `GridManager.BuildGrid` and serialized fields | Grid ownership lives there. |
| Author map terrain | `MapDefinition` | Reusable dimensions and sparse coordinate-to-terrain entries. |
| Query runtime terrain | `GridManager.GetTerrainType`, `IsTerrainPassable`, `GetTerrainMovementCost` | Read terrain data without changing current movement semantics. |
| Calculate a weighted route | `GridPathfinder` via `GridManager.CalculateMovementPath` | Read-only 8-direction Dijkstra; does not move ships or mutate occupancy. |
| Change footprint rotation/world-cell math | `FootprintUtil` | All placement and vision hull calculations reuse it. |
| Change placement collision rules | `GridManager.CanPlaceShip` | This is the single placement validation path, including the one-tile exclusion zone. |
| Change movement budget or mutation | `GridManager.MoveShip` | It owns range validation and atomic grid updates. |
| Change ship stats or sensor profiles | `ShipData` | Hardcoded card definitions are built there. |
| Add a ship card | `ShipType`, `ShipFactory`, and `ShipData` | Factory dispatch and card data are separate by design. Five cards exist: Wolf, Athena, SwordFish, Cruiser, Carrier. Only Wolf and Athena are in the active deployment roster. |
| Change initial fleets/anchors | `GridManager.Start` and `DeploymentService` | Match creation and deployment are here. |
| Change phase order/player switching | `TurnManager` and `Phase` | `TurnManager` is the event source. |
| Change when fog runs | `GridManager.HandlePhaseChanged` / `FogManager` | The former wires phases; the latter coordinates operations. |
| Change stored fog state | `FogGrid` | It owns passive/active dictionaries and state upgrades. |
| Change halo/cone geometry or domain filtering | `VisionResolver` | It is the stateless vision rules layer. |
| Change attack legality/resolution | `CombatResolver.ResolveAttack` | This is the authoritative attack path, exposed through `GridManager.Combat`. |
| Change terrain line-of-fire logic | `CombatResolver.HasClearLineOfFire` and `VisionResolver.TryGetFirstBlockingCell` | `HasClearLineOfFire` iterates in-range pairs; the supercover traversal lives in `VisionResolver`. |
| Change Player A input | `TestShipController` | It should request manager operations rather than duplicate rules. |
| Implement fog-aware AI | `AIController` plus `FogManager.GetFogGrid` | AI decisions need its own fog view; final attacks still use `CombatResolver.ResolveAttack`. |
| Add armor/defenses/ammo spending | Future combat work around `CombatResolver.ResolveAttack`, `ChargeState`, and profiles | Ammo deduction and armor reduction are implemented. Defense saves, recharge, and side effects are not. |

Do not put vision geometry in `FogGrid`, attack resolution in `ShipInstance`,
or a second placement validator in an input/controller class.

---

## 14. Temporary and debug code

| Code | Location | Purpose | Safe removal? |
| --- | --- | --- | --- |
| `[Fog]` logs | Formerly in `FogManager` | Verified passive and active detected cells in the Console. | Removed; fog state is unchanged. |
| `FogGrid.Describe` | Formerly in `FogGrid.cs` | Formatted both dictionaries for temporary logs. | Removed; no caller remains. |
| `Debug Recompute Fog` | `GridManager.cs` context menu | Manually recomputes passive fog and prints per-cell observations. | Yes; it is not part of the phase flow. |
| `Debug Cone Counts` | Formerly in `GridManager.cs` | Confirmed cone cell counts for temporary verification. | Removed; it only logged. |
| `DrawDebugCones` | `GridView.OnDrawGizmos` | Displays active cone geometry for visual verification and planned scan UI. | Retained as a visualization surface. |
| `DrawDebugHalos` | Formerly in `GridView.OnDrawGizmos` | Displayed passive halo coverage. | Removed; method and Gizmo deleted. |
| `DrawMovementRanges` | Formerly in `GridView.OnDrawGizmos` | Displayed reachable anchor cells. | Removed; method and Gizmo deleted. |
| `revealAllInFog` toggle | `GridView.cs` | Bypasses fog culling for developer inspection in Scene/Game view. | Retained for developer inspection; default false in Play mode. |
| `[AI]` logs | `AiController.cs` and planners | Shows movement and weapon decisions. | Optional; useful while AI undergoes tuning. |
| `LogStatBlock` and occupancy logs | `ShipInstance`, `DeploymentService`, `GridManager` | Verifies card data and deployment. | Optional verification helpers. |

---

## 15. Current implementation versus planned implementation

| System | Current implementation | Planned / missing |
| --- | --- | --- |
| Terrain | `MapDefinition` loads normal, costly, and impassable data into runtime `Tile` objects; unassigned maps default to all `Normal`. `GridPathfinder` consumes that data for read-only weighted routes. | Additional terrain types if needed; no A* planned. |
| Vision LOS | Phase 9A implemented: `VisionResolver.TryGetFirstBlockingCell` runs supercover Bresenham per candidate cell. Only `Impassable` blocks; `Costly` is transparent. Corner-adjacent blockers are handled conservatively. | Player-facing fog/visibility UI. |
| Attack line-of-fire | Phase 9B implemented: `CombatResolver.HasClearLineOfFire` tests every in-range cell pair via the same supercover traversal. At least one clear pair allows the attack; all blocked → rejected before rolling. | — |
| Fog storage | Two `FogGrid` objects, each with passive and active dictionaries. Purges destroyed ship cells via `ClearMarksForShip`. | Player-facing fog/visibility UI. |
| Passive detection | `Search` recomputes live enemy cells using halo/cone rules, domain filters, and terrain LOS. Planes provide passive halo vision (range 3) that sees over terrain LOS. | — |
| Active Search | Player-activated during Search (`S` or clicking active ship); aimed with mouse drag/click or `Q`/`E`; confirmed with `Enter` or `Space` on advance; cancelled with `Escape`/`C`. **Fleet Active Scan Limit = 1 per player per Search phase**. Sonar domain detects `DomainType.Both`. | UI scan buttons. |
| Fog visualization | `GridView` distinguishes `Identified` (solid red cube) from `Marked` (light orange contact marker `sensorMarkedColor` with golden wireframe; red ship cube hidden). Unrevealed cells are completely hidden. `revealAllInFog` toggle in Inspector. | Final art / sprite shroud. |
| Fog lifetime | Passive is rebuilt at `Search`; active is cleared at `End`; dead ship marks cleared on destruction; no ghost positions. | Persistent last-known markers, explicitly deferred. |
| Combat gate | `ResolveAttack` verifies target is known, phase is Battle and acting player's turn, weapon is ready, target domain matches, and ship has not already attacked (`hasAttackedThisPhase`). | Combat reveal hook after firing. |
| Combat resolution | d20 tier damage, armor reduction (`ApplyArmor`), ammo deduction, **one attack per ship per Battle phase limit** (`hasAttackedThisPhase`), and destroyed-ship cleanup. All rejections log their cause. | Defense saves, defense side effects. |
| AI | Modular planner architecture (`AiTurnContext`, `AIMovementPlanner1`, `AIAttackPlanner`, `AIScoring`, `AiActiveScanner`, `AiEnemyMemory`). Controls all living Player B ships, respects fog, plans 1 fleet scan per turn, and predicts targets. | Migrate AI movement from `MoveShip` to provisional movement. |
| Deployment | Both players receive Wolf, Athena, SwordFish, Cruiser, Carrier at hardcoded anchors. `CanPlaceShip` validation runs before each placement. | Player-controlled deployment. |
| Staging — mines | `SwordFishClass` deploys `Contact Mine` (600 flat damage, no roll, no armor, no defenses) one cell behind stern with `M`. Mines recharge (1 per 2 turns, cap 2). Trigger on confirmed movement. Enemy mines hidden until active sonar reveals them as `Marked`. | Repair ship. |
| Staging — planes | `CarrierClass` deploys `Recon Plane` with `P` + click within launch range 4. Airborne unit (flies over ships/terrain). Click-selectable and Tab-cycleable in Staging. Movement locked on deploy turn and allowed in subsequent Staging phases; `C` undeploys plane and refunds sortie charge (`UndeployPlane`). Fuel ticks down at End phase (removed at 0). | Plane combat / air defense. |
| Movement | Keyboard and pointer dragging create read-only provisional previews from movement snapshot. Escape or C cancels back, and Space commits all valid previews before leaving Move. `Enter` is not a movement commit key. `GridManager` commits all valid ship previews atomically using Dijkstra result. | Richer movement UI. |
| Cleanup | Board, cone, plane, mine, and contact verification Gizmos remain; `DrawDebugHalos` and `DrawMovementRanges` removed. | Remove other prototype-only verification helpers when no longer useful. |
| Match end | Dead ships are removed from grid and live fleet. | Win-condition/game-over handling. |

---

## 16. Glossary

| Term | Meaning |
| --- | --- |
| Anchor | The world grid coordinate stored by a `ShipInstance`; footprint offsets are added to it. |
| Footprint | Local list of `Vector2Int` offsets describing a ship's occupied cells. |
| Occupied cell | A world grid coordinate returned by `GetOccupiedCells`. |
| Domain | `Surface` or `SubSurface` runtime state; `Both` is used by definitions. |
| Surface | Ship runtime domain visible to surface-compatible weapons/sensors. |
| SubSurface | Submerged runtime domain visible to sub-surface-compatible weapons/sensors. |
| Sensor | Vision type that produces `Marked` knowledge. |
| Absolute | Vision type that produces `Identified` knowledge during passive recompute. |
| Marked | Known enemy position/domain without full identity in the milestone model (renders light orange contact marker). |
| Identified | Stronger fog state written by an absolute passive layer (renders solid red ship cubes). |
| Passive | Vision layer evaluated during `Search` without an active action. |
| Active | Vision layer evaluated during `Search`; its marks last until `End`. |
| Halo | Chebyshev-radius vision checked from the nearest source hull cell. |
| Cone | Directional scan extending from the bow using `ConeSlope = 1`. |
| FogGrid | One player's passive/active detected-cell state. |
| VisionResolver | Stateless class that applies vision geometry and filtering rules. |
| PhaseChanged | `TurnManager` event raised after each phase transition. |
| `anchorAtTurnStart` | Movement-phase origin used to enforce the per-turn range budget. |
| Bow | The occupied cell furthest from the anchor in the footprint-derived facing direction. |

---

## 17. Quick Reference

### Class → responsibility

```text
GridManager            → board, occupancy, movement, attack resolution, staging actions, phase/fog wiring
Tile                   → one coordinate, terrain data, and current occupant
FootprintUtil          → rotate footprints and calculate world cells
ShipInstance           → runtime ship state, profiles, charge states, attack status
ShipFactory            → dispatch ship creation
ShipData               → hardcoded Wolf/Athena/SwordFish/Cruiser/Carrier definitions
MatchState             → both players, aggregate ship access, active mines, active planes
PlayerState            → one player's roster and live ships
DeploymentService      → initial fleet construction and placement
TurnManager            → phase/player state and PhaseChanged event
FogManager             → coordinates when each player's vision is evaluated
FogGrid                → passive/active fog state for one player
VisionResolver         → halo/cone geometry, LOS supercover raycasting, detection filtering
ActiveScanPreviewState → active scan cone preview state (ship, forward direction)
CombatResolver         → authoritative attack pipeline, range, LOS, d20, armor, ammo, death cleanup
PlaneProfile           → immutable plane stats (range, fuel, vision)
PlaneUnit              → live airborne plane unit (position, fuel, vision layer, sortie refund)
MineProfile            → immutable mine stats (damage, count, rechargeTime)
MineTile               → live board mine (position, damage)
ChargeState            → runtime ammo/uses, cooldowns, maxCapacity, Recharge()
AiController           → event-driven Player B controller subscribing to PhaseChanged
AiTurnContext          → AI turn evaluation context (live ships, fog visibility, enemy tracking)
AIMovementPlanner1     → AI movement planning toward contacts or patrol points
AIAttackPlanner        → AI attack selection using authoritative combat gates
AiActiveScanner        → AI single fleet active scan planner
AiEnemyMemory          → AI dead-reckoning target prediction
AIScoring              → AI utility scoring for attacks and scans
TestShipController     → Player A keyboard and mouse input controller
GridView               → Gizmo visualization of board, ships, cones, planes, mines, sensor contacts
```

### Method → purpose

```text
GridManager.CanPlaceShip               → validate candidate footprint (includes one-tile exclusion zone)
GridManager.CanAdvancePhase            → pure read: check whether the current phase is ready to advance
GridManager.MoveShip                   → validate and atomically move/rotate a ship (AI legacy path)
GridManager.PreviewMove                → compute Dijkstra path and preview candidate movement
GridManager.ConfirmProvisionalMovement → atomically commit all provisional ship movements
GridManager.DeployMine                 → deploy a contact mine behind the stern during Staging (SwordFish)
GridManager.DeployPlane                → deploy a reconnaissance plane within launch range during Staging (Carrier)
GridManager.PreviewPlaneMove           → validate and preview plane movement during Staging
GridManager.UndeployPlane              → undeploy freshly deployed plane and refund carrier sortie charge
GridManager.ActivateActiveScan         → begin player-controlled scan preview for a ship (enforces fleet limit)
GridManager.SetActiveScanForward       → aim active scan cone via mouse click or drag
GridManager.RotateActiveScan           → rotate active scan cone preview by quarter turns
GridManager.ConfirmActiveScan          → resolve active scan and write fog marks
GridManager.CancelActiveScan           → discard scan preview with no fog change
GridManager.ResetShipAttackStates      → reset hasAttackedThisPhase across all ships
CombatResolver.ResolveAttack           → authoritative attack path (one-attack limit + fog + LOS + d20 + armor + ammo)
CombatResolver.CanShipAttack           → query whether a ship is eligible to attack in current Battle phase
CombatResolver.ApplyArmor              → calculate damage reduction from target armor (0.15% per point)
CombatResolver.HasClearLineOfFire      → test in-range cell pairs for terrain obstruction
CombatResolver.IsTargetKnown           → apply the fog attack gate
FogManager.RecomputeAllPassive         → rebuild both passive fog views during Search (ships and planes)
FogManager.RunActiveSearch             → mark cells found by one ship's active scan layer (ships and mines)
FogManager.ClearMarksForShip           → purge destroyed ship cells from passive and active fog grids
FogManager.ClearAllActiveMarks         → clear temporary search results at End
ShipInstance.TickRecharge              → decrement cooldowns and replenish charges at End
TurnManager.AdvancePhase               → advance phase and raise PhaseChanged
```

### System → main entry point

```text
Grid             → GridManager.Awake / Start
Deployment       → DeploymentService.DeployAll
Turns            → TurnManager.AdvancePhase
Passive fog      → GridManager.HandlePhaseChanged(Search)
Active fog       → GridManager.ActivateActiveScan / ConfirmActiveScan (player-triggered)
Combat           → GridManager.Combat.ResolveAttack
Player input     → TestShipController.Update
AI               → AiController.HandlePhaseChanged (event-driven)
```

### Event → who raises it / who listens

```text
PhaseChanged → raised by TurnManager.AdvancePhase
             → listened to by GridManager.HandlePhaseChanged and AiController.HandlePhaseChanged
```

### Data → where it is stored

```text
Board occupancy → GridManager.tiles
Match ownership → MatchState.playerA/playerB
Live fleets     → PlayerState.ships
Active mines    → MatchState.mines
Active planes   → MatchState.planes
Ship state      → ShipInstance
Card definitions→ ShipData-built profiles
Charges         → ShipInstance.weaponCharges / defenseCharges / mineCharges / planeCharges
Player fog      → FogManager's two FogGrid instances
```

---

## 18. Complete Log of Implemented Features (from CHANGELOG)

The following implemented features have been integrated into the codebase (derived directly from `CHANGELOG.md`):

1. **Plane Staging Click Selection and Undeploy on Cancel**:
   - `PlaneUnit` tracks `launchedFrom`, `profileId`, and `deployedThisTurn`.
   - In Staging, friendly planes can be selected by clicking their tile or cycling with Tab.
   - Newly deployed plane immediately activates upon placement.
   - Movement is locked on the turn of placement and unlocked in subsequent Staging phases.
   - Pressing `C` while controlling a plane deployed this turn calls `GridManager.UndeployPlane`, removing the plane and refunding the carrier's sortie charge (`ChargeState.remaining++`, capped at `maxCapacity`). Older planes revert move on `C`.
   - `deployedThisTurn` is cleared on transition to `Phase.Search`.

2. **Plane Movement in Staging Phase**:
   - Reconnaissance planes move during `Phase.Staging` instead of `Phase.Move`.
   - `PreviewPlaneMove` validates `Phase.Staging` and player ownership.
   - Position snapshotting moved from Move to Staging.
   - Space commits plane moves when advancing from Staging.

3. **One Attack Per Ship Per Battle Phase Limit**:
   - `ShipInstance.hasAttackedThisPhase` tracks attack state.
   - `CombatResolver.ResolveAttack` rejects extra attacks (`ALREADY_ATTACKED_THIS_PHASE`) and gates attacks to active Battle phase and current player's turn.
   - `CombatResolver.CanShipAttack(ship)` provides public query.
   - `GridManager.ResetShipAttackStates()` resets attack state across all ships on entry to `Phase.Battle` and `Phase.End`.

4. **Sunk Ship Contact Cleanup**:
   - `FogManager.ClearMarksForShip(target)` purges occupied cells from both passive and active fog grids upon ship destruction in combat or from mine detonations.
   - `GridView` defensively hides destroyed occupants on the death frame, eliminating lingering orange contact artifacts.

5. **Fix Enemy Ship Movement Flash in Fog**:
   - `GridView.DrawProvisionalShips()` filters provisional footprints by owner and fog state. Enemy ships move authoritatively and are not provisionally rendered.

6. **Active Search Sonar Domain Set to Both**:
   - `AthenaClass`, `CruiserClass`, `CarrierClass` active sonar detects domain updated to `DomainType.Both` (detecting Surface and SubSurface vessels).
   - Preserves `VisionType.Sensor` so contacts are marked (`FogState.Marked`) with the orange contact indicator rather than identified.

7. **Fog of War Visual Differentiation (The Red Cube Solution)**:
   - `Identified` (Absolute vision): renders solid red cubes for enemy ship cells.
   - `Marked` (Sensor vision): renders light orange contact indicator (`sensorMarkedColor`) with golden wireframe; red ship cube is hidden.
   - `Unknown`: enemy ships, mines, and planes are completely hidden.
   - `revealAllInFog` serialized field on `GridView` allows developer inspection.

8. **Mouse Drag & Click Active Scan Cone Aiming**:
   - Mouse click or drag aims the active scan cone toward the cursor.
   - Clicking the active ship in Search opens the cone preview.
   - Keyboard controls (`S`, `Q`/`E`, `Enter`, `Escape`/`C`) remain fully functional.

9. **Removal of Obsolete Gizmos**:
   - Deleted `DrawDebugHalos` from `GridView` (passive vision halo wireframe removed).
   - Deleted `DrawMovementRanges` from `GridView` (movement range box removed).

10. **Click-to-Select Active Ship**:
    - Left-clicking any friendly ship in any phase immediately selects it as the active ship (`currentShipIndex`).

11. **Fleet Active Scan Limit (1 per player per Search phase)**:
    - Enforced single confirmed active scan per player per Search phase (`fleetScannedThisPhase`).
    - Single `ActiveScanPreview` state in `GridManager`.
    - Space auto-confirms pending active scan preview when advancing out of Search.
    - WolfClass submarine domain toggle (`D`) gated to Move phase when active.

12. **Carrier Reconnaissance Plane System**:
    - `PlaneProfile.cs` (id, count 1, launch 4, move 5, vision 3, fuel 3) and `PlaneUnit.cs`.
    - Carrier deploys plane in Staging (`P` + click).
    - Airborne unit (flies over ships and terrain).
    - Passive absolute halo vision (range 3) sees over terrain LOS.
    - Fuel decrements on owning player's End phase; removed at 0 fuel.
    - Rendered as diamond wireframes (green Player A, magenta Player B).

13. **Contact Mines with Recharge**:
    - `MineProfile.cs` and `MineTile.cs`.
    - `SwordFishClass` deploys Contact Mine behind stern with `M`.
    - Mines deal 600 flat damage on confirmed movement.
    - Recharge: 1 mine per 2 turns, cap 2 (`rechargeTime: 2`, `maxCapacity: 2`). `ShipInstance.TickRecharge` recharges spent stock.
    - Enemy mines hidden until revealed as `Marked` by active sonar.

14. **Armor Reduction**:
    - `CombatResolver.ApplyArmor(rawDamage, armor)` applies `round(rawDamage * (1 - armor * 0.0015))` (0.15% reduction per armor point).

15. **Ammo Deduction**:
    - `CombatResolver.ResolveAttack` decrements `weaponCharge.remaining` after each successful shot (infinite `-1` ammo is preserved).

16. **Attack Rejection Logs**:
    - Specific rejection reasons logged for all paths: dead target, already attacked, not battle phase, not your turn, not ready, domain mismatch, out of range, target unknown, line of fire blocked.

17. **Phase 9B Terrain Line of Fire**:
    - `CombatResolver.HasClearLineOfFire` tests attacker×target cell pairs via `VisionResolver.TryGetFirstBlockingCell`. At least one clear pair required; blocked shots rejected before rolling.

18. **One-Tile Exclusion Zone**:
    - `GridManager.HasClearExclusionZone` enforces a 1-cell buffer around all ships during placement and confirmation.

19. **Modular AI System Overhaul**:
    - `AiController` event-driven architecture using `PhaseChanged`.
    - `AiTurnContext`, `AIMovementPlanner1`, `AIAttackPlanner`, `AIScoring`, `AiActiveScanner`, `AiEnemyMemory`.
    - Fog-aware decision making, controls all living Player B ships, dead-reckoning enemy tracking.

20. **CruiserClass and CarrierClass Ship Cards**:
    - Added `BuildCruiserClass` and `BuildCarrierClass` to `ShipData`, `ShipFactory`, and `ShipType`.

