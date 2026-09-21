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
- `GridManager` owns the authoritative tile occupancy and currently also owns
  movement, combat resolution, phase reaction, and debug Gizmos.
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

- `AIController` still targets Player A's first ship using global knowledge.
- The AI still controls only Player B's first ship.
- Active search is automatic and has no player-selected search action.
- Fog state is internal runtime state; there is no player-facing visibility UI.
- Armor, defense rolls, ammo consumption, recharge, and defense side effects are
  not implemented.
- There is no win-condition/game-over flow.
- Temporary fog logs and verification Gizmos remain.

---

## 1. Architecture overview

### Major systems

| System | Main implementation | Responsibility |
| --- | --- | --- |
| Grid | `GridManager`, `Tile`, `FootprintUtil` | Builds the board, stores occupancy, validates placement, and moves ships. |
| Ships | `ShipInstance`, `ShipFactory`, `ShipData`, `ShipType` | Defines runtime ship state and builds hardcoded ship cards. |
| Match | `MatchState`, `PlayerState`, `DeploymentService` | Owns both players, their fleet rosters, live ships, and initial deployment. |
| Turns | `TurnManager`, `Phase`, `PlayerId` | Advances the phase sequence and identifies the acting player. |
| Fog of War | `FogManager`, `FogGrid`, `FogState`, `VisionResolver` | Computes each player's detected enemy cells and stores their knowledge. |
| Combat data | `WeaponProfile`, `DefenseProfile`, `RollTier`, `ChargeState`, `DomainType`, `VisionLayer` | Stores weapon, defense, roll-table, charge, domain, and vision definitions. |
| Combat behavior | `GridManager.ResolveAttack` | Performs the current attack validation and d20 damage resolution. |
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
    ├── Staging → Fog.RecomputeAllPassive(match)
    ├── Search  → Fog.RunActiveSearch(CurrentPlayer, match)
    └── End     → Fog.ClearAllActiveMarks()
```

The event is deliberately one-way. `TurnManager` knows only that it raises
`PhaseChanged`; it does not know about fog, ships, or the grid.

---

## 2. File-by-file map

### `Assets/Scripts/Grid/`

#### `GridManager.cs` — `GridManager : MonoBehaviour`

The scene-level coordinator and current authority for board occupancy,
placement, movement, attack resolution, phase-driven fog updates, and
prototype visualization. It depends on almost every other system.

Important members:

- `width`, `height`, `cellSize`: serialized board settings.
- `tiles`: private coordinate-to-`Tile` dictionary.
- `match`: the runtime `MatchState`.
- `turnManager`: serialized event source.
- `Fog`: the `FogManager` created in `Awake`.
- `BuildGrid`, `GetTile`, `IsInBounds`, and `IsOccupied`: board access.
- `CanPlaceShip`, `PlaceShip`, `RemoveShip`, `MoveShip`: placement/movement
  path.
- `ResolveAttack`, `FindChargeState`, `IsTargetKnown`: current combat path.
- `HandlePhaseChanged`: phase-to-fog integration.
- `OnDrawGizmos` and helper methods: grid, zone, cone, and halo debug drawing.

#### `FootprintUtil.cs` — `FootprintUtil`

Stateless grid geometry utility. `RotateOffsets` applies the project's
clockwise 0/90/180/270-degree convention to local footprint offsets.
`GetWorldCells` rotates every offset and adds the ship anchor.

#### `Tile.cs` — `Tile`

Plain data object for one grid coordinate. `Position` is immutable after
construction, `Occupant` is the current ship reference, and `IsValid` is a
reserved flag for future non-rectangular maps. The current rectangular
builder
does not use `IsValid`.

#### `TestShipController.cs` — `TestShipController : MonoBehaviour`

Temporary Player A input controller. It advances phases with Space, cycles
Player A's live ships with Tab, selects weapon slots with 1/2/3, moves and
rotates during `Move`, toggles domain with D, and requests attacks with a
mouse click during `Battle`.

It delegates legality to `GridManager.MoveShip` and
`GridManager.ResolveAttack`; it does not implement a second combat or movement
validation path.

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

- `BuildWolfClass`: two-cell ship; movement range 10 for testing; three weapons,
  three defenses, passive sonar/absolute vision, and active search sonar.
- `BuildAthenaClass`: three-cell surface ship; three weapons, two defenses,
  passive absolute radar/sonar, and active sub-surface search sonar.
- `BuildSwordFishClass`: two-cell data definition with one weapon and no
  defenses/vision. It exists in the factory but is not in the current
  deployment roster.

Each builder assigns definitions, then calls `InitializeCharges`.

#### `ShipType.cs` — `ShipType`

The card identifiers `WolfClass`, `AthenaClass`, and `SwordFishClass`.

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
player's live list.

Current limitation: `DeployFleet` calls `PlaceShip` directly rather than first
calling `CanPlaceShip`. That is listed as Milestone 5 housekeeping and is not
yet corrected.

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

`Describe` is explicitly temporary debug code.

#### `FogManager.cs` — `FogManager`

Owns Player A and Player B `FogGrid` instances. It performs passive
recomputation on both sides, runs active scans for the acting player, and
clears both active layers. It delegates detection shape/domain rules to
`VisionResolver`.

#### `VisionResolver.cs` — `VisionResolver`

Stateless geometry and filtering implementation. It handles passive halos,
active cones, living-ship checks, `detects` domain filtering, and
`onlyWhileSurfaced`.

---

### `Assets/Scripts/Combat/`

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

Mutable runtime uses for one weapon or defense slot. `remaining == -1` means
infinite. `IsReady` is true when recharge time is zero and remaining is not
zero. Spending and recharge logic are not implemented.

---

### `Assets/Scripts/AI/`

#### `AIController.cs` — `AIController : MonoBehaviour`

Temporary Player B controller. It acts once per phase by tracking
`actedThisPhase`, moves `gridManager.ObstructionShip` toward
`gridManager.TestShip`, and selects the highest expected-value weapon that
passes its local charge/domain/range checks. It ultimately calls
`GridManager.ResolveAttack`.

It does not use Player B's `FogGrid`, does not select among all living ships,
does not scan during Search, and does not move toward the center when it has no
known target. Those are planned, not implemented.

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
├── state: currentDomain
├── definitions: weapons, defenses, visionLayers
└── runtime charges: weaponCharges, defenseCharges
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

- `passive`: rebuilt on every `Staging` transition.
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

The current attack gate treats both `Marked` and `Identified` as known. No
consumer currently behaves differently based on the two states beyond how they
are produced.

### Passive detection

Passive detection is triggered when `TurnManager` changes to `Phase.Staging`.
The actual chain is:

```text
TurnManager.AdvancePhase()
  → PhaseChanged(Phase.Staging)
  → GridManager.HandlePhaseChanged(Phase.Staging)
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
current ship positions at every `Staging`. A previous position is forgotten
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

There is no line-of-sight or obstacle blocking. `Tile.IsValid` is not consulted
by the resolver, and off-board cone cells simply fail to match enemy cells.

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

Active detection is triggered automatically on `Phase.Search`:

```text
TurnManager.AdvancePhase()
  → PhaseChanged(Phase.Search)
  → GridManager.HandlePhaseChanged(Phase.Search)
  → FogManager.RunActiveSearch(CurrentPlayer, match)
```

`RunActiveSearch` gets the acting player's fog and the opposing live ship list.
It iterates every ship in the acting player's live list and every non-passive
vision layer. Each detected cell is written with `MarkActive(cell, Marked)`.
Active scans deliberately write `Marked` even if the layer's definition says
`Absolute`; the milestone rule is that active search reveals presence, not
identity.

The active layer is cleared when the phase becomes `End`:

```text
Phase.End
  → GridManager.HandlePhaseChanged(End)
  → FogManager.ClearAllActiveMarks()
  → playerAFog.ClearActiveMarks()
  → playerBFog.ClearActiveMarks()
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
Player A Staging       passive fog recompute
    ↓
Player A Search        active scan
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

There is no implemented Staging action beyond passive fog, no Search input
selection, and no End-phase cooldown or win check.

---

## 6. Combat and Fog interaction

### Human input path

```text
TestShipController.Update()
  → HandleAttackInput(ship)
  → clicked tile's Occupant
  → GridManager.ResolveAttack(attacker, target, weapon)
```

The controller performs only input-level checks: clicked tile exists, has an
occupant, and is not friendly; selected weapon index is valid. The authoritative
attack path is `ResolveAttack`.

### `GridManager.ResolveAttack`

The current validation order is:

1. Reject a dead target.
2. Find the attacker's `ChargeState` by weapon id and require `IsReady`.
3. Require the weapon target domain to match the target domain or be `Both`.
4. Calculate the minimum Chebyshev distance from `attacker.anchor` to any target
   occupied cell.
5. Reject if weapon range is less than that distance.
6. Call `IsTargetKnown`.
7. Roll one d20 with `RollWeapon`.
8. Subtract the selected tier's damage from target health.
9. If health reaches zero, remove the target from grid occupancy and its
   owner's live `ships` list.

The current range calculation uses `attacker.anchor`, not the nearest
attacker occupied cell. This is a known housekeeping item and is important when
extending range behavior.

`IsTargetKnown` gets the attacker's fog grid and returns true if any target
occupied cell has a non-`Unknown` state. It does not require every target cell
to be known. This means a multi-cell target can be attacked when only one of its
cells is detected.

`ResolveAttack` does not currently consume ammo, apply armor, run defenses, or
reveal a target after a successful attack. It returns true when resolution was
performed, including a d20 miss with zero damage.

### AI attack path

```text
AIController.Update()
  → DecideAttack(aiShip, enemyShip)
  → CanFire for each weapon
  → ExpectedValue for legal weapons
  → GridManager.ResolveAttack(aiShip, enemyShip, best)
```

`CanFire` duplicates charge/domain/range checks for weapon selection, but the
actual final validation remains in `ResolveAttack`. It currently omits the fog
check because the AI is not yet fog-aware; `ResolveAttack` still applies the
fog gate when the AI calls it.

---

## 7. Destroyed ship behavior

When a resolved attack reduces `currentHealth` to zero:

```text
ResolveAttack
  → Debug.Log destroyed message
  → RemoveShip(target)
       clears every tile whose Occupant is target
  → match.GetPlayer(target.owner).ships.Remove(target)
```

Removing the ship from the live fleet has several downstream effects:

- Future passive and active fog loops no longer include it as a source or
  target.
- `MatchState.AllShips()` no longer returns it.
- The grid no longer reports it as an occupant.
- `TestShipController` clamps its selected Player A index against the smaller
  list.

There is no separate destroyed flag and no win-condition check. A caller that
still holds a reference can see `currentHealth <= 0`, but normal match systems
use the live list.

---

## 8. AI implementation

### CURRENT IMPLEMENTATION

`AIController.Update`:

1. Requires serialized `gridManager` and `turnManager`.
2. Returns unless `CurrentPlayer` is Player B.
3. Resets `actedThisPhase` when the phase/player changes.
4. On a new Move phase, snapshots `ObstructionShip.anchorAtTurnStart`.
5. Uses only `gridManager.ObstructionShip` and `gridManager.TestShip`.
6. During Move, moves one Chebyshev step toward the target's anchor.
7. During Battle, scores every weapon by average d20 damage among its tiers,
   after charge/domain/range checks, and fires the highest score.
8. Does nothing during Search.

The first ship properties on `GridManager` are simply `playerA.ships[0]` and
`playerB.ships[0]`, so the current AI controls only Player B's first deployed
ship (the Wolf).

### PLANNED / NOT YET IMPLEMENTED

The Milestone 5 plan calls for:

- finding the nearest enemy with a known cell in the AI's own `FogGrid`;
- not chasing or attacking hidden targets;
- moving toward map center when no target is known;
- acting for every living AI ship;
- using Search to build knowledge.

None of those changes should be documented as current behavior until
`AIController` is changed and verified.

One additional known issue is that `actedThisPhase` is set after the decision.
The planned housekeeping item is to set it before acting so an exception cannot
cause repeated frame attempts.

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
the center of the map from the opposite side. The current code assumes the
hardcoded anchors are legal; it does not call `CanPlaceShip` before placement.

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
state. Passive fog is not computed until the first transition into Staging.

### Flow B: passive fog update

```text
AdvancePhase: Move → Staging
  → PhaseChanged(Staging)
  → GridManager.HandlePhaseChanged
  → FogManager.RecomputeAllPassive
  → reset Player A passive dictionary
  → resolve Player A passive layers against Player B live ships
  → reset Player B passive dictionary
  → resolve Player B passive layers against Player A live ships
```

Each result is stored by enemy occupied cell, not as a full-board coverage map.

### Flow C: active Search

```text
AdvancePhase: Staging → Search
  → PhaseChanged(Search)
  → RunActiveSearch(CurrentPlayer)
  → iterate acting player's live ships
  → iterate non-passive layers
  → VisionResolver builds cone/halo and filters domains
  → MarkActive(cell, Marked)
```

The current profiles use cone-shaped active layers. Active marks remain through
Battle and are cleared at End.

### Flow D: Player A attacks

```text
Mouse click
  → convert screen position to Vector2Int
  → GetTile(clickedPos)
  → read Occupant
  → ResolveAttack
      → dead/charge/domain/range checks
      → IsTargetKnown(Player A fog)
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
| Change footprint rotation/world-cell math | `FootprintUtil` | All placement and vision hull calculations reuse it. |
| Change placement collision rules | `GridManager.CanPlaceShip` | This is the single placement validation path. |
| Change movement budget or mutation | `GridManager.MoveShip` | It owns range validation and atomic grid updates. |
| Change ship stats or sensor profiles | `ShipData` | Hardcoded card definitions are built there. |
| Add a ship card | `ShipType`, `ShipFactory`, and `ShipData` | Factory dispatch and card data are separate by design. |
| Change initial fleets/anchors | `GridManager.Start` and `DeploymentService` | Match creation and deployment are here. |
| Change phase order/player switching | `TurnManager` and `Phase` | `TurnManager` is the event source. |
| Change when fog runs | `GridManager.HandlePhaseChanged` / `FogManager` | The former wires phases; the latter coordinates operations. |
| Change stored fog state | `FogGrid` | It owns passive/active dictionaries and state upgrades. |
| Change halo/cone geometry or domain filtering | `VisionResolver` | It is the stateless vision rules layer. |
| Change attack legality/resolution | `GridManager.ResolveAttack` | This is the authoritative attack path. |
| Change Player A input | `TestShipController` | It should request manager operations rather than duplicate rules. |
| Implement fog-aware AI | `AIController` plus `FogManager.GetFogGrid` | AI decisions need its own fog view; final attacks still use `ResolveAttack`. |
| Add armor/defenses/ammo spending | Future combat work around `ResolveAttack`, `ChargeState`, and profiles | These definitions exist, but execution is not implemented. |

Do not put vision geometry in `FogGrid`, attack resolution in `ShipInstance`,
or a second placement validator in an input/controller class.

---

## 14. Temporary and debug code

| Code | Location | Purpose | Safe removal? |
| --- | --- | --- | --- |
| `[Fog]` logs | `FogManager.RecomputeAllPassive`, `RunActiveSearch` | Verify passive and active detected cells in the Console. | Yes, once verification is complete; fog state itself is unaffected. |
| `FogGrid.Describe` | `FogGrid.cs` | Formats both dictionaries for those logs. | Yes, if no caller remains. |
| `Debug Recompute Fog` | `GridManager.cs` context menu | Manually recomputes passive fog and prints per-cell observations. | Yes; it is not part of the phase flow. |
| `Debug Cone Counts` | `GridManager.cs` context menu | Confirms range 2 = 8 and range 4 = 24. | Yes; it only logs. |
| `DrawDebugCones` | `GridManager.OnDrawGizmos` | Displays active cone geometry for visual verification. | Yes; detection does not depend on Gizmos. |
| `DrawDebugHalos` | `GridManager.OnDrawGizmos` | Displays passive halo approximation in the Scene view. | Yes; it is visualization only. |
| `[AI]` logs | `AIController.cs` | Shows movement and weapon decisions. | Optional; useful while AI remains prototype. |
| `LogStatBlock` and occupancy logs | `ShipInstance`, `DeploymentService`, `GridManager` | Verifies card data and deployment. | Optional verification helpers. |

`DrawDebugHalos` currently visualizes distance from `ship.anchor`, whereas
`VisionResolver` measures halos from every occupied source cell. It is therefore
an approximate debug drawing, not the authoritative detection result.

---

## 15. Current implementation versus planned implementation

| System | Current implementation | Planned / missing |
| --- | --- | --- |
| Fog storage | Two `FogGrid` objects, each with passive and active dictionaries. | Player-facing fog/visibility UI. |
| Passive detection | `Staging` recomputes live enemy cells using halo/cone rules and domain filters. | Additional shapes or line-of-sight, which are explicitly out of scope. |
| Active Search | `Search` automatically runs every non-passive layer for every live acting-player ship; writes `Marked`. | Player-selected search action/aiming. |
| Fog lifetime | Passive is rebuilt at `Staging`; active is cleared at `End`; no ghost positions. | Persistent last-known markers, explicitly deferred. |
| Combat gate | `ResolveAttack` calls `IsTargetKnown`; any marked/identified target cell is sufficient. | Combat reveal hook after firing. |
| Combat resolution | d20 tier damage and destroyed-ship cleanup. | Armor, defense saves, charge spending/recharge, side effects. |
| AI | One Player B ship homes on Player A's first ship and greedily picks a weapon. | Fog-aware targets, center fallback, all living ships, active-search decisions. |
| Deployment | Both players receive Wolf and Athena at hardcoded anchors. | Validated deployment path and player-controlled deployment. |
| Cleanup | Temporary fog verification logs/Gizmos remain. | Remove or reduce them after verification. |
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
| Marked | Known enemy position/domain without full identity in the milestone model. |
| Identified | Stronger fog state written by an absolute passive layer. |
| Passive | Vision layer evaluated during `Staging` without an active action. |
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
GridManager       → board, occupancy, movement, attack resolution, phase/fog wiring
Tile              → one coordinate and its occupant
FootprintUtil     → rotate footprints and calculate world cells
ShipInstance      → runtime ship state
ShipFactory       → dispatch ship creation
ShipData          → hardcoded Wolf/Athena/SwordFish definitions
MatchState        → both players and aggregate ship access
PlayerState       → one player's roster and live ships
DeploymentService → initial fleet construction and placement
TurnManager       → phase/player state and PhaseChanged event
FogManager        → when each player's vision is evaluated
FogGrid           → passive/active fog state for one player
VisionResolver    → halo/cone geometry and detection filtering
AIController      → prototype Player B movement/weapon choice
TestShipController→ prototype Player A input
```

### Method → purpose

```text
GridManager.CanPlaceShip       → validate candidate footprint
GridManager.MoveShip            → validate and atomically move/rotate a ship
GridManager.ResolveAttack       → authoritative current attack path
GridManager.IsTargetKnown       → apply the fog attack gate
ShipInstance.GetOccupiedCells   → calculate current world footprint
ShipInstance.InitializeCharges  → initialize health and profile charges
VisionResolver.GetDetectedCells→ find enemy cells detected by one layer
VisionResolver.GetConeCells     → generate directional cone coordinates
VisionResolver.GetBowAndFacing  → derive bow and facing from footprint
FogManager.RecomputeAllPassive  → rebuild both passive fog views
FogManager.RunActiveSearch      → mark cells found by acting player's active layers
FogManager.ClearAllActiveMarks  → clear temporary search results
TurnManager.AdvancePhase        → advance phase and raise PhaseChanged
```

### System → main entry point

```text
Grid             → GridManager.Awake / Start
Deployment       → DeploymentService.DeployAll
Turns            → TurnManager.AdvancePhase
Passive fog      → GridManager.HandlePhaseChanged(Staging)
Active fog       → GridManager.HandlePhaseChanged(Search)
Combat           → GridManager.ResolveAttack
Player input     → TestShipController.Update
AI               → AIController.Update
```

### Event → who raises it / who listens

```text
PhaseChanged → raised by TurnManager.AdvancePhase
             → currently listened to by GridManager.HandlePhaseChanged
```

### Data → where it is stored

```text
Board occupancy → GridManager.tiles
Match ownership → MatchState.playerA/playerB
Live fleets     → PlayerState.ships
Ship state      → ShipInstance
Card definitions→ ShipData-built WeaponProfile/DefenseProfile/VisionLayer lists
Charges         → ShipInstance.weaponCharges/defenseCharges
Player fog      → FogManager's two FogGrid instances
```
