# Admirals MVP Architecture

This document describes the current runtime architecture of the Admirals MVP Unity prototype.

The source under `Assets/Scripts` is authoritative. If this document disagrees with the implementation, follow the implementation and update this document when practical.

This document is descriptive, not a mandate to preserve every prototype choice forever. Architectural changes should still be intentional and should update ownership boundaries rather than accidentally creating duplicate authorities.

---

## 1. System overview

Admirals is currently a local, turn-based naval combat prototype built around a rectangular grid.

Default board size:

```text
30 x 15
```

Ships are plain C# runtime objects rather than ship `GameObject` or `ScriptableObject` instances.

The major runtime systems are:

| System | Main types | Responsibility |
| --- | --- | --- |
| Grid | `GridManager`, `Tile`, `FootprintUtil` | Board construction, occupancy, placement, movement, board operations |
| Terrain | `MapDefinition`, `TerrainType`, `Tile` | Reusable terrain authoring and runtime terrain data |
| Pathfinding | `GridPathfinder`, `MovementPathResult` | Read-only weighted 8-direction movement path calculations |
| Provisional movement | `ProvisionalMovementState`, `GridManager` | Snapshot, preview, and atomic movement confirmation |
| Ships | `ShipInstance`, `ShipFactory`, `ShipData`, `ShipType` | Runtime ship state and hardcoded ship definitions |
| Match | `MatchState`, `PlayerState`, `DeploymentService` | Player/fleet ownership and live runtime collections |
| Turns | `TurnManager`, `Phase`, `PlayerId` | Active player and phase progression |
| Fog of War | `FogManager`, `FogGrid`, `FogState`, `VisionResolver` | Per-player knowledge, detection, and LOS geometry |
| Combat data | `WeaponProfile`, `DefenseProfile`, `RollTier`, `ChargeState`, `DomainType`, `VisionLayer` | Combat/sensor definitions and runtime charge state |
| Combat behavior | `CombatResolver` via `GridManager.Combat` | Attack legality and resolution |
| Human input | `TestShipController` | Prototype Player A command/input path |
| Visualization | `GridView` | Gizmo rendering of board and runtime state |
| AI | AI controller, context, planners, memory, scoring | Player B decisions and phase execution |
| Mines | `MineProfile`, `MineTile`, grid/match operations | Staging deployment, contact resolution, recharge |
| Recon planes | `PlaneProfile`, `PlaneUnit`, grid/match operations | Carrier plane deployment, movement, fuel, vision |

---

## 2. Architectural ownership

The project intentionally separates state ownership from geometry, orchestration, and visualization.

### 2.1 Match ownership

`MatchState` is authoritative for which runtime entities exist and which player owns them.

It owns or exposes:

- Player A and Player B state.
- Fleet rosters/live ship collections.
- Shared runtime collections currently used for systems such as mines and planes.

`PlayerState` represents per-player fleet/runtime ownership.

A controller or view may reference these objects, but should not create a competing source of truth for whether a ship is alive or who owns it.

### 2.2 Grid ownership

`GridManager.tiles` is authoritative for board occupancy by coordinate.

Placement is derived from a ship's:

- `anchor`
- `rotationDegrees`
- `footprintOffsets`

`FootprintUtil` converts those values into occupied world cells.

`GridManager` is therefore the primary mutation boundary for:

- placing ships,
- moving ships,
- validating candidate placement,
- board interactions,
- phase-driven board state changes,
- several staging-system operations.

### 2.3 Fog ownership

Each `FogGrid` stores one player's knowledge.

A `FogGrid` does not own ships and should not calculate board geometry.

`FogManager` owns the player fog grids and coordinates their lifecycle:

- passive recomputation,
- active scan application,
- active-mark clearing,
- contact cleanup.

`VisionResolver` is stateless behavior for detection/LOS geometry.

This separation matters because fog stores *knowledge about the board*, not the board itself.

### 2.4 Combat ownership

`CombatResolver` is the authoritative attack service.

Callers request combat through the established `GridManager.Combat` path rather than independently checking and applying all combat rules.

This keeps turn gating, fog gating, range, domain checks, terrain LOS, damage, charge consumption, and destruction cleanup in one resolution path.

### 2.5 View/input ownership

`GridView` renders runtime state and Gizmos. It reads state but does not own or mutate gameplay rules.

`TestShipController` is a prototype input adapter. It should translate input into requests to authoritative systems rather than reproduce board, movement, fog, or combat rules.

The AI follows the same principle: it may decide *what it wants to do*, while shared gameplay services decide whether the action is legal and apply it.

---

## 3. Runtime initialization

The current high-level startup flow is:

```text
GridManager.Awake()
  -> BuildGrid()
  -> create FogManager

GridManager.Start()
  -> create PlayerState(PlayerA, roster)
  -> create PlayerState(PlayerB, roster)
  -> create MatchState(playerA, playerB)
  -> bind Player A and B to authored neutral MapDefinition zones
  -> build Player B's deterministic legal draft
```

`DeploymentService` holds unoccupied drafts by player and roster slot. The Player A prototype controller places and rotates its roster, while the local Player B setup searches legal positions through the same API. Zone regions are authored in `MapDefinition` and assigned to players at match startup.

Both full formations must be confirmed before any draft becomes a live ship or occupies a tile. Confirmation revalidates both formations, commits them together, and starts one Player A Move event. `TurnManager.AdvancePhase` is blocked until that start event.

---

## 4. Turn and phase flow

The phase order is:

```text
Move
  -> Staging
  -> Search
  -> Battle
  -> End
  -> next player's Move
```

Expanded:

```text
Player A Move
  -> Player A Staging
  -> Player A Search
  -> Player A Battle
  -> Player A End
  -> Player B Move
  -> Player B Staging
  -> Player B Search
  -> Player B Battle
  -> Player B End
  -> Player A Move
```

`TurnManager.AdvancePhase()`:

1. asks generic `PhaseAdvanceRequested` guards whether the current phase may end,
2. changes `CurrentPhase` when none reject,
3. switches `CurrentPlayer` only after `End`,
4. logs the new turn/phase state,
5. raises `PhaseChanged`.

Subscribers receive the **new** phase.
The grid guard rejects an invalid Player A Move confirmation before Staging;
the turn manager does not know the movement rules.

The intended dependency direction is one-way:

```text
TurnManager
    |
    | PhaseChanged
    v
phase-aware subscribers
    |
    +--> GridManager
    +--> AI orchestration
    +--> other phase-aware runtime systems
```

`TurnManager` should not need to understand fog, ships, mines, planes, grid occupancy, or combat implementation details.

---

## 5. GridManager phase reactions

`GridManager.HandlePhaseChanged()` is a major integration point.

Conceptually:

```text
Move
  -> snapshot provisional movement states for current player's ships

Staging
  -> confirm provisional ship movement atomically
  -> snapshot plane positions
  -> allow staging actions such as mines/planes through their explicit commands

Search
  -> refresh passive fog
  -> update plane deployment lifecycle flags
  -> active scan remains an explicit player/AI action

Battle
  -> reset ship attack-state tracking for the Battle phase

End
  -> clear active fog marks
  -> reset attack state as currently wired
  -> tick recharge/fuel lifecycle
```

This event-driven flow should be preferred over adding `Update()` polling for phase transitions.

---

## 6. Grid, terrain, and placement

### 6.1 Tile model

Each `Tile` represents a board coordinate and contains runtime board information such as:

- current ship occupant,
- loaded terrain type.

The tile grid is owned by `GridManager`.

### 6.2 Terrain model

`MapDefinition` is the reusable terrain authoring surface.

`TerrainType` distinguishes the current terrain categories:

- `Normal`
- `Costly`
- `Impassable`

Runtime terrain queries are exposed through the grid layer.

Terrain affects movement/path cost and line-of-fire according to the specific subsystem rules.

### 6.3 Placement

`GridManager.CanPlaceShip` validates a candidate footprint.

A valid placement must fit within the grid and must not collide with another ship's occupied cells.

Placement validation should remain centralized so controllers, AI, deployment, and future UI do not drift into different legality rules.

---

## 7. Footprint geometry

Ship placement uses an anchor plus a list of local footprint offsets.

`FootprintUtil.RotateOffsets` applies the project's clockwise quarter-turn convention:

```text
0°   (x, y)
90°  (-y, x)
180° (-x, -y)
270° (y, -x)
```

`GetWorldCells` applies the rotated offsets to the ship anchor.

Any change to rotation semantics is high-impact because it can affect:

- deployment,
- occupancy,
- collision checks,
- movement,
- range calculations,
- line of fire,
- rendering.

---

## 8. Movement architecture

The codebase currently contains two movement concepts that should not be conflated.

### 8.1 Legacy movement

`GridManager.MoveShip` is the legacy direct movement path.

It uses distance-based validation from `anchorAtTurnStart`, then validates placement before mutating occupancy.

AI movement still executes through this path.

### 8.2 Weighted path calculation

`GridPathfinder` and `MovementPathResult` perform read-only weighted 8-direction path calculations.

They calculate; they do not move ships and do not mutate grid occupancy.

### 8.3 Provisional movement

Player-facing movement has a provisional flow.

`ProvisionalMovementState` stores movement snapshots and candidate states.

The current behavior includes:

- preview through the `GridManager` movement preview path,
- retention of the last valid provisional state,
- rejection of invalid candidate previews without replacing that valid state,
- cancellation through Escape/C,
- confirmation through Space before leaving Move,
- atomic confirmation of the set of valid candidate states.

Enter is not the movement commit key.

The provisional flow is designed to prevent invalid terrain, footprint, or occupancy states from reaching commit.

### 8.4 Movement debt

The main known movement architecture debt is that AI planning and player movement do not yet share the same execution semantics.

AI planners are modular, but AI movement still commits through legacy `MoveShip` rather than provisional/Dijkstra movement.

---

## 9. Fog of War

### 9.1 Knowledge model

Fog knowledge is layered:

```text
Unknown < Marked < Identified
```

Meanings:

- `Unknown`: no current evidence.
- `Marked`: a detected contact/cell without necessarily revealing full contents.
- `Identified`: absolute vision has revealed the cell contents.

`FogGrid.Upgrade` preserves the strongest knowledge when multiple layers overlap.

### 9.2 Per-player state

Fog state is stored independently for Player A and Player B.

This prevents one player's observations from becoming global board knowledge.

### 9.3 Passive detection

`FogManager` rebuilds both players' passive detection after deployment commits and each committed ship move, rotation, domain change, plane deployment or move, plane removal, and ship destruction. Search also refreshes it. Ship and plane previews leave live vision unchanged. The Wolf's passive Absolute layer works in both domains. Active marks remain until End unless destruction clears a contact.

The exact geometry/domain filtering is handled through `VisionResolver` and the fog manager rather than being stored as geometry inside `FogGrid`.

### 9.4 Active scanning

Active scanning uses a preview/confirmation flow and is not automatically fired by entering Search.

The current prototype has one fleet active scan limit per player per Search phase.

Active results are temporary.

### 9.5 Active mark lifecycle

Active marks are cleared at `End`.

This lifecycle is important: moving the clear earlier/later changes what information remains attackable/visible across phases.

### 9.6 Attack gating

The current combat gate treats both `Marked` and `Identified` as known.

For a multi-cell target, knowledge of at least one target cell is currently enough for target-known gating.

### 9.7 Visualization

`GridView` distinguishes fog states in its prototype visualization:

- `Identified`: enemy ship cells are rendered as identified ship geometry.
- `Marked`: contact indicators are rendered without revealing the full enemy ship.
- `Unknown`: enemy units remain hidden.

A developer `revealAllInFog` toggle can expose units for testing.

This is a visualization/debug feature and does not replace authoritative fog state.

---

## 10. Vision and line of sight

`VisionResolver` is the shared stateless geometry layer for detection and LOS-related calculations.

Combat terrain line-of-fire reuses `VisionResolver.TryGetFirstBlockingCell`.

This intentionally aligns vision LOS and attack LOS around the same supercover Bresenham/corner-adjacency policy.

For current attack line-of-fire:

- `Impassable` terrain blocks.
- `Normal` terrain does not block.
- `Costly` terrain does not block.
- attacker and target endpoint cells are excluded from the blocker test.

An attack can proceed if at least one in-range attacker-cell/target-cell pair has a clear line.

---

## 11. Ship runtime model

`ShipInstance` stores runtime state for a ship, including concepts such as:

- identity/type,
- owner,
- placement,
- health,
- armor,
- current domain,
- weapon/sensor/defense profile references,
- charge/ammo runtime state,
- per-Battle attack state.

`ShipFactory`, `ShipData`, and `ShipType` provide the current hardcoded ship-card creation flow.

Ship objects should carry ship state; they should not become the service responsible for board authority, fog geometry, or attack resolution.

---

## 12. Combat architecture

### 12.1 Combat entry point

`CombatResolver` is reached through `GridManager.Combat`.

The resolver performs both legality checks and resolution so that a caller cannot easily apply only part of the rules.

### 12.2 Current resolution sequence

The documented current sequence is:

1. Reject a dead target.
2. Verify Battle phase and attacking player's turn.
3. Reject an attacker that already attacked this Battle phase.
4. Verify weapon/charge readiness.
5. Verify target-domain compatibility.
6. Compute Chebyshev distance across all occupied-cell pairs.
7. Require at least one pair within weapon range.
8. Require the target to be known in the attacker's fog.
9. Require clear terrain line-of-fire for at least one in-range pair.
10. `RequestAttack` offers ready defenses matching the target's current domain. Player A's response pauses resolution; Player B passes automatically.
11. `SubmitDefense` spends and rolls the selected defense first. A successful defense avoids the weapon roll and damage; Crash Dive also submerges the ship and locks its next Move.
12. On a pass or failed defense, roll the weapon and apply armor.
13. Spend the attack opportunity and finite ammo once on every resolved attack, including an avoided hit.
14. If the target reaches zero health, remove it from occupancy and live-ship state and clear its fog/contact marks. `AttackFinalized` reports the outcome.

### 12.3 Armor

Current armor reduction:

```text
effectiveDamage = round(rawDamage * (1 - armor * 0.0015))
```

Rules:

- a miss remains `0`,
- a non-zero reduced result is clamped to at least `1`.

### 12.4 Return semantics

`RequestAttack` returns `Rejected`, `PendingDefense`, or `Finalized`. `SubmitDefense` returns false for an invalid response without changing the pending attack and returns the finalized outcome for a valid choice or pass.

### 12.5 Charge/ammo

Finite weapon charge/ammo is decremented on resolved use.

Recharge state is maintained separately through `ChargeState` lifecycle behavior.

### 12.6 Destruction

When a target is destroyed, the current flow removes it from:

- grid occupancy,
- the owner's live ship list,

and clears associated active/passive sensor contact marks through fog cleanup.

### 12.7 Defensive measures

`CombatResolver` exposes a defender-safe pending view with target and eligible defense IDs. The keyboard adapter submits a choice or pass; it does not roll or mutate combat. Player B currently passes automatically. A successful Crash Dive sets SubSurface and prevents movement or rotation in the owner's next Move phase.

---

## 13. Mines

Mine-related runtime types include:

- `MineProfile`
- `MineTile`

Relevant state/operations include:

- `GridManager.DeployMine`
- `GridManager.ResolveMinesFor`
- `MatchState.mines`

The current prototype includes:

- deployment during Staging,
- contact detonation,
- documented `600` contact damage,
- a `2`-turn recharge lifecycle for the mine capability.

Mine behavior should remain integrated with authoritative grid/match state instead of being implemented solely in input code.

---

## 14. Carrier reconnaissance planes

Reconnaissance planes use:

- `PlaneProfile`
- `PlaneUnit`
- deployment/undeployment grid operations,
- `MatchState.planes`.

The current plane model includes:

- carrier deployment,
- launch range,
- sortie usage,
- Staging-phase movement,
- fuel lifecycle,
- absolute halo vision.

Current staging behavior includes:

- a freshly deployed plane is movement-locked for that deployment turn,
- a freshly deployed plane can be undeployed/refunded through the current input flow,
- later Staging phases may preview movement separately from the plane's live position; confirmation commits the position and refreshes fog, while canceling discards the preview.

Plane state is runtime state rather than a replacement for fog ownership; its sensor effect is applied through the fog/vision system.

---

## 15. AI architecture

The AI is event-driven from `TurnManager.PhaseChanged` for Player B.

The original project guide contained contradictory shorthand about whether the AI was “fog-aware.” The more detailed implementation description establishes the current model:

**AI decisions are fog-aware; AI movement execution still uses a legacy movement path.**

### 15.1 Orchestration

The AI controller reacts to Player B phase changes rather than polling every frame for turn progress.

### 15.2 Turn context

`AiTurnContext` captures a per-turn snapshot of relevant AI state, including living AI ships and fleet scan usage.

### 15.3 Enemy memory

`AiEnemyMemory` stores observed enemy information across turns.

The documented behavior includes:

- last known coordinates,
- observation recency,
- predicted positions as sightings age.

This lets AI reasoning use remembered information rather than raw hidden enemy state.

### 15.4 Search

`AiActiveScanner` evaluates candidate sensor ships/cone orientations and can execute up to the current fleet Search-phase scan limit.

### 15.5 Movement

`AIMovementPlanner1` evaluates movement candidates toward:

- observed/predicted enemy positions, or
- board center when no enemy has been observed.

Known debt: execution still goes through `GridManager.MoveShip`.

### 15.6 Battle

`AIAttackPlanner` evaluates legal attacks against targets known through Player B's fog/memory context.

It respects shared combat legality, including the one-attack-per-ship rule.

`AIScoring` provides scoring logic for candidate scans, movements, and attacks.

The AI should call shared authoritative services for action execution rather than implementing AI-only legality.

---

## 16. Deployment architecture

`GridManager.Start` creates the two player states from the current roster setup.

`DeploymentService` validates full rotated footprints against the assigned map zone, passable terrain, occupancy, and other drafts. Callers use player IDs, zone IDs, and roster slots. The local deterministic Player B routine is in this service, outside the AI subsystem. Draft reads are scoped to the requesting player until deployment completes.

---

## 17. File-by-file responsibility map

The exact repository should be inspected before editing because files may evolve, but the current operating model is:

### `Assets/Scripts/Grid/`

#### `GridManager.cs`

Scene-level integration/controller for:

- board occupancy,
- placement,
- movement,
- match setup,
- phase-driven grid/fog lifecycle,
- several staging-system operations,
- combat service access.

#### `MapDefinition.cs`

Reusable map definition containing dimensions and sparse terrain entries.

#### `TerrainType.cs`

Terrain category definition.

#### `GridView.cs`

Read-only visualization for:

- board/terrain,
- zones,
- sensor cones/contacts,
- mines,
- planes,
- ships,
- fog-aware debug rendering.

Obsolete `DrawDebugHalos` and `DrawMovementRanges` helpers were removed; active cones and other current Gizmo surfaces remain intentionally available.

#### `FootprintUtil.cs`

Rotation and footprint world-cell calculations.

#### `Tile.cs`

Runtime board coordinate and its occupancy/terrain data.

#### `TestShipController.cs`

Prototype Player A input adapter.

### `Assets/Scripts/Ships/`

#### `ShipInstance.cs`

Runtime mutable state for a ship.

#### `ShipFactory.cs`, `ShipData.cs`, `ShipType.cs`

Hardcoded ship definitions and creation flow.

### `Assets/Scripts/Match/`

#### `PlayerState.cs`

Per-player fleet/live-ship state.

#### `MatchState.cs`

Top-level match ownership and aggregated runtime collections.

#### `DeploymentService.cs`

Fleet construction and initial placement orchestration.

### `Assets/Scripts/Turns/`

#### `TurnManager.cs`

Current player/current phase and phase progression.

#### `Phase.cs`, `PlayerId.cs`

Shared turn/player identity enums.

### `Assets/Scripts/FogOfWar/`

#### `FogState.cs`

Knowledge ordering: `Unknown`, `Marked`, `Identified`.

#### `FogGrid.cs`

Stored knowledge for one player.

#### `FogManager.cs`

Fog lifecycle and passive/active recomputation.

#### `VisionResolver.cs`

Stateless vision/LOS geometry and domain filtering.

#### `ActiveScanPreviewState.cs`

Active scan preview direction/pivot/confirmation state.

### `Assets/Scripts/Combat/`

#### `CombatResolver.cs`

Attack legality and d20 resolution.

#### `DomainType.cs`

Current domain categories include `Surface`, `SubSurface`, and `Both`.

#### `VisionLayer.cs`

Sensor layer definition including range, shape, domain, reveal type, and active/passive status.

#### `WeaponProfile.cs`

Weapon definition, including damage/ammo behavior.

#### `DefenseProfile.cs`

Defense/save data and side-effect placeholders.

#### `RollTier.cs`

d20 tier model.

#### `ChargeState.cs`

Runtime use/ammo/recharge state.

#### `PlaneProfile.cs`, `PlaneUnit.cs`

Carrier reconnaissance plane definition/runtime state.

#### `MineProfile.cs`, `MineTile.cs`

Mine definition/runtime state.

### `Assets/Scripts/AI/`

The documented AI subsystem includes:

- `AiController.cs`
- `AiTurnContext.cs`
- `AiEnemyMemory.cs`
- `AiActiveScanner.cs`
- `AIMovementPlanner1.cs`
- `AIAttackPlanner.cs`
- `AIScoring.cs`

Use the actual source as authority for exact file/class casing.

---

## 18. Dependency principles

Preferred dependency direction:

```text
Input / AI / Visualization
          |
          v
 Authoritative operations
(GridManager / CombatResolver / FogManager)
          |
          v
     Runtime state
(MatchState / Grid tiles / FogGrid / ShipInstance)
```

Geometry/utility services such as `FootprintUtil`, `GridPathfinder`, and `VisionResolver` should remain reusable and should not become hidden state owners.

Avoid this pattern:

```text
Controller A -> duplicate legality
Controller B -> different duplicate legality
AI           -> third duplicate legality
```

Prefer:

```text
Controller A --\
Controller B ----> shared authoritative service
AI ----------/
```

---

## 19. Current prototype boundaries

The following are intentionally still prototype/incomplete areas:

- no win-condition/game-over flow,
- no Player B defense selection strategy or LAN defense adapter,
- repair ship not implemented,
- AI movement execution still legacy,
- fog presentation is Gizmo/debug oriented,
- active scan limit is fleet-level per player/Search phase,
- input and orchestration still include temporary prototype assumptions.

These should be treated as explicit backlog/architecture boundaries, not as permission to fill in unspecified rules during unrelated work.

---

## 20. High-risk architectural changes

Changes in these areas should be reviewed across multiple systems because they have broad effects:

### Phase order/lifecycle

Can affect:

- fog recomputation,
- active-mark lifetime,
- attacks,
- recharge,
- plane fuel,
- AI actions,
- movement confirmation.

### Footprint geometry

Can affect:

- placement,
- collisions,
- deployment,
- movement,
- targeting range,
- line of fire,
- rendering.

### Fog knowledge semantics

Can affect:

- visibility,
- attack legality,
- AI targeting,
- scan behavior,
- contact rendering.

### Occupancy mutation

Can affect:

- placement correctness,
- movement,
- mine contact,
- combat targeting,
- destruction cleanup.

### Shared LOS

Can affect both:

- sensor visibility,
- combat line-of-fire.

### Ship destruction

Must remain synchronized across:

- health,
- tile occupancy,
- live ship lists,
- fog/contact state.

---

## 21. Recommended extension points

Use the narrowest existing owner for a change.

| Change | Preferred extension point |
| --- | --- |
| Tile/grid construction | `GridManager.BuildGrid` |
| Terrain authoring | `MapDefinition` |
| Terrain query | grid terrain APIs |
| Weighted route rules | `GridPathfinder` |
| Placement rules | `GridManager.CanPlaceShip` |
| Footprint geometry | `FootprintUtil` |
| Provisional movement | provisional state + `GridManager` preview/confirm flow |
| Phase progression | `TurnManager` |
| Phase-driven integration | `GridManager.HandlePhaseChanged` / relevant subscriber |
| Fog storage semantics | `FogGrid` |
| Fog lifecycle | `FogManager` |
| Vision/cone/LOS geometry | `VisionResolver` |
| Attack legality/resolution | `CombatResolver` |
| Ship definitions | `ShipData` / `ShipFactory` / `ShipType` |
| AI decision policy | AI planners/scoring/memory |
| Player input mapping | `TestShipController` |
| Rendering/debug visualization | `GridView` |

---

## 22. Architectural verification checklist

When changing architecture-sensitive behavior, check the relevant items:

- Is there still exactly one authority for the state being changed?
- Does occupancy agree with ship placement?
- Does match live-ship state agree with destroyed/alive state?
- Does fog remain per-player?
- Are `Marked`/`Identified` semantics preserved unless intentionally changed?
- Are active marks still cleared at the intended phase boundary?
- Do AI and player actions go through the same legality/resolution services?
- Does `TurnManager` remain decoupled from subsystem implementation?
- Are views still read-only?
- Are rejected provisional moves non-mutating?
- Does a destruction path clean up occupancy and contacts?
- Do vision LOS and combat LOS still intentionally share geometry if either was changed?
- Was the Unity runtime actually exercised, or only statically inspected/compiled?

---

## 23. Relationship to `AGENTS.md`

`AGENTS.md` is the concise operational contract for coding agents:

- source-of-truth rules,
- architecture boundaries,
- critical invariants,
- edit routing,
- verification behavior.

This document is the deeper map of how the current prototype fits together.

When the two disagree, the order of authority is:

```text
current source code
    >
AGENTS.md / docs that match the source
    >
older plans, status notes, or stale documentation
```
