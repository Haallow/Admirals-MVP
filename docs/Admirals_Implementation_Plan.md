# Admirals — Phase 1 Implementation Plan

Target: Unity. No code written yet. Art in progress separately.
Purpose: hand this to an AI coding assistant as a build spec.

## Scope (KISS/YAGNI)

Build now:
- Tile grid, multi-tile ships, move + rotate
- Turn/phase loop (Move, Search, Battle)
- Fog of War with "mark only" reveal
- Per-ship scan patterns
- Per-ship attack patterns + dice-based resolution

Defer (do not build yet, don't scaffold for it either):
- Networking/multiplayer sync (single local session or hotseat only for now)
- Save/load
- Full ability system beyond Submerge/Lay Mine (hardcode the two you have, don't build a generic ability framework yet)
- Animations, VFX, sound
- AI opponent
- Balance tuning / final numbers from the doc — treat them as placeholders

Rule for the coding agent: if a feature isn't in the "Build now" list, don't add hooks, interfaces, or "future-proofing" for it. Add it when it's actually needed.

## Core data model (ScriptableObjects)

Use ScriptableObjects so ship/weapon stats are data, not code. One asset per ship class.

**ShipDefinition**
- id, displayName
- footprint (width x length in tiles, e.g. 1x2, 1x3)
- health, armor
- movementRange
- domain: Surface / SubSurface / Both (for subs that toggle)
- list of ScanProfile
- list of WeaponProfile (offensive)
- list of DefenseProfile (defensive schemes)
- abilityId (string/enum, hardcoded logic per id for now — see Defer list)

**ScanProfile**
- id (Passive Sonar, Active Search Sonar, etc.)
- shape: Halo(radius) or Cone(range, arc)
- domainsDetected: SF / SS / Both
- visionType: Sensor (mark only) or Absolute (full reveal)
- passive (always on) vs active (consumes the Search phase)

**WeaponProfile**
- id, ammoCount (or infinite)
- targetDomain: SF / SS / Both
- attackShape: which tiles it can hit (single tile, line, cone — define per weapon)
- rollTable: list of (diceRangeMin, diceRangeMax, outcome, damage) — this is your existing D1-10/D11-20 style table
- requiresState (e.g. only usable while Surfaced)

**DefenseProfile**
- id, usesPerGame or infinite
- validAgainst: which weapon domain it counters
- savingThrowTable: list of (diceRange, outcome)
- sideEffect (e.g. "becomes Sub-Surface, cannot move next turn")

This lets you add new ships/weapons by creating assets, no new code, once the system is built.

## Systems to implement, in order

**1. Grid — detailed spec**

Classes to build first:
- `Vector2Int` (Unity built-in, use it for all tile coordinates)
- `Tile`: plain data, holds `Vector2Int position`, `ShipInstance occupant` (nullable), maybe `bool isValid` if the board isn't a perfect rectangle
- `GridManager` (MonoBehaviour or plain C# singleton): owns `Dictionary<Vector2Int, Tile> tiles`, exposes:
  - `bool IsInBounds(Vector2Int pos)`
  - `bool IsOccupied(Vector2Int pos)`
  - `Tile GetTile(Vector2Int pos)`
  - `void PlaceShip(ShipInstance ship, List<Vector2Int> cells)`
  - `void RemoveShip(ShipInstance ship)`

Footprint representation:
- Store a ship's footprint as a list of local offsets from an anchor cell, e.g. a 1x3 ship = `[(0,0), (1,0), (2,0)]`
- Rotation = rotate each offset 90/180/270 degrees around the anchor: for 90°, `(x,y) -> (-y,x)`, etc. Write one `RotateOffsets(offsets, degrees)` helper, reuse it everywhere.
- World cells for a ship = anchor position + rotated offsets

Validation function (`CanPlaceShip(anchor, footprint, rotation)`), used by both Deploy and Move:
- Compute world cells from anchor + rotated footprint
- Every cell must be `IsInBounds` and NOT `IsOccupied` (except by the ship's own current cells, for a move)
- Return true/false, don't apply the move yet — let the caller decide what to do on failure

**2. Ship placement & movement**
- Deploy phase: player picks anchor + rotation, call `CanPlaceShip`, if true call `GridManager.PlaceShip`
- Move phase: player picks new anchor + rotation, validate range first (distance from current anchor to new anchor, using whatever distance rule you pick — Chebyshev/king-move distance is the usual choice for grid games with rotation), then validate placement, then `RemoveShip` old cells + `PlaceShip` new cells in one atomic step (don't leave the grid in a half-updated state if validation fails partway)
- Rotation: same `RotateOffsets` helper as above. Expose rotation as 0/90/180/270 only, no arbitrary angles, since footprints are grid-aligned rectangles
- `ShipInstance` (runtime, not the ScriptableObject): holds a reference to its `ShipDefinition`, current anchor position, current rotation, current health, current ammo per weapon

**3. Turn/Phase state machine**
- Simple state machine: Deploy (turn 1 only) → loop of Move → Search → Battle
- Each phase: iterate active player's ships, let them act (or pass), advance
- Keep this dumb and explicit for now — a switch/enum state, not a generic FSM framework

**4. Fog of War**
- Per-player visibility grid, separate from the ship-occupancy grid
- Two reveal states per tile per player: Unknown, Marked (something's there, no ship identity/art), Identified (only if you decide Absolute Vision shows identity — confirm this; doc doesn't say explicitly whether Absolute reveals ship type or just the tile)
- Sensor Vision → sets Marked
- Absolute Vision → sets Identified (assumption, flag this for confirmation)
- Marked/Identified tiles don't decay automatically per your notes ("fog will stay") — once revealed, stays revealed unless you later add a decay rule

**5. Scan resolution**
- Active scan: player selects a ship, applies its ScanProfile shape from that ship's position
- For each enemy ship cell inside the shape whose domain matches: mark that tile in the scanning player's FoW grid
- Passive scan: apply automatically at the start of each turn (or continuously) using the same shape logic, no player action needed

**6. Combat resolution**
- Player selects attacker, weapon, target tile(s) matching that weapon's attack shape
- Roll 1d20 (your ranges are all within 1-20), look up outcome in the WeaponProfile's rollTable
- If Direct Hit / Catastrophic Hit: apply damage minus target's armor to target's health
- Before damage applies, defender may have a queued Defense scheme (decoys, chaff, evasive) — resolve saving throw first if applicable, using DefenseProfile's savingThrowTable
- Health <= 0 → ship destroyed, remove from grid

## Suggested folder structure

```
Assets/
  Scripts/
    Grid/          (Tile, GridManager, FootprintUtil)
    Ships/         (ShipDefinition, ShipInstance/runtime state)
    FogOfWar/       (FowGrid, RevealState)
    Scan/          (ScanProfile, ScanResolver)
    Combat/        (WeaponProfile, DefenseProfile, CombatResolver, DiceRoller)
    Turns/         (TurnManager, Phase enum)
  Data/
    Ships/         (ShipDefinition assets)
    Weapons/       (WeaponProfile assets)
    Scans/         (ScanProfile assets)
```

## Build order (milestones)

1. Grid + place a hardcoded ship, see it occupy correct cells
2. Move + rotate that ship, validate collisions
3. Turn/phase loop cycling with no real actions yet (just logs)
4. Two ships (one per side), FoW grid with Marked/Identified states, no scanning logic yet — just prove the grid can be per-player
5. Wire in one ScanProfile (Passive Sonar halo), confirm it marks enemy tiles correctly
6. Wire in one WeaponProfile (Deck Gun) with dice resolution and damage
7. Add remaining ships/weapons as data assets once the pipeline above works for one of each

## Open questions to confirm before/while building

- Does Absolute Vision reveal ship identity/art, or just a stronger mark than Sensor Vision? Doc doesn't say.
- Movement Range and Attack Range are blank on every ship card — need real numbers before step 2/6 above can be finished, placeholders are fine to start.
- Hotseat (shared screen, take turns) or two separate views even locally? Affects whether FoW needs to be enforced in the same session.
- Mines (Sword Fish ability): are they visible to the opponent once placed, or do they interact with FoW too? Treat as a placed hazard object, resolved like a stationary weapon on trigger.
