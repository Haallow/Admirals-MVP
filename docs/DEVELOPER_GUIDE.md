# Admirals Developer Guide

This guide describes the checked-in Unity prototype. Runtime code in
`Assets/Scripts` is the authority for behavior; the sample scene and map asset
show the checked-in setup. `docs/CHANGELOG.md` records history and is not
evidence that a feature still works as described there.

## Current scope and limits

- The project declares Unity `6000.5.10f1` in `ProjectSettings/ProjectVersion.txt`.
- `GridManager` defaults to a 30 by 15 rectangular board. The sample scene
  assigns `TestMap.asset`, also 30 by 15, with no terrain entries. Thus the
  checked-in sample board has only Normal terrain, though the runtime supports
  Costly and Impassable cells when a map defines them.
- Ships are plain serializable C# `ShipInstance` objects, built from hardcoded
  `ShipData` cards. They are not ship GameObjects or ScriptableObject cards.
- Current systems include provisional Player A movement, legacy AI movement,
  per-player fog, passive and active detection, terrain line of sight, combat
  with d20 damage and armor, contact mines, and reconnaissance planes.
- The playable presentation and input remain prototype level. `GridView`
  draws Gizmos; `TestShipController` uses Unity's legacy `Input` polling.
  There is no production fog shroud or ship art in this runtime path.
- Combat resolves chosen defenses and their successful side effects through
  `CombatResolver`. There is no repair ship or production game-over
  screen. Fleet elimination now produces a match result and Console summary.
  The AI has no Staging actions for mines or planes.

## Ownership and runtime flow

| Authority | Responsibility |
| --- | --- |
| `MatchState` and `PlayerState` | Own players, requested rosters, live ship lists, mines, planes, match result, and turn summaries. `AllShips()` builds a new aggregate list. |
| `GridManager.tiles` | Authoritative ship occupancy by coordinate. `GridManager` builds the board, places/removes/moves ships, deploys mines and planes, owns provisional movement and scan previews, and reacts to phases. |
| `ShipInstance` | Holds a ship's owner, card profiles, health, domain, anchor, rotation, footprint offsets, charge states, and per-Battle attack flag. Its occupied cells derive from the anchor, rotation, and offsets. |
| `FogManager` / `FogGrid` | Own the two players' separate knowledge grids and their lifecycle. A `FogGrid` stores knowledge, not ships or occupancy. |
| `VisionResolver` | Stateless detection shapes, domain filters, and terrain line-of-sight geometry. |
| `CombatResolver` | Authoritative attack validation, roll, damage, ammunition use, and kill cleanup; exposed as `GridManager.Combat`. |
| `GridView` | Reads grid and fog state to draw Gizmos; does not change gameplay state. |
| `TurnManager` | Owns player and phase, then emits `PhaseChanged`; it does not implement board, fog, or combat rules. |

`GridManager.Awake` builds tiles and creates `FogManager` and
`CombatResolver`. `GridManager.Start` creates the two `PlayerState` objects,
builds `MatchState`, initializes `DeploymentService`, drafts Player B's local
formation, and subscribes to
`TurnManager.PhaseChanged`. `AIController` subscribes in `OnEnable` and
unsubscribes in `OnDisable`. `TurnManager.AdvancePhase` asks generic guards
whether the current phase may end, then changes phase, switches player only
on End to Move, logs, and invokes subscribers with the new phase.
`GridManager` guards Player A's Move exit with the same read-only validation
used by movement confirmation, including direct phase advances. The
prototype controller confirms movement before calling `AdvancePhase`.

The cycle is `Move -> Staging -> Search -> Battle -> End -> next player's Move`.

| Entered phase | `GridManager` reaction | Other action |
| --- | --- | --- |
| Move | Clears provisional states; snapshots each Player A ship when Player A is active. | On Player B's Move event, AI snapshots and moves its ships sequentially through legacy `MoveShip`. |
| Staging | For Player A, calls `ConfirmProvisionalMovement` again; snapshots the acting player's plane positions. | Player A may deploy mines/planes and move eligible planes. AI does no Staging action. |
| Search | Clears the acting player's planes' `deployedThisTurn`, recomputes passive fog for both players, clears scan preview and fleet scan flag. | A scan requires explicit Player A input or the AI Search event. |
| Battle | Resets all live ships' `hasAttackedThisPhase`. | Player A input or AI may request attacks. |
| End | Resets attack flags, clears active fog marks and scan state, ticks the acting player's ship recharge and plane fuel, removes exhausted planes, refreshes passive fog, then snapshots and logs the turn summary. | Space starts the next player's Move unless the match has ended. |

Combat destruction and completed mine movement commits evaluate fleet elimination immediately. A result blocks subsequent commands and phase advances and records a partial final summary without End recharge or fuel ticks. If both fleets are lost in the same mine commit, the result is a draw. The prototype reports the result in the Console.

The starting Move phase begins through `StartMatch` after both formations
commit; it emits `PhaseChanged(Move)`. `TestShipController` also snapshots Player A's
`anchorAtTurnStart` when it first observes Move.

## Grid, terrain, placement, and movement

`MapDefinition` is a reusable ScriptableObject containing width, height, and
sparse `MapTerrainEntry` values. If assigned, its dimensions replace
`GridManager.width/height` during `BuildGrid`; unspecified cells are Normal.
Without an asset, the serialized grid dimensions remain and all new tiles
default to Normal. A `Tile` holds immutable `Position`, current `Occupant`,
terrain type/cost, and a currently unused `IsValid` flag.

| Terrain | Passable | Entering cost | Vision and attack LOS |
| --- | --- | ---: | --- |
| Normal | Yes | 1 | Transparent |
| Costly | Yes | At least 2, configured by map entry | Transparent |
| Impassable | No | 0 | Blocks intermediate rays |

`FootprintUtil.RotateOffsets` uses these quarter turns:

```text
0° (x,y)     90° (-y,x)     180° (-x,-y)     270° (y,-x)
```

`GridManager.CanPlaceShip` checks every candidate footprint cell for bounds
and another ship's occupancy. It does **not** enforce a one-cell buffer, check
terrain passability, or call an exclusion-zone helper. `PlaceShip` writes
occupancy; `RemoveShip` clears every tile occupied by that ship. Deployment
calls `CanPlaceShip` before `PlaceShip`.

`GridPathfinder` calculates read-only, eight-direction Dijkstra routes. It
rejects impassable and other-ship-occupied anchor cells, allows the moving
ship's own cells, and charges the cost of each entered cell. The path result
contains reachability, ordered cells, total cost, and whether cost fits the
budget. Pathfinder traversal checks the anchor cell, so the final full
footprint is checked separately by `GridManager`.

For Player A, `PreviewMove` calculates from each ship's original snapshot,
requires a path within budget, and checks the candidate footprint for bounds,
passable terrain, authoritative occupants, and other provisional footprints.
An invalid preview leaves its last valid candidate intact. The preview does
not change ship anchors or occupancy. `ConfirmProvisionalMovement` validates
all current candidates, rejects overlapping candidate footprints, removes
their old occupancy, places all new footprints, resolves mines for the moved
ships, and clears preview state. `CancelProvisionalMovement` clears all
previews. Escape/C clear uncommitted previews and stop an active drag;
authoritative anchors remain at their Move-start positions. After successful
confirmation and entry to Staging, ship movement is committed and cannot be
canceled. A direct `AdvancePhase` call is rejected before the transition if
the remaining provisional state cannot be confirmed.

`MoveShip`, still used by AI, is a separate distance-based path. It compares
Chebyshev distance from `anchorAtTurnStart` to the requested anchor with
`movementRange`, then calls `CanPlaceShip`, removes and places occupancy,
and resolves mines. It does not use Dijkstra cost or provisional candidates.
AI candidate selection uses reachable anchor cells and `CanPlaceShip`, but
the final execution still uses `MoveShip`.

## Fleets and cards

`GridManager.Start` requests these rosters, in this order:

| Player | Requested ships | Initial anchors |
| --- | --- | --- |
| A | Wolf, Athena, SwordFish, Carrier, Cruiser | `(1,1)`, `(1,4)`, `(1,7)`, `(1,10)`, `(1,13)`; rotation 0° |
| B | Wolf, Athena | `(width-3,1)`, `(width-3,4)`; rotation 180° |

`DeploymentService` increments Y by 3 only after successful placement.
Invalid placements are skipped with a warning and are not added to the live
list. In the checked-in 30 by 15 sample configuration, Player A's Cruiser
candidate at Y 13 is in bounds because its footprint extends along X.
`PlayerState.fleetRoster` remains the requested roster after a ship dies;
`PlayerState.ships` is the live roster.

| `ShipData` card | Hull / move / domain | Weapons | Vision and other capabilities |
| --- | --- | --- | --- |
| Wolf | 2 cells, 500 HP, armor 50, move 6, starts Surface | MRK-1 Torpedo, Spear Anti-Ship Missile, Hippocampus Torpedo | Passive Sensor sonar range 2 detects Both; surfaced-only Absolute halo range 4; active Sensor cone range 4 detects Both. Three defense profiles. |
| Athena | 3 cells, 2000 HP, armor 150, move 4, Surface | Deck Gun, Anti-Ship Missile, Anti-Submarine Rocket | Passive Absolute halo range 2 and active Sensor cone range 2, both detect Both. Two defense profiles. |
| SwordFish | 2 cells, 1000 HP, armor 100, move 8, Surface | Same three weapon types as Athena | Passive Absolute radar range 2 detects Surface; active Sensor sonar range 5 detects Both; Contact Mine profile: count 2, damage 600, recharge time 2. Two defense profiles. |
| Carrier | 5 cells, 1600 HP, armor 200, move 5, Surface | Deck Gun; Anti-Ship Missile with 5 ammo | Passive Absolute halo range 2 and active Sensor cone range 2 detect Both; Recon Plane profile: one sortie, launch 4, move 5, vision 3, fuel 3. Two defense profiles. |
| Cruiser | 4 cells, 1200 HP, armor 100, move 5, Surface | Deck Gun, Anti-Ship Missile, Anti-Submarine Rocket | Passive Absolute halo range 2 and active Sensor cone range 5 detect Both. Two defense profiles. |

`ShipFactory` dispatches `ShipType` to these builders.
`ShipInstance.InitializeCharges` sets current health and creates runtime
weapon, defense, mine, and plane charge lists. Nullable ammunition or uses
become the `-1` infinite sentinel. `LogStatBlock` is a console helper.
`WeaponProfile`, `DefenseProfile`, `RollTier`, `VisionLayer`,
`MineProfile`, and `PlaneProfile` define card capabilities; `ChargeState`
holds mutable remaining uses, capacity, and recharge countdown.

The hardcoded weapon tables retain these prototype values. Roll bands are
inclusive; unlisted damage in a Miss band is zero.

| Weapon and users | Ammo | Range / target | d20 outcomes |
| --- | ---: | --- | --- |
| MRK-1 Torpedo — Wolf | 4 | 4 / SubSurface | 1–10 Miss; 11–20 800 |
| Spear Anti-Ship Missile — Wolf | 2 | 7 / Surface | 1–3 Miss; 4–17 500; 18–20 750 |
| Hippocampus Torpedo — Wolf | 3 | 5 / Both | 1–6 Miss; 7–16 1000; 17–20 1500 |
| Deck Gun — Athena, SwordFish, Carrier, Cruiser | Infinite | 6 / Surface | 1–10 Miss; 11–20 300 |
| Anti-Ship Missile — Athena, SwordFish, Cruiser | 2 | 8 / Surface | 1–3 Miss; 4–17 500; 18–20 750 |
| Anti-Ship Missile — Carrier | 5 | 8 / Surface | 1–3 Miss; 4–17 500; 18–20 750 |
| Anti-Submarine Rocket — Athena, SwordFish, Cruiser | 2 | 8 / SubSurface | 1–5 Miss; 6–20 600 |

Wolf defines Crash Dive, Acoustic Decoys, and Deep Dive defenses; the
surface ship cards define Evasive Manouvers and Chaff & Flares. Their
saving-throw bands and uses are stored on `DefenseProfile`, but none are
consulted by `RequestAttack`. The names in code include the
`Manouvers` spelling.

## Fog and vision

Each `FogGrid` has separate passive and active dictionaries. `Unknown = 0`,
`Marked = 1`, and `Identified = 2`; `Upgrade` preserves the stronger state
within a layer and `GetState` returns the stronger layer result. Both Marked
and Identified count as known for attacks. These values represent knowledge
of a **cell**; `FogGrid` does not own a ship identity or scan coverage map.

`FogManager.RecomputeAllPassive` clears each player's
passive layer and scans living opposing ships with every friendly passive
`VisionLayer`. It writes Identified for Absolute layers and Marked for Sensor
layers. Owned planes contribute passive Absolute halo detection. Passive fog
is refreshed after deployment commits, committed ship movement or rotation,
domain changes, plane deployment or committed movement, plane removal,
ship destruction, Search entry, and End fuel ticks. Ship and plane previews
leave passive fog on committed positions. The Wolf's passive Absolute layer
works while surfaced or submerged. Active marks persist until End unless explicitly cleared for a
destroyed ship. Passive information is rebuilt from current detections, not
stored as persistent last-known contact memory.

`VisionResolver` skips dead ships, honors `onlyWhileSurfaced`, filters
target domains (`Both` accepts either concrete domain), and returns only
enemy occupied cells within the scan shape. Halo uses Chebyshev distance from
any source hull cell. A cone begins one cell beyond the bow derived from the
ship footprint, with half-width `d * ConeSlope` at distance `d`;
`ConeSlope = 1`. `GetConeCells` itself can produce off-board cells.
Ship vision checks a ray from a suitable hull cell (or the bow for a cone)
through `TryGetFirstBlockingCell`. Its supercover traversal includes side
cells on corner ties, excludes source and target endpoints, and treats only
Impassable terrain as blocking. Plane halo vision uses its position and
domain filter but does not check terrain LOS.

Active scanning is available during Search through
`GridManager.ActivateActiveScan`, `SetActiveScanForward` /
`RotateActiveScan`, and `ConfirmActiveScan`. Activation chooses the
ship's first non-passive Cone layer. Confirmation calls
`FogManager.RunActiveSearch`, writes Marked even if a definition is
Absolute, and sets one shared `fleetScannedThisPhase` flag. Thus at most
one confirmed fleet scan occurs for the acting player in that Search phase.
Cancel discards a preview without marking cells. The scan flag and preview
reset on entering Search and End.

The current mine reveal implementation deserves care: `RunActiveSearch`
checks enemy mine positions against `scan.DetectedCells`, which contains
detected **enemy ship cells**, not all cone cells. It therefore does not
generally reveal a mine merely because its cell lies in the cone. Since a
deployed mine starts on an unoccupied cell and is removed when a ship
triggers it, the documented intent of general active sonar mine reveal is
not established by this code.

`FogManager.ClearMarksForShip` removes a destroyed ship's occupied cells
from both players' passive and active layers. This prevents stale marks at
those cells, though it also removes any overlapping knowledge there until
the next recomputation.

## Combat

`CombatResolver.RequestAttack(attacker, target, weapon)` is the attack
authority, reached through `GridManager.Combat`. Its current sequence:

1. Reject a dead target or an attacker already marked as having attacked.
2. If a `TurnManager` is assigned, require Battle and the attacker's turn.
3. Find the attacker's charge by weapon ID and require `IsReady`.
4. Require the weapon's target domain to match the target or be Both.
5. Measure Chebyshev distance across all attacker/target occupied-cell pairs
   and require at least one pair within weapon range.
6. Require at least one target occupied cell to be known in the attacker's fog.
7. Require at least one in-range pair with clear terrain line of fire, using
   `VisionResolver.TryGetFirstBlockingCell`.
8. Offer ready defenses matching the target's current domain. Player A selects
   one or passes; Player B currently passes automatically. A pending response
   blocks further attacks and phase advancement.
9. Roll and spend a selected defense first. Success avoids the weapon roll and
   damage; Crash Dive submerges the target and locks its next Move.
10. Spend the attack opportunity and finite ammo once. After a pass or failed
    defense, roll the weapon, apply armor, and subtract health.
9. If the target reaches zero health, clear its fog marks, remove its tile
   occupancy, and remove it from its owner's live ship list.

`ApplyArmor` computes
`round(rawDamage * (1 - armor * 0.0015))`. A zero-damage miss stays zero;
nonzero damage is clamped to at least one. `RequestAttack` returns a rejected,
pending, or finalized status; `SubmitDefense` returns a structured outcome.
`CanShipAttack` is a read-only alive/phase/turn/attack-flag query; it does
not check weapon, target, range, fog, or line of fire. Defense profiles and
their charge state and side effects are resolved by `CombatResolver`.

## Mines and reconnaissance planes

`GridManager.DeployMine` requires the selected ship to own a positive mine
charge and be the acting player in Staging. It calculates one cell behind
the stern and rejects out-of-bounds, impassable, ship-occupied, or
already-mined destinations. Success adds a `MineTile` to
`MatchState.mines`, consumes one charge, and starts its recharge countdown.
After a confirmed or legacy ship move, `ResolveMinesFor` collects mines
under the moved footprint, removes them, and applies their combined flat
damage without armor or defense. On death it clears fog marks, occupancy,
and live ownership. The current trigger loop does not filter mines by owner.
`ShipInstance.TickRecharge` decrements the acting player's mine countdown
at End and restores one charge at zero, up to its initial capacity. There is
no plane sortie recharge path.

`DeployPlane` requires Staging, ownership, a positive sortie charge, an
in-bounds **passable** launch destination, and Chebyshev distance at most
the profile's launch range from any carrier hull cell. It does not reject
ship occupancy at that cell. The plane is stored in `MatchState.planes`,
not `GridManager.tiles`; deployment consumes a sortie and recomputes
passive fog. A plane deployed this Staging cannot move until a later
Staging. `PreviewPlaneMove` stores a separate candidate after
checking owner, phase, bounds, and distance from `positionAtTurnStart`;
it does not check terrain, occupancy, or intermediate path.
`ConfirmPlaneMove` updates the live position and refreshes fog; canceling
discards the candidate. `UndeployPlane` removes
only a plane deployed in the current Staging, refunds its launch ship's
matching sortie charge up to capacity, and recomputes fog. At the owning
player's End, fuel decreases by one and a plane at zero is removed.

## Human controls in `TestShipController`

The controller targets Player A's ship list. It polls `Input` each frame,
and `Space` advances `TurnManager` even outside Player A's turn if this
component is active. It returns from other action handling when it is not
Player A's turn. The checks below describe the implemented input path,
not general guarantees on direct service calls.

| Context | Implemented input |
| --- | --- |
| Selection | Left-click a friendly ship's authoritative or relevant preview cell; Tab cycles Player A ships. During Staging, friendly planes can be clicked or reached by Tab after ships. |
| Move | Arrows preview anchor movement; Q/E preview quarter-turn rotation; dragging from a selected ship's cell previews pointer movement. Escape/C clear **all** uncommitted provisional moves. D toggles an active Wolf between Surface and SubSurface. Space confirms provisional movement and, on success, advances. Enter does not commit movement. |
| Staging ship | M requests stern mine deployment. P arms plane placement; next left-click requests deployment, using the selected carrier or the first available Player A carrier. Escape, C, or right-click cancels placement mode. |
| Staging plane | A new plane becomes selected after placement but cannot move that turn. Arrows or a destination click call `PreviewPlaneMove` for older planes; C undeploys a newly launched plane or cancels an older plane's preview. Space commits owned plane moves and advances. |
| Search | S or a click on the active ship opens a cone preview. Mouse hold/drag aims to a cardinal direction; Q/E rotates; Enter confirms; Escape/C cancels. Space confirms a pending preview, then advances. |
| Battle | Keys 1, 2, 3 choose existing weapon slots. Clicking an occupied enemy tile requests an attack; clicking a friendly ship changes selection. On an incoming attack, number keys choose a defense, 0 passes, and Enter submits. |

The controller's Battle click path reads `Tile.Occupant` directly, so
`GridView` hiding an unknown enemy is only a visual constraint. The
authoritative combat fog gate still rejects an unknown target.

## AI

`AIController` handles Player B `PhaseChanged` events and has no Update
loop. At Move it builds `AITurnContext`, records/ages
`AIEnemyMemory`, snapshots each living ship's start anchor, plans, and
executes moves sequentially with `GridManager.MoveShip`. At Search it calls
`AIActiveScanPlanner.RunActiveScans`. At Battle it refreshes memory without
aging, selects attacks for living ships, and calls
`GridManager.Combat.RequestAttack`. If Player A has an eligible defense,
the remaining Player B attacks resume after its response. It has no Staging
or End decision.

`AITurnContext` contains living friendly and enemy lists, Player B fog, and
`KnownEnemies` (an enemy with at least one occupied cell known in that fog).
`AIMovementPlanner` in `AImovementplanner1.cs` scores reachable,
placeable anchors against known enemies, possible attacks, expected danger,
health, and targets already claimed by another ship. It may flee when at or
below 30% health; without an attack option it approaches a known enemy or
the board center. `AIAttackPlanner` scores currently known targets and
available weapons, including hypothetical line of fire and range.
`AIScoring.ExpectedValue` calculates weapon expected damage. Final attack
legality remains in `CombatResolver`.

`AIEnemyMemory` stores last sightings and rough heading for scan
predictions. It is separate from fog and does not make a hidden target
attackable. `AIActiveScanPlanner` in `Aiactivescanner.cs` scores four
cardinal facings by predicted bearing and unknown fog coverage. It iterates
ships in roster order and immediately confirms the first activation that
succeeds; the grid's fleet scan limit rejects later attempts. It does not
compare all ships to choose a global best scanner.

`AIScoring.ExpectedValue` sums each tier's damage times its inclusive
roll-band width divided by 20. This is raw expected damage before armor or
defense. `AIAttackPlanner` favors a predicted lethal shot and applies a
soft penalty when another ship has already fired on a nonlethal target.

The AI context and memory receive live enemy `ShipInstance` references
to establish which ships are currently known. The planner's target lists
use `KnownEnemies`; this is fog-aware selection, but it is not a strict
information-isolated AI API. Movement still executes through the legacy
distance-based grid method.

## Rendering and debugging

`GridView.OnDrawGizmos` draws terrain, starting zones, ships, provisional
footprints, active cone cells, contacts, mines, and planes from grid/match
state. Player A ships are cyan. In Play mode, enemy ship cells are red when
Identified, orange contact markers when Marked, and hidden when Unknown.
The serialized `revealAllInFog` toggle or Edit mode reveals all ship
occupants. Own mines are yellow; enemy mines are otherwise hidden, while
a Marked mine cell uses the generic contact marker. Own planes are green
diamonds; enemy planes are magenta only when their cell is known or reveal
mode is active. The view uses Player A's fog, not the currently active
player's fog. `DrawDebugHalos` and `DrawMovementRanges` are absent.
Console logs from deployment, vision, combat, mine, plane, and AI operations
remain part of prototype verification.

## File map

| Location | Main types and purpose |
| --- | --- |
| `Assets/Scripts/Grid` | `GridManager` (board and phase operations), `GridView` (read-only Gizmos), `TestShipController` (Player A input), `Tile`, `TerrainType`, `MapDefinition`, `FootprintUtil`, `GridPathfinder`, `MovementPathResult`, `ProvisionalMovementState`. |
| `Assets/Scripts/Ships` | `ShipInstance` runtime state, `ShipType`, `ShipFactory`, `ShipData` card builders. |
| `Assets/Scripts/Match` | `PlayerState`, `MatchState`, `DeploymentService`. |
| `Assets/Scripts/Turns` | `TurnManager`, `Phase`, `PlayerId`. |
| `Assets/Scripts/FogOfWar` | `FogState`, `FogGrid`, `FogManager`, `VisionResolver`, `VisionScanResult`, `ActiveScanPreviewState`. |
| `Assets/Scripts/Combat` | `CombatResolver`, domain/vision/shape definitions, weapon/defense/roll/charge profiles, mine and plane profiles/runtime types. |
| `Assets/Scripts/AI` | `AIController`, `AITurnContext`, `AIMovementPlanner`, `AIAttackPlanner`, `AIActiveScanPlanner`, `AIEnemyMemory`, `AIScoring`. Some filenames differ in case and spelling from their class names. |

## Unverified runtime outcomes

This guide is based on source, the sample scene/map serialization, and
static inspection. A Unity Play mode session was not run for this audit.
The following outcomes therefore need runtime confirmation:

- Whether the sample scene's duplicate serialized `TurnManager` component
  on the grid GameObject has any observable effect. The grid, AI, and input
  references point to the separate `TurnManager` component.
- Whether active scanning can ever leave a visible mine contact in a
  nonstandard overlapping state; the ordinary mine reveal path described
  above is not supported by the scan's detected-cell list.
- The appearance of Gizmos in a specific Game or Scene view setup and the
  full sequence of controller input interactions across phase changes.
