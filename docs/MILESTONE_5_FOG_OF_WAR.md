# Milestone 5 — Fog of War

**Status:** In progress (updated 2026-09-20)
**Branch:** one feature branch for this milestone (current work is on `feature/grid-system`; move to a dedicated branch before merging)
**Reads with:** `docs/AGENTS.md` (KISS/YAGNI and conventions apply to every step below)

---

## Goal

Give each player a per-turn, domain-aware picture of the enemy from passive and active detection, and make attacks and AI decisions respect it.

> Placeholder wording. Replace with the exact one-sentence quote from the master plan, as `AGENTS.md` requires.

---

## What "done" looks like

Progress markers: `[x]` verified in Play Mode, `[~]` implemented but not yet verified, `[ ]` not started.

### Foundations (built during this milestone)
- [x] Each player owns a fleet; both sides auto-deploy a Wolf and an Athena in their own half (`MatchState`, `PlayerState`, `DeploymentService`).
- [x] Turn cycle is `Move → Staging → Search → Battle → End`, the player switches at `End`, and `TurnManager` raises `PhaseChanged`.
- [x] Movement range is a per-turn budget measured from `anchorAtTurnStart`.
- [x] Destroyed ships are removed from the grid **and** from their fleet list, cannot be attacked again, and do not crash the AI.

### Fog core
- [x] `FogState` (`Unknown`, `Marked`, `Identified`); one `FogGrid` per player; `FogManager` owns both.
- [x] Passive vision recomputes on entering `Staging`: Absolute layers give `Identified`, Sensor layers give `Marked`, states never downgrade within a recompute.
- [x] Halo range is measured from the nearest cell the source ship occupies. Any other shape is skipped.
- [x] Dead ships neither detect nor are detected.
- [x] Verified by logs: each player's `[Fog]` line lists only the enemy cells within range, and detection is asymmetric because ranges differ.
- [~] **Attack gate:** `GridManager.IsTargetKnown` is called from `ResolveAttack`, so the human click and any other caller are gated. A target is known if any of its cells is known. *Applied, not yet run in Unity.*
- [ ] **Active Search Sonar:** runs automatically on entering `Search` for the acting player's living ships. Cone shape, from the bow cell along the ship's facing. Writes `Marked` into the active layer, never `Identified`. Respects `detects` and `onlyWhileSurfaced`. Active marks clear on entering `End` (already wired).
- [ ] **Vision logic out of the data class:** Halo and Cone geometry and the detection rules live in a static class in `FogOfWar/`. `FogGrid` keeps only state and helpers.
- [ ] **Fog-aware AI:** the AI chooses targets from its own fog and never reads hidden enemy state.

### Housekeeping
- [ ] AI sets its "acted this phase" flag before acting, so a future exception cannot repeat every frame.
- [ ] `Tab` cycling is gated by owner and turn, and its index is clamped when a fleet list shrinks.
- [ ] `ResolveAttack` range uses the nearest attacker cell, not `attacker.anchor`.
- [ ] `DeployFleet` validates through `GridManager.CanPlaceShip`.
- [ ] Temporary `[Fog]` debug logs removed or reduced.

---

## Design decisions (settled)

- Visibility is **non-sticky**: fully recomputed at every `Move → Staging`. No remembered or "ghost" positions.
- The fog grid stores only cells that hold a detected enemy. It does not store scan coverage.
- `Marked` = position and target domain. `Identified` = position, domain, and ship type. Currently nothing consumes either state differently; that is acceptable until a consumer exists.
- Detection requires the layer's `detects` to be `Both` or to match the target's current domain. `onlyWhileSurfaced` layers work only while the source ship is `Surface`.
- Active scan is automatic (no aiming or selection), uses the ship's facing, and never identifies.
- **Cone geometry:** `forward` is the ship's facing (+X at rotation 0, rotated like the footprint). The origin is the occupied cell furthest along `forward` (the bow). A cell counts if `1 <= d <= range` and `|lateral| <= d`, where `d` is distance along `forward`. The slope is a single constant so it is easy to tune.
- **Attack validity has one path:** fog, charge, domain and range are all checked inside `ResolveAttack`, in the same way placement validity lives in `CanPlaceShip`.
- No line-of-sight. `Obstacle` tiles do not block vision.

---

## Build order

Each step is one small, independently verifiable increment. Do not start a step before the previous one is verified with `Debug.Log` or Gizmos.

| # | Step | Status |
|---|---|---|
| 1 | Fleet model and auto-deployment | Done |
| 2 | Phase cycle and `PhaseChanged` | Done |
| 3 | Passive fog (Halo) with debug logging | Done |
| 4 | Destroyed-ship handling | Done |
| 5 | Attack gate in `ResolveAttack` | Applied, verify next |
| 6 | Extract vision logic, then add the Cone and Active Search Sonar | Next |
| 7 | Fog-aware AI | Last |
| 8 | Housekeeping items | Any time, one commit each |

### Step 5 — verify the attack gate
1. In Battle, click a ship your fog does not know. Expect `Target not known.`, no damage.
2. Bring an enemy within your Wolf's range 4 and pass a `Staging`. Expect its cells as `Identified` in the log, and the attack to resolve.
3. Attack a known ship outside weapon range. Expect the range rejection, showing the two checks are independent.

### Step 6 — extraction first, then the Cone
1. **Extract (no behavior change).** Move the detection loops and Halo distance logic out of `FogGrid` into a static class. Re-run the existing fog log checks and confirm identical output.
2. **Add the Cone** to the same static class, with `GetConeCells(origin, forward, range, inBounds)`. Unit-check by log: at rotation 0, origin (5,5), range 2 gives 8 cells; range 4 gives 24.
3. **Wire the Search hook.** In `HandlePhaseChanged`, on entering `Search`, run every non-passive layer of the acting player's living ships and write `Marked` into the active layer.
4. **Verify visually** with a temporary Gizmo drawing the active ship's cone, then rotate with Q/E and confirm it leaves from the bow.
5. **Verify with an enemy:** place an enemy inside the cone and confirm it appears as `Marked`, and disappears after `End`. Confirm the Athena's sonar ignores `Surface` enemies.

### Step 7 — fog-aware AI
1. Add `FindKnownTarget(aiShip)` that returns the nearest enemy with a known cell in the AI's own fog, or null.
2. If null, move toward the map center; do not attack.
3. Attacks go through `ResolveAttack`, which already enforces fog.
4. The AI acts for each of its living ships, not only the first.
5. Verify: hide your ships and confirm the AI stops homing on them; enter its sensor range and confirm it reacts.

---

## Suggested file list

**Existing, in play:** `Match/MatchState.cs`, `PlayerState.cs`, `DeploymentService.cs`; `FogOfWar/FogState.cs`, `FogGrid.cs`, `FogManager.cs`; `Grid/GridManager.cs`; `Turns/TurnManager.cs`, `Phase.cs`; `Grid/TestShipController.cs`; `AI/AIController.cs` (Step 7 only).
**New:** `FogOfWar/VisionResolver.cs` (static; Halo and Cone geometry plus detection). Split it into two files only if it grows.

`AGENTS.md` lists four script folders; the repo now also has `AI/`, `FogOfWar/` and `Match/`. Update that line.

---

## What NOT to build yet

- Mines, plane launches, or any real `Staging` action.
- Defense saving throws, armor applied to damage, `sideEffectId` execution, ammo/use consumption, recharge (Milestone 6).
- Click-to-select ships, weapon or defense selection UI, production UI or art.
- Ghost or last-known markers, scan-coverage overlays, line-of-sight blocking.
- `TileType` and predefined map layouts (see the open decision below).
- A combat reveal hook in `ResolveAttack`.
- A generic ability framework, `CombatResolver` or `GridView` extraction (only when a change actually needs it), a ScriptableObject data pipeline, networking, save/load.
- Anything to make `Marked` and `Identified` behave differently beyond what already exists.

---

## Open decision

The previous plan listed **map variety** (`TileType`, `Channel`, `Islands` layouts) inside Milestone 5. It does not depend on fog, so this doc **defers it**. Confirm, or move it back in.

---

## Known issues carried in

- The AI targets `TestShip` (your first ship) by global knowledge, moves one tile per turn regardless of range, and never controls its Athena. Step 7 addresses the first and third; the second is out of scope.
- No game-over handling when a fleet is empty. Only guards prevent exceptions.
- `Marked` rarely appears from passive vision with current ship data, because Absolute layers cover the Sensor layers while surfaced. It comes mainly from a submerged Wolf and from active scans.

---

## Next milestone preview

**Milestone 6 — Combat resolution:** apply armor, resolve defense saving throws, consume ammo and uses, process recharge, and execute defense side effects such as Crash Dive. Complete the remaining ship cards, then decide whether map variety becomes its own milestone.
