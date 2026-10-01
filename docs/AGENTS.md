# Admirals MVP Agent Guide

This repository is a Unity prototype for a local, turn-based naval combat game. The source code is the authoritative implementation. When planning or editing code, treat the runtime logic in `Assets/Scripts` as the source of truth over older planning/status notes.

## Scope and current implementation status

This project currently implements a rectangular `30 x 15` board by default, with ships represented as plain C# runtime objects rather than `GameObject` or `ScriptableObject` assets.

Key implementation status from the current codebase:

- `GridManager` owns authoritative tile occupancy, board operations, match setup, and phase reactions.
- `CombatResolver` owns attack validation and resolution; `GridView` owns board and fog/scan Gizmo rendering.
- `MatchState` owns both player rosters and the active `ShipInstance` lists.
- Fog of War is tracked separately for Player A and Player B.
- Input and AI remain prototype controllers driven by Unity lifecycle callbacks and console/Gizmo verification.
- Passive detection, active cone detection, fog gating in attack resolution, and active-mark clearing are implemented.
- The phase system includes `Move`, `Staging`, `Search`, `Battle`, and `End`.

Known gaps still present in the prototype:

- AI movement execution still uses the legacy `MoveShip` path rather than the provisional movement system (though AI decision planners are modular, fog-aware, and control all living Player B ships).
- Active scanning operates on a single fleet scan limit per player per Search phase.
- Fog state is runtime-only; visualization is Gizmo-based (`GridView`).
- Defense rolls and side effects are not implemented. Armor reduction (`ApplyArmor`), ammo deduction, and mine recharge are implemented.
- Staging actions: Mines are implemented on `SwordFishClass` (`GridManager.DeployMine`, `MatchState.mines`); Carrier Reconnaissance Planes are implemented on `CarrierClass` (`PlaneProfile`, `PlaneUnit`, `DeployPlane`, `UndeployPlane`, `MatchState.planes`). Repair ship is not yet built.
- One attack per ship per Battle phase limit is implemented (`ShipInstance.hasAttackedThisPhase`).
- There is no win-condition/game-over flow.
- Obsolete Gizmos (`DrawDebugHalos`, `DrawMovementRanges`) have been removed; `GridView` retains active cones, planes, mines, and sensor contacts (`DrawSensorContact`) with a `revealAllInFog` developer toggle.

## Architecture overview

### Major systems

- Grid: `GridManager`, `Tile`, `FootprintUtil`
  - Builds the board, stores occupancy, validates placement, and moves ships.
- Terrain: `MapDefinition`, `TerrainType`, `Tile`
  - Authors reusable normal/costly/impassable terrain data and loads it into runtime cells.
- Movement paths: `GridPathfinder`, `MovementPathResult`
  - Performs read-only weighted 8-direction Dijkstra calculations; it does not move ships or mutate occupancy.
- Provisional movement: `ProvisionalMovementState`, `GridManager`
  - Stores movement-phase snapshots and candidate placements; confirmation commits all candidates atomically.
- Ships: `ShipInstance`, `ShipFactory`, `ShipData`, `ShipType`
  - Defines runtime ship state and builds hardcoded ship cards.
- Match: `MatchState`, `PlayerState`, `DeploymentService`
  - Owns players, fleet rosters, and live ships.
- Turns: `TurnManager`, `Phase`, `PlayerId`
  - Advances phase state and identifies the acting player.
- Fog of War: `FogManager`, `FogGrid`, `FogState`, `VisionResolver`
  - Computes detected enemy cells and stores each player's knowledge.
- Combat data: `WeaponProfile`, `DefenseProfile`, `RollTier`, `ChargeState`, `DomainType`, `VisionLayer`
  - Stores definitions for weapons, defenses, rolls, charge, domains, and vision.
- Combat behavior: `CombatResolver` via `GridManager.Combat`
  - Performs attack validation and d20 damage resolution.
- Human input: `TestShipController`
  - Prototype Player A command path.
- AI: `AIController`
  - Prototype Player B behavior; not yet fog-aware.
- Deployment: `DeploymentService`
  - Creates fleets, assigns ownership/placement, and adds ships to the grid.

### Ownership and authority

The codebase separates authority intentionally:

- `MatchState` is authoritative for which ships exist and which player owns them.
- `GridManager.tiles` is authoritative for board occupancy by coordinate.
- A ship's `anchor`, `rotationDegrees`, and `footprintOffsets` are the authoritative inputs used to calculate occupied cells.
- `FogGrid` is authoritative only for one player's knowledge. It does not own ships, calculate geometry, or maintain scan coverage history.
- `FogManager` owns the two fog grids and decides when they are rebuilt or cleared.
- `VisionResolver` is stateless behavior used by `FogManager`.

This means changes should respect the current ownership boundaries instead of duplicating logic in controllers or ship objects.

## Runtime flow

```text
GridManager.Awake()
  -> BuildGrid()
  -> new FogManager()

GridManager.Start()
  -> new PlayerState(PlayerA, roster)
  -> new PlayerState(PlayerB, roster)
  -> new MatchState(playerA, playerB)
  -> DeploymentService.DeployAll(match, gridManager)

TurnManager.AdvancePhase()
  -> changes CurrentPhase
  -> switches CurrentPlayer after End
  -> logs new state
  -> invokes PhaseChanged

GridManager.HandlePhaseChanged()
  -> Move    -> snapshot provisional states for current player's ships
  -> Staging -> confirm provisional movement atomically; snapshot plane positions
              (mine deployment: player activates via DeployMine during Staging;
               plane deployment via DeployPlane; click-select or Tab planes;
               C key undeploys freshly deployed planes and refunds sortie;
               plane movement locked on deploy turn, allowed in subsequent Staging phases via PreviewPlaneMove)
  -> Search  -> Fog.RecomputeAllPassive(match), clear deployedThisTurn flags on acting player's planes
              (active scan requires player activation via ActivateActiveScan /
               ConfirmActiveScan — it is not triggered automatically)
  -> Battle  -> ResetShipAttackStates() (each ship may fire at most once per Battle phase)
  -> End     -> Fog.ClearAllActiveMarks(), ResetShipAttackStates(), tick recharge & plane fuel
```

The event flow is one-way: `TurnManager` raises `PhaseChanged`, and subscribers
react without the turn system knowing about fog, ships, or the grid.

## File-by-file operating model

### `Assets/Scripts/Grid/`

- `GridManager.cs`
  - Scene-level controller for occupancy, placement, movement, match setup, and phase-driven fog updates.
- `MapDefinition.cs`
  - Reusable map asset containing dimensions and sparse terrain entries.
- `TerrainType.cs`
  - Explicit normal, costly, and impassable terrain categories.
- `GridView.cs`
  - Visualization component for board, zone, cone, contact marks, mines, planes, and ships; it reads but does not mutate `GridManager` state. Renders `Identified` red ship cubes vs `Marked` orange contact markers; hides enemy units in fog (toggleable via `revealAllInFog`). `DrawDebugHalos` and `DrawMovementRanges` have been removed.
- `FootprintUtil.cs`
  - Rotation and world-cell math for ship footprints.
- `Tile.cs`
  - One coordinate in the board; stores the current ship occupant and loaded terrain data.
- `TestShipController.cs`
  - Temporary Player A input path; requests `GridManager` operations rather than duplicating movement/attack rules.

### `Assets/Scripts/Ships/`

- `ShipInstance.cs`
  - Runtime ship state for identity, placement, health, armor, current domain, profile definitions, charge state, and `hasAttackedThisPhase` combat tracking.
- `ShipFactory.cs` / `ShipData.cs` / `ShipType.cs`
  - Hardcoded ship card definitions and factory creation flow.

### `Assets/Scripts/Match/`

- `PlayerState.cs`
  - Per-player fleet and live ship list.
- `MatchState.cs`
  - Stores both players and exposes aggregated live ship queries.
- `DeploymentService.cs`
  - Creates and places each roster entry.

### `Assets/Scripts/Turns/`

- `TurnManager.cs`
  - Tracks `currentPlayer` and `currentPhase`.
- `Phase.cs` and `PlayerId.cs`
  - Shared enums for phase and player identity.

### `Assets/Scripts/FogOfWar/`

- `FogState.cs`
  - Ordered knowledge levels: `Unknown`, `Marked`, `Identified`.
- `FogGrid.cs`
  - Stores passive and active knowledge for one player.
- `FogManager.cs`
  - Owns both fog grids and recomputes passive/active visibility.
- `VisionResolver.cs`
  - Stateless geometry and detection rules.
- `ActiveScanPreviewState.cs`
  - Encapsulates player active scan cone preview, direction, pivot, and confirmation.

### `Assets/Scripts/Combat/`

- `CombatResolver.cs`
  - Plain combat service for attack validation (one attack per ship per Battle phase, turn/phase gating, range, fog, LOS), d20 resolution, armor damage reduction, ammo deduction, and destruction/mark cleanup.
- `DomainType.cs`
  - `Surface`, `SubSurface`, `Both`.
- `VisionLayer.cs`
  - Defines a sensor layer: range, shape, domain, reveal type, and active/passive status.
- `WeaponProfile.cs`
  - Weapon definition with ammo and damage table.
- `DefenseProfile.cs`
  - Defense definition with save tiers and side effects placeholder.
- `RollTier.cs`
  - d20 tier model.
- `ChargeState.cs`
  - Runtime ammo/use state and recharge metadata.
- `PlaneProfile.cs` / `PlaneUnit.cs`
  - Card profile definition and runtime unit for carrier reconnaissance aircraft (launch range, staging-only movement, fuel lifecycle, absolute halo vision).
- `MineProfile.cs` / `MineTile.cs`
  - Card profile and runtime contact mine data (deployment in Staging, 600 damage on contact, 2-turn recharge).

### `Assets/Scripts/AI/`

- `AiController.cs`
  - Scene-level AI orchestrator subscribing to `TurnManager.PhaseChanged` for Player B turns.
- `AiTurnContext.cs`
  - Captures per-turn snapshot of living AI ships, friendly memory, and fleet active scan usage.
- `AiEnemyMemory.cs`
  - Tracks observed enemy positions, turn recency, and predicted locations across turns.
- `AiActiveScanner.cs`
  - Selects and executes up to 1 fleet active scan per Search phase using sensor ships.
- `AIMovementPlanner1.cs`
  - Plans movement candidates toward observed enemies or board center (legacy debt: executes via `GridManager.MoveShip`).
- `AIAttackPlanner.cs`
  - Evaluates and executes legal attacks against known targets respecting `hasAttackedThisPhase`.
- `AIScoring.cs`
  - Scoring functions for AI candidate scans, moves, and attacks.

## Core conventions for agents

### Follow the established authority boundaries

Do not move logic into the wrong layer.

- Do not put vision geometry in `FogGrid`.
- Do not put attack resolution in `ShipInstance`.
- Do not add a second placement validation path in a controller.
- Prefer modifying `GridManager` for board operations, `VisionResolver` for detection rules, and `ResolveAttack` for combat resolution.

### Respect the current phase model

The phase cycle is:

```text
Player A Move
  -> Player A Staging
  -> Player A Search (active scan)
  -> Player A Battle (input/AI may request attacks)
  -> Player A End (clear active marks)
  -> Player B Move
  -> repeat
```

Important details:

- `AdvancePhase` changes the enum first and only switches player on `End -> Move`.
- Subscribers receive the new phase, not the previous one.
- Fog recomputation and active scanning on `Search`, plus active mark clearing
  on `End`, are part of the phase wiring.
- Search and movement behavior are still prototype-level, not fully feature-complete.

### Maintain fog correctness

Fog is implemented as layered knowledge:

- `Unknown`: no evidence.
- `Marked`: detected cell, type not necessarily known.
- `Identified`: absolute vision revealed cell contents.

The current implementation treats both `Marked` and `Identified` as known for attack gating, but `FogGrid.Upgrade` preserves the strongest value when multiple layers overlap. `GridView` visually distinguishes these: `Identified` renders the solid red enemy ship cells inside absolute vision, `Marked` renders a light orange tile contact indicator without drawing the enemy ship cube, and `Unknown` leaves enemy units un-rendered in fog. An Inspector toggle (`revealAllInFog`) allows developers to reveal all units during testing.

Passive detection is rebuilt on every `Search`; active marks are temporary and
cleared at `End`.

## Important implementation details

### Movement and placement

- `CanPlaceShip` validates candidate cells and rejects out-of-bounds or occupied cells from other ships.
- `MoveShip` uses Chebyshev distance from `anchorAtTurnStart` and then validates placement before mutating state.
- The movement path is atomic: remove old occupancy, mutate placement, and then place new occupancy.
- Terrain is loaded into each `Tile` and consumed by provisional movement
  previews. The legacy `MoveShip` method remains distance-based, while keyboard
  movement now previews candidates through `GridManager.PreviewMove`; Escape
  or C cancels, and Space commits all valid previews before leaving Move.
  Enter is not a movement commit key.
  Rejected previews must not replace the last valid provisional state; this keeps
  phase confirmation atomic and prevents invalid terrain or footprint positions
  from reaching the confirmation step.

### Footprint geometry

`FootprintUtil.RotateOffsets` handles quarter-turn rotations using the project's clockwise convention:

```text
0°   (x, y)
90°  (-y, x)
180° (-x, -y)
270° (y, -x)
```

`GetWorldCells` adds the ship anchor after rotating offsets.

### Combat rules

The current authoritative attack flow in `CombatResolver.ResolveAttack` is:

1. Reject dead target (logs reason).
2. Enforce phase and turn gating: attack must occur in Battle phase during the attacking player's turn.
3. Enforce single attack limit: attacker must not have already attacked this Battle phase (`attacker.hasAttackedThisPhase == false`).
4. Verify weapon charge readiness (`IsReady` requires `remaining != 0` and `turnsUntilRecharge == 0`).
5. Require target-domain compatibility (logs reason).
6. Compute minimum Chebyshev distance across all attacker occupied cells ×
   all target occupied cells. Collect every in-range pair for step 8.
7. Reject if no cell pair is within weapon range (logs reason).
8. Check fog knowledge via `IsTargetKnown`.
9. Check terrain line-of-fire via `HasClearLineOfFire`: the attack is legal
   when at least one in-range pair has a clear supercover Bresenham path
   through the grid. Only `Impassable` terrain blocks; `Normal` and `Costly`
   are transparent. Attacker and target endpoint cells are excluded from the
   blocker test. Logs `BLOCKED_LINE_OF_FIRE` with the first blocking cell when
   every pair is blocked.
10. Roll one d20 and apply `ApplyArmor(rawDamage, target.armor)`:
   `effectiveDamage = round(rawDamage * (1 - armor * 0.0015))`. Miss stays 0;
   non-zero results are clamped to minimum 1. Log shows raw and reduced values.
11. Mark attacker as having attacked (`attacker.hasAttackedThisPhase = true`).
12. Deduct weapon ammo/charge (`weaponCharge.remaining--`, if finite).
13. If health reaches zero, remove the target from grid occupancy, remove from
   owner's live ship list, and clear all active/passive sensor contact marks
   for the sunken ship via `FogManager.ClearMarksForShip(target)`.

Notes:

- Ammo consumption (`weaponCharge.remaining--`), armor reduction (`ApplyArmor`),
  one-attack-per-ship gating (`hasAttackedThisPhase`), and destroyed ship contact
  mark cleanup (`ClearMarksForShip`) are fully implemented.
- Defenses and side effects are defined in data profiles but not yet evaluated during attack resolution.
- The fog attack gate checks knowledge at the cell level; any one known cell
  on a multi-cell target is sufficient.
- `ResolveAttack` returns `true` even on a d20 miss, when resolution was
  performed. It returns `false` on any rejection, with a log identifying the
  cause.
- `HasClearLineOfFire` reuses `VisionResolver.TryGetFirstBlockingCell` so the
  same supercover Bresenham algorithm and corner-adjacency policy govern both
  vision LOS (Phase 9A) and attack line-of-fire (Phase 9B).

## AI implementation status

The AI has been overhauled into an event-driven system subscribed to `TurnManager.PhaseChanged`:

- **Event-Driven Execution:** `AiController` listens to `PhaseChanged`. When Player B's turn advances, it executes phase logic without frame polling.
- **Context & Memory:** Captures a per-turn snapshot in `AiTurnContext` and tracks enemy sightings across turns via `AiEnemyMemory` (marking last known coordinates and aging stale contacts).
- **Multi-Ship Control:** Operates across all living Player B ships, not just a single hardcoded unit.
- **Search Phase Active Scanning:** `AiActiveScanner` scores candidate sensor ships and cone orientations, firing up to 1 fleet active scan per Search phase.
- **Move Phase Positioning:** `AIMovementPlanner1` evaluates movement candidates toward predicted/observed enemy targets or map center if no enemy has been sighted. (Legacy debt: Movement currently executes via `GridManager.MoveShip` Chebyshev steps rather than provisional Dijkstra paths).
- **Battle Phase Engagement:** `AIAttackPlanner` evaluates legal attacks against known targets in Player B's fog, respects the one-attack-per-phase constraint (`hasAttackedThisPhase`), and selects weapons by expected damage.

## Deployment and setup

`GridManager.Start` creates both player states with a Wolf/Athena roster, and `DeploymentService.DeployAll` deploys ships by calling `ShipFactory` and `PlaceShip` in sequence.

Current deployment assumptions:

- Player A starts at `anchorX = 1`, facing `0` degrees.
- Player B starts at `gridManager.width - 3`, facing `180` degrees.
- `DeployFleet` calls `CanPlaceShip` before `PlaceShip`; ships that fail
  validation are skipped with a warning log.

## Safe change guidance

When making changes, prefer these locations:

| Desired change | Primary location |
| --- | --- |
| Change board dimensions or tile creation | `GridManager.BuildGrid` |
| Author reusable map terrain | `MapDefinition` |
| Query runtime terrain | `GridManager.GetTerrainType`, `IsTerrainPassable`, `GetTerrainMovementCost` |
| Calculate a weighted route | `GridPathfinder` via `GridManager.CalculateMovementPath` |
| Change footprint rotation/world-cell math | `FootprintUtil` |
| Change placement collision rules | `GridManager.CanPlaceShip` |
| Change movement budget or mutation | `GridManager.MoveShip` |
| Change ship stats or sensor profiles | `ShipData` |
| Add a ship card | `ShipType`, `ShipFactory`, `ShipData` |
| Change initial fleets or anchors | `GridManager.Start`, `DeploymentService` |
| Change phase order or player switching | `TurnManager`, `Phase` |
| Change when fog runs | `GridManager.HandlePhaseChanged`, `FogManager` |
| Change stored fog state | `FogGrid` |
| Change halo/cone geometry or domain filtering | `VisionResolver` |
| Change attack legality and damage resolution | `CombatResolver.ResolveAttack` via `GridManager.Combat` |
| Change terrain line-of-fire logic | `CombatResolver.HasClearLineOfFire` via `VisionResolver.TryGetFirstBlockingCell` |
| Change mine deployment or detonation logic | `GridManager.DeployMine`, `GridManager.ResolveMinesFor`, `MatchState.mines` |
| Change Player A input | `TestShipController` |
| Implement fog-aware AI | `AIController`, `FogManager.GetFogGrid` |

## Temporary and debug code

The codebase still contains explicit debug or prototype code that should be
treated carefully:

- `GridView` terrain, cone, and halo Gizmos are retained as planned
  visualization surfaces.
- Console verification helpers such as `ShipInstance.LogStatBlock`.

The former `[Fog]` logs, `FogGrid.Describe`, `Debug Cone Counts`,
`FogManager.RecomputeAllPassive`, and `FogManager.RunActiveSearch` are not
current cleanup targets: the first three were removed as one-off diagnostics,
while the last two remain live fog lifecycle methods.
- Temporary toggles and assumptions in `TestShipController` and `AIController`.

These are valid for prototype validation but should not be treated as final gameplay systems unless they are intentionally kept.

## Working rules for agents

1. Use the source code as the final authority; do not rely on stale docs unless they match the current implementation.
2. Preserve the existing ownership model: board occupancy, match state, and fog state each have distinct responsibilities.
3. Do not add new gameplay logic in controller scripts when the manager layer already owns the relevant behavior.
4. Be aware that features like AI fog awareness, combat side effects, and win conditions are still incomplete.
5. When debugging or extending behavior, prefer the current implementation path (`TurnManager` → `HandlePhaseChanged` → `FogManager` / `CombatResolver.ResolveAttack`) over speculative re-architecture.

## Recommended starting points for change

If you need to add or modify behavior in this project, start by reading:

- `Assets/Scripts/Grid/GridManager.cs`
- `Assets/Scripts/Combat/CombatResolver.cs`
- `Assets/Scripts/FogOfWar/FogManager.cs`
- `Assets/Scripts/FogOfWar/VisionResolver.cs`
- `Assets/Scripts/Turns/TurnManager.cs`
- `Assets/Scripts/Ships/ShipInstance.cs`
- `Assets/Scripts/AI/AIController.cs`

These are the highest-leverage files for board logic, combat (including
line-of-fire), fog, turn flow, and prototype AI.
