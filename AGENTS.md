# Admirals MVP Agent Guide

This repository is a Unity prototype for a local, turn-based naval combat game.

## Source of truth

- Treat the runtime source in `Assets/Scripts` as authoritative.
- If this guide or another document disagrees with the current implementation, follow the source.
- Before a non-trivial change, inspect the affected implementation and its direct callers/collaborators.
- Do not preserve stale behavior merely because an older note describes it.
- If a change makes this guide or `docs/ARCHITECTURE.md` materially inaccurate, update the relevant documentation when practical.

For the deeper system map and runtime flow, see [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Current project snapshot

- Default board: rectangular `30 x 15`.
- Ships are plain C# runtime objects, not ship `GameObject` or `ScriptableObject` instances.
- `GridManager` owns authoritative board occupancy, board operations, match setup, and phase reactions.
- `MatchState` owns players, rosters, and live `ShipInstance` collections.
- `CombatResolver` owns attack validation and resolution.
- `GridView` reads state and renders board/fog/scan Gizmos; it must not become a gameplay authority.
- Fog is tracked separately for Player A and Player B.
- The phase cycle is `Move -> Staging -> Search -> Battle -> End`.
- Match setup binds players to neutral map deployment zones. Player A drafts its roster manually; a deterministic local routine drafts Player B. Both formations commit together before the first Move phase.
- Passive detection, active cone detection, fog attack gating, active-mark clearing, armor reduction, ammo deduction, mines, reconnaissance planes, and one-attack-per-ship gating are implemented.
- AI decisions use fog knowledge/enemy memory, but AI movement execution still uses legacy `GridManager.MoveShip` rather than provisional/Dijkstra movement.

Known prototype gaps:

- Player B currently passes on defense automatically; its defense strategy remains future work.
- Active scanning currently uses one fleet scan per player per Search phase.
- Fog visualization is still Gizmo/debug oriented.
- Repair ship is not yet implemented.
- There is no win-condition/game-over flow.
- Input and several orchestration surfaces remain prototype-level.

Do not “finish” unrelated gaps as part of a focused task.

## Authority boundaries

Preserve these ownership rules unless the requested task intentionally changes the architecture.

### Match state

`MatchState` is authoritative for which runtime ships/entities exist and which player owns them.

Do not create parallel live-ship ownership in controllers, views, fog state, or AI memory.

### Grid and placement

`GridManager.tiles` is authoritative for occupancy by coordinate.

A ship's `anchor`, `rotationDegrees`, and `footprintOffsets` are the authoritative inputs for its occupied cells.

Use `GridManager` for board mutation, placement, movement, match setup, and phase-driven board operations.

Do not add a second placement/collision validation path in a controller or AI planner.

### Fog and vision

`FogGrid` stores one player's knowledge only. It must not own ships, board occupancy, vision geometry, or attack resolution.

`FogManager` owns the player fog grids and their lifecycle.

`VisionResolver` is stateless detection/LOS geometry.

Do not move vision geometry into `FogGrid`.

### Combat

`CombatResolver`, reached through the existing `GridManager.Combat` path, owns attack legality and attack resolution.

Do not put attack resolution in `ShipInstance`, input controllers, AI planners, or views.

### Views and controllers

`GridView` is read-only visualization.

`TestShipController` and AI code should request actions from authoritative systems rather than duplicate gameplay rules.

## Phase invariants

```text
Player A Move
  -> Staging
  -> Search
  -> Battle
  -> End
  -> Player B Move
  -> repeat
```

Preserve these behaviors unless deliberately changing turn rules:

- `TurnManager.AdvancePhase()` changes the phase first.
- The active player switches only on `End -> Move`.
- `PhaseChanged` subscribers receive the new phase.
- `TurnManager` should remain unaware of fog, combat, occupancy, mine, or plane implementation details.
- Prefer the existing event-driven phase flow over new frame polling.

Current phase reactions:

- `Move`: snapshot provisional movement state.
- `Staging`: atomically confirm provisional ship movement and snapshot plane positions.
- `Search`: refresh passive fog; active scan remains an explicit player/AI action.
- `Battle`: reset per-ship attack state for the Battle phase.
- `End`: clear active fog marks and tick current end-of-turn lifecycle such as recharge/fuel.

## Movement and footprint invariants

- `GridManager.CanPlaceShip` is the placement authority.
- Candidate placement must reject out-of-bounds cells and cells occupied by another ship.
- Occupancy changes must remain atomic through a validated path.
- `GridPathfinder` / `MovementPathResult` calculate weighted 8-direction paths; they do not mutate occupancy.
- Provisional movement stores snapshots/candidates and commits confirmed movement atomically.
- An invalid preview must not replace the last valid provisional state.
- Player provisional movement currently uses the `GridManager` preview/confirm path.
- Escape or C cancels; Space commits valid previews before leaving Move; Enter is not the movement commit key.
- Legacy `MoveShip` remains distance-based and is still used by AI movement. Do not silently mix legacy and provisional semantics.

`FootprintUtil.RotateOffsets` uses the project's clockwise convention:

```text
0°   (x, y)
90°  (-y, x)
180° (-x, -y)
270° (y, -x)
```

## Fog invariants

Fog knowledge levels are:

- `Unknown`: no evidence.
- `Marked`: detected contact/cell, not necessarily fully identified.
- `Identified`: absolute vision revealed the cell contents.

Current behavior:

- Both `Marked` and `Identified` count as known for attack gating.
- `FogGrid.Upgrade` preserves the strongest overlapping knowledge.
- Passive detection is rebuilt after deployment and every committed ship, domain, or plane vision change, including destruction; Search also refreshes it.
- Ship and plane movement previews do not affect live occupancy or passive fog.
- The Wolf's passive Absolute Vision works at Surface and SubSurface.
- Active marks are temporary and are cleared on `End`.
- Fog state remains per-player.
- Destroyed ships must not leave stale fog/contact marks.
- `GridView` may visualize fog states but must not change them.

Do not expose hidden enemy state to controllers or AI when the acting player's fog/memory API should be used.

## Combat invariants

The authoritative path is `CombatResolver.RequestAttack` followed by `SubmitDefense` when a response is pending.

Preserve these current rules unless the task explicitly changes gameplay:

1. Reject a dead target.
2. Require the attacker's `Battle` phase and turn.
3. Enforce one attack per ship per Battle phase.
4. Require weapon/charge readiness.
5. Require target-domain compatibility.
6. Determine range from occupied-cell pairs using Chebyshev distance.
7. Require at least one pair in range.
8. Require the target to be known in the attacker's fog.
9. Require clear terrain line-of-fire for at least one in-range pair.
10. Offer ready defenses matching the defender's current domain; block phase advancement while Player A chooses.
11. Roll and spend a selected defense before the weapon roll. A successful defense avoids damage; Crash Dive submerges the ship and locks its next Move.
12. Consume the attacker's per-phase attack opportunity and finite ammo/charge exactly once on resolution.
13. Otherwise resolve the d20 damage table and armor reduction. On destruction, remove the target from occupancy/live-ship state and clear its sensor contact marks.

Current armor formula:

```text
effectiveDamage = round(rawDamage * (1 - armor * 0.0015))
```

A miss stays `0`; non-zero effective damage is clamped to at least `1`.

Combat line-of-fire reuses `VisionResolver.TryGetFirstBlockingCell`. `Impassable` terrain blocks; `Normal` and `Costly` terrain are transparent. Endpoint cells are excluded from the blocker test.

Player A defenses are resolved through the shared combat path. Player B currently passes automatically; do not add AI defense choices incidentally during unrelated work.

## Mines and reconnaissance planes

Mines currently use `GridManager.DeployMine`, `GridManager.ResolveMinesFor`, `MatchState.mines`, `MineProfile`, and `MineTile`.

Reconnaissance planes use `PlaneProfile`, `PlaneUnit`, grid deployment/movement operations, and `MatchState.planes`.

Keep their state mutation in the established grid/match paths rather than input code. Plane sensor effects should continue through the fog/vision system.

## AI rules

The AI subsystem is event-driven from `TurnManager.PhaseChanged` for Player B.

Current behavior includes per-turn context, enemy sighting memory, fleet scan selection, multi-ship control, fog-aware attack selection, and movement planning toward observed/predicted enemies or board center.

Important distinction:

**AI decision-making is fog-aware; AI movement execution still uses legacy `GridManager.MoveShip`.**

When changing AI:

- do not bypass `CombatResolver`,
- do not inspect hidden enemy state when fog/memory should be used,
- do not add AI-only gameplay legality,
- prefer shared grid/fog/combat services over duplicate checks.

## Safe change routing

Inspect the actual source before editing; use this as the starting map.

| Desired change | Primary location |
| --- | --- |
| Board dimensions/tile creation | `GridManager.BuildGrid` |
| Reusable terrain authoring | `MapDefinition` |
| Runtime terrain query | grid terrain APIs |
| Weighted route calculation | `GridPathfinder` via `GridManager.CalculateMovementPath` |
| Footprint rotation/world cells | `FootprintUtil` |
| Placement collision rules | `GridManager.CanPlaceShip` |
| Legacy movement mutation | `GridManager.MoveShip` |
| Provisional movement | provisional movement state + `GridManager` preview/confirm path |
| Ship stats/sensors | `ShipData` |
| Add a ship card | `ShipType`, `ShipFactory`, `ShipData` |
| Initial fleets/anchors | `GridManager.Start`, `DeploymentService` |
| Phase order/player switching | `TurnManager`, `Phase` |
| Phase-driven fog lifecycle | `GridManager.HandlePhaseChanged`, `FogManager` |
| Stored fog state | `FogGrid` |
| Vision/cone/domain/LOS geometry | `VisionResolver` |
| Attack legality/damage | `CombatResolver.RequestAttack` / `SubmitDefense` via `GridManager.Combat` |
| Mine behavior | grid mine operations + `MatchState.mines` |
| Recon plane lifecycle | plane runtime types + grid operations + `MatchState.planes` |
| Player A prototype input | `TestShipController` |
| AI behavior | relevant AI controller/context/planner/scoring file + shared services |

## Editing rules

- Keep changes scoped to the requested behavior.
- Prefer extending an existing authority over creating a parallel system.
- Do not perform speculative re-architecture during a focused fix.
- Do not duplicate validation for caller convenience.
- Keep rendering/debug views read-only.
- Preserve current event-driven phase behavior where applicable.
- Do not edit generated Unity folders such as `Library/`, `Temp/`, `Logs/`, or `obj/`.
- Do not casually rewrite Unity `.meta` files or asset GUIDs.
- Treat scene, prefab, and serialized asset edits as high-impact; make them only when required.
- Preserve unrelated prototype/debug helpers unless the requested task targets them.
- Follow naming, formatting, serialization, and organization patterns in the surrounding source.

## Verification requirements

After code changes:

1. Inspect the final diff for accidental or unrelated edits.
2. Run existing relevant tests if present and supported by the environment.
3. Compile/check affected C# through available Unity/project tooling when practical.
4. For movement, occupancy, fog, combat, mine, plane, or turn changes, verify the relevant invariants above, not only the happy path.
5. If Unity runtime behavior was not actually exercised, say so.
6. Never claim runtime verification when only static inspection or compilation was performed.
7. If tooling is unavailable, report what was checked and what remains unverified.

Prefer tests around authoritative services/logic rather than duplicating controller behavior.

## Recommended reading by task

### Board / terrain / placement / movement

Start with `GridManager`, `FootprintUtil`, `GridPathfinder`, and the provisional movement types involved in the requested path.

### Fog / detection / LOS

Start with `FogManager`, `FogGrid`, `VisionResolver`, and the relevant `GridManager` integration.

### Combat

Start with `CombatResolver`, `ShipInstance`, relevant weapon/charge/domain profiles, and `VisionResolver` for terrain LOS.

### Turns / phase lifecycle

Start with `TurnManager`, `Phase`, and `GridManager.HandlePhaseChanged`.

### AI

Start with the relevant AI controller/context/planner/scoring files, then inspect the shared grid, fog, and combat APIs they use.

### Mines / planes

Start with their profile/runtime types, `MatchState`, and the `GridManager` operations that mutate their state.

For the broader dependency map, subsystem responsibilities, runtime flow, implementation notes, and high-risk change areas, read [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).
