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

- `AIController` still targets Player A's first ship using global knowledge.
- The AI still only controls Player B's first ship.
- Search is automatic; there is no player-selected search action.
- Fog state is runtime-only; there is no player-facing UI.
- Armor, defense rolls, ammo consumption, recharge, and defense side effects are not implemented.
- There is no win-condition/game-over flow.
- Temporary fog logs and cone-count commands are removed; `GridView` retains
  the cone and halo Gizmos as planned visualization surfaces.

## Architecture overview

### Major systems

- Grid: `GridManager`, `Tile`, `FootprintUtil`
  - Builds the board, stores occupancy, validates placement, and moves ships.
- Terrain: `MapDefinition`, `TerrainType`, `Tile`
  - Authors reusable normal/costly/impassable terrain data and loads it into runtime cells.
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
  -> Search -> Fog.RecomputeAllPassive(match)
  -> Search -> Fog.RunActiveSearch(CurrentPlayer, match)
  -> End -> Fog.ClearAllActiveMarks()
```

The event flow is one-way: `TurnManager` raises `PhaseChanged`, and subscribers react without the turn system knowing about fog, ships, or the grid.

## File-by-file operating model

### `Assets/Scripts/Grid/`

- `GridManager.cs`
  - Scene-level controller for occupancy, placement, movement, match setup, and phase-driven fog updates.
- `MapDefinition.cs`
  - Reusable map asset containing dimensions and sparse terrain entries.
- `TerrainType.cs`
  - Explicit normal, costly, and impassable terrain categories.
- `CombatResolver.cs`
  - Plain combat service for attack validation, d20 resolution, damage, and destruction cleanup.
- `GridView.cs`
  - Visualization component for board, zone, cone, and halo Gizmos; it reads but does not mutate `GridManager` state.
- `FootprintUtil.cs`
  - Rotation and world-cell math for ship footprints.
- `Tile.cs`
  - One coordinate in the board; stores the current ship occupant and loaded terrain data.
- `TestShipController.cs`
  - Temporary Player A input path; requests `GridManager` operations rather than duplicating movement/attack rules.

### `Assets/Scripts/Ships/`

- `ShipInstance.cs`
  - Runtime ship state for identity, placement, health, armor, current domain, profile definitions, and charge state.
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

### `Assets/Scripts/Combat/`

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

### `Assets/Scripts/AI/`

- `AIController.cs`
  - Temporary prototype for Player B movement and attack selection.
  - It is not fog-aware yet and operates only over the current first ship in a limited way.

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

The current implementation treats both `Marked` and `Identified` as known for attack gating, but `FogGrid.Upgrade` preserves the strongest value when multiple layers overlap.

Passive detection is rebuilt on every `Search`; active marks are temporary and
cleared at `End`.

## Important implementation details

### Movement and placement

- `CanPlaceShip` validates candidate cells and rejects out-of-bounds or occupied cells from other ships.
- `MoveShip` uses Chebyshev distance from `anchorAtTurnStart` and then validates placement before mutating state.
- The movement path is atomic: remove old occupancy, mutate placement, and then place new occupancy.
- Terrain is currently data only. `MapDefinition` loads `Normal`, `Costly`, or
  `Impassable` into each `Tile`, but movement does not consume terrain cost or
  route around obstacles until a later phase.

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

1. Reject dead target.
2. Verify weapon charge readiness.
3. Require target-domain compatibility.
4. Compute minimum Chebyshev distance from the attacker anchor to target occupied cells.
5. Reject if range is insufficient.
6. Check fog knowledge via `IsTargetKnown`.
7. Roll one d20 and apply selected tier damage.
8. If health reaches zero, remove the target from grid occupancy and from the owner's live ship list.

Notes:

- Ammo consumption, armor, defenses, and side effects are not implemented yet.
- The attack gate currently checks knowledge at the cell level, not every cell of a multi-cell ship.
- `ResolveAttack` returns true even on a miss, when resolution was performed.

## AI implementation status

The current `AIController` is intentionally limited:

- It acts once per phase.
- It uses only the first Player B ship.
- It moves toward the first Player A ship during `Move`.
- It selects the best legal weapon by expected value during `Battle`.
- It does nothing during `Search`.
- It does not use the AI's own fog grid.

This is prototype behavior, not the target Milestone 5 design. Planned improvements include:

- selecting the nearest known enemy from the AI's own fog view,
- not chasing or attacking hidden targets,
- moving toward the map center when no target is known,
- acting for every living AI ship,
- using Search to build knowledge.

## Deployment and setup

`GridManager.Start` creates both player states with a Wolf/Athena roster, and `DeploymentService.DeployAll` deploys ships by calling `ShipFactory` and `PlaceShip` in sequence.

Current deployment assumptions:

- Player A starts at `anchorX = 1`, facing `0` degrees.
- Player B starts at `gridManager.width - 3`, facing `180` degrees.
- `DeployFleet` currently calls `PlaceShip` directly rather than `CanPlaceShip` first.

## Safe change guidance

When making changes, prefer these locations:

| Desired change | Primary location |
| --- | --- |
| Change board dimensions or tile creation | `GridManager.BuildGrid` |
| Author reusable map terrain | `MapDefinition` |
| Query runtime terrain | `GridManager.GetTerrainType`, `IsTerrainPassable`, `GetTerrainMovementCost` |
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
- `Assets/Scripts/FogOfWar/FogManager.cs`
- `Assets/Scripts/FogOfWar/VisionResolver.cs`
- `Assets/Scripts/Turns/TurnManager.cs`
- `Assets/Scripts/Ships/ShipInstance.cs`
- `Assets/Scripts/AI/AIController.cs`

These are the highest-leverage files for board logic, fog, turn flow, and prototype AI.
