# Admirals — Current Project Progress Status

**Status date:** 2026-09-20  
**Project:** Admirals  
**Engine:** Unity `6000.5.10f1`  
**Repository branch:** `feature/grid-system`

## Purpose

This document is the current implementation status for the Admirals prototype. It
describes what is actually present in the repository, what is only partially
implemented, what remains out of scope, and the next work that should be done.
It is intentionally based on the current source and scene files rather than on
planned features being treated as complete.

## Project summary

Admirals is a local, turn-based naval combat prototype played on a rectangular
tile grid. Ships occupy multiple cells, can rotate and move, and have domains,
weapons, defenses, vision definitions, health, armor, and charge state. The
prototype currently proves the core grid, movement, phase, ship-data, combat,
and basic AI foundations through Unity Console logs and Gizmos.

The project is not yet a complete playable fog-of-war game. There is no finished
deployment flow, scan resolver, player-specific visibility enforcement, UI,
art, or production combat system.

## Milestone status

### Milestone 1 — Grid and hardcoded ship placement

**Status: Complete foundation.**

- `Tile` stores a coordinate, occupant, and validity flag.
- `GridManager` builds a rectangular `Dictionary<Vector2Int, Tile>`.
- Ships are created from hardcoded ship data and placed on the grid at runtime.
- Occupied cells are logged to the Console.
- Occupied cells and empty cells are shown with Gizmos.

### Milestone 2 — Move, rotate, and collision validation

**Status: Complete foundation.**

- `FootprintUtil` converts local footprint offsets to world cells.
- Rotations use 0, 90, 180, and 270 degrees.
- `GridManager.CanPlaceShip` validates bounds and collisions.
- A ship is allowed to overlap its own current cells during movement/rotation.
- `GridManager.MoveShip` validates range and placement before mutating the grid.
- Movement uses Chebyshev distance.
- Movement is atomic: failed validation leaves the prior placement unchanged.

### Milestone 3 — Turn/phase loop and ownership gating

**Status: Prototype complete.**

- `Phase` currently contains `Move`, `Search`, and `Battle`.
- `TurnManager` advances through those phases.
- Completing `Battle` returns to `Move` and switches players.
- `TestShipController` only allows movement and attacks during the owning
  player's turn.
- Player A input is currently keyboard/mouse based.
- Phase changes are verified through Console logs.

### Milestone 4 — Ship data, profiles, charges, and basic AI

**Status: Implemented prototype.**

- `ShipFactory` creates `WolfClass`, `AthenaClass`, and `SwordFishClass`
  runtime instances.
- `ShipData` builds each ship's current stats and profile lists.
- `ShipInstance` contains runtime placement, health, armor, domain, profiles,
  and charge state.
- `WeaponProfile` supports target domains, range, ammo, and d20 roll tiers.
- `DefenseProfile` supports valid domains, uses, saving-throw tiers, and a
  reserved side-effect id.
- `ChargeState` tracks remaining uses and recharge fields.
- `VisionLayer` stores halo/cone geometry, range, detected domain, passive/active
  state, and a surfaced-only flag.
- Player B has a basic AI controller that moves toward Player A and selects a
  legal weapon using expected damage.

### Milestone 5+ — Fog of war, scanning, complete combat, remaining systems

**Status: Not complete.**

Only the first data placeholders exist:

- `FogState` defines `Unknown`, `Marked`, and `Identified`.
- `FogGrid` currently stores only one owner and one `fogTile`.
- `FogManager` exists as an empty placeholder.
- `VisionLayer` stores scan definitions but no scan resolver applies them.

The following are still missing:

- Per-player fog grids covering the full board.
- Passive and active scan resolution.
- Halo and cone geometry evaluation.
- Domain and ownership filtering for scans.
- Persistent marked/identified target knowledge.
- Fog-gated attack validation.
- A complete defense/save resolution path.
- Charge consumption and recharge behavior.
- A deployment phase and fleet deployment rules.
- A complete multi-ship match state.

## Current systems

### Grid and board

The current scene is configured for a **30 × 15** board with one-cell tiles.
The Grid Manager draws:

- Gray wireframe cells.
- Cyan Player A occupancy.
- Red Player B occupancy.
- A blue translucent Player A starting zone.
- A red translucent Player B starting zone.

The starting-zone width is currently five columns on each side. The colors are
serialized in `SampleScene.unity` and also have matching script defaults.

### Runtime test ships

`GridManager.Start()` creates:

- `testShip` as a `ShipType` selected in the Inspector, owned by Player A.
- `obstructionShip` as a `ShipType` selected in the Inspector, owned by Player B.

Both ships are placed directly by hardcoded runtime anchors. This is still test
setup, not player-controlled deployment.

### Player input

Current prototype controls:

- **Arrow keys:** move Player A's test ship during Player A's `Move` phase.
- **Q / E:** rotate Player A's test ship during Player A's `Move` phase.
- **Left mouse button:** attack the clicked enemy ship during Player A's
  `Battle` phase.
- **Space:** advance the current phase.
- **D:** toggle the Player A ship between `Surface` and `SubSurface`.

The attack controller currently selects a fixed weapon list entry rather than
letting the player choose from the ship's available weapons.

### Combat

`GridManager.ResolveAttack` currently checks:

1. The attacker's matching charge exists and is ready.
2. The weapon target domain matches the target domain.
3. The target is within weapon range.
4. A d20 roll maps to the weapon's configured `RollTier`.
5. Roll damage is applied and a destroyed ship is removed from the grid.

This is a prototype resolver. It does not yet apply armor, resolve defense
profiles, consume ammo/uses, process recharge, or resolve defense side effects.

### AI

`AIController` is a simple Player B opponent:

- It acts automatically during Player B's turn.
- During `Move`, it attempts to move one cell toward Player A.
- During `Battle`, it evaluates legal weapons and chooses the highest expected
  damage weapon.
- It does not scan, reason about fog, choose targets, use defenses, or handle
  multiple ships.

### Ship data currently present

The repository contains hardcoded builders for:

- **Wolf Class:** surfaced/subsurface-capable prototype with multiple weapons,
  defenses, passive/absolute vision, and active/passive scan definitions.
- **Athena Class:** surface ship with multiple weapon and defense profiles and
  passive/active vision definitions.
- **SwordFish Class:** ship data builder is present and supplies its current
  prototype stats and profiles.

These are plain C# runtime data builders, not ScriptableObject assets. The
current architecture intentionally keeps data and behavior separate, but the
planned data-asset pipeline has not been built.

## Repository structure

### Implemented script areas

- `Assets/Scripts/Grid/`
  - `GridManager.cs`
  - `Tile.cs`
  - `FootprintUtil.cs`
  - `TestShipController.cs`
- `Assets/Scripts/Ships/`
  - `ShipInstance.cs`
  - `ShipFactory.cs`
  - `ShipData.cs`
  - `ShipType.cs`
- `Assets/Scripts/Turns/`
  - `Phase.cs`
  - `PlayerId.cs`
  - `TurnManager.cs`
- `Assets/Scripts/Combat/`
  - `WeaponProfile.cs`
  - `DefenseProfile.cs`
  - `VisionLayer.cs`
  - `DomainType.cs`
  - `RollTier.cs`
  - `ChargeState.cs`
- `Assets/Scripts/AI/`
  - `AIController.cs`
- `Assets/Scripts/FogOfWar/`
  - `FogState.cs`
  - `FogGrid.cs`
  - `FogManager.cs`

### Scene and project

- `Assets/Scenes/SampleScene.unity` is the current prototype scene.
- Unity version is `6000.5.10f1`.
- The project includes the 2D, Tilemap, URP, Input System, Test Framework, and
  editor integration packages.
- No production art track is currently present in this repository.

## Current working-tree changes

At the time this status was written, the working tree contains changes that are
not all part of the last commit:

- `Assets/Scenes/SampleScene.unity`
  - Scene board changed to 30 × 15.
  - Starting-zone width and colors serialized.
- `Assets/Scripts/Grid/GridManager.cs`
  - 30 × 15 defaults.
  - Starting-zone color fields and Gizmo rendering.
- `Assets/Scripts/Ships/ShipData.cs`
  - Local ship-data/stat changes relative to `HEAD`.
- `docs/AGENTS.md`
  - Local milestone checklist/wording change.
- `Assets/Scripts/FogOfWar.meta`
  - Untracked folder metadata file.

These changes should be reviewed and committed separately from future feature
work. Do not discard them as part of an unrelated milestone implementation.

## Known limitations and technical debt

### Gameplay

- No start/deployment phase exists in the phase enum.
- Only two runtime test ships are placed automatically.
- There is no fleet or match-state owner for multiple ships.
- Search phase has no action.
- Battle phase has no target/weapon selection UI.
- Domain toggling is not phase-gated.
- Destroyed ships are removed from occupancy but have no broader lifecycle.

### Fog and visibility

- `FogGrid` is not a board-sized visibility map.
- `FogManager` has no behavior.
- Vision geometry is stored but not evaluated.
- No information hiding is enforced; the prototype can inspect the full grid.

### Combat

- Armor is stored but not applied.
- Defenses are stored but not resolved.
- Charges are initialized but not decremented.
- Recharge fields are stored but not processed.
- The fixed attack weapon selection is fragile and must be replaced before adding
  more weapons.

### Presentation and verification

- There is no UI, art, animation, sound, or VFX.
- Verification is currently manual through Unity Play Mode, Console logs, and
  Gizmos.
- No automated gameplay tests are currently present.

## Recommended next implementation slice

Following `docs/AGENTS.md` and the KISS/YAGNI rule, the next focused milestone
should be to define the smallest verifiable fog-grid foundation:

1. Create a board-sized `FogGrid` for one player.
2. Add read/write operations for `Unknown`, `Marked`, and `Identified`.
3. Add a second fog grid for the other player.
4. Log a deterministic test reveal for one or two known cells.
5. Verify that fog state is independent from ship occupancy.

Do not add scan geometry, combat fog gating, UI, networking, save/load, or a
generic ability framework in that slice. Those should follow only after the
fog-grid data structure is proven.

## Verification checklist

The current prototype should be manually verified in Unity:

- Enter Play Mode without compile errors.
- Confirm the Console logs both runtime ship stat blocks.
- Confirm occupied cells are logged.
- Confirm the 30 × 15 grid is visible.
- Confirm the blue and red starting zones appear in the Scene view.
- Move and rotate Player A's ship with arrow keys and Q/E.
- Confirm invalid moves do not partially alter occupancy.
- Press Space through Move → Search → Battle → Move.
- Confirm Player B changes turn after Battle.
- Confirm the AI attempts movement and attacks during Player B's phases.
- Confirm a valid attack produces a d20 outcome log.

No Unity Play Mode run was performed while generating this document.

## Credits and ownership

- **Game design, rules, and ship stats:** Tom (Hallow).
- **Art:** in progress and maintained separately from this prototype.
- **Architecture planning and implementation guidance:** developed
  collaboratively with Claude (Anthropic), following the milestone plans in
  `/docs`.

