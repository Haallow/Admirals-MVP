# Admirals — Implementation Status and Code Cleanup Audit

**Date:** 2026-09-24  
**Unity version:** 6000.5.10f1  
**Branch:** feature/grid-system  
**Source authoritative:** Yes — every classification below is verified against the actual source files.

---

## Table of Contents

1. [Main Status Table](#1-main-status-table)
2. [Detailed Implementation Status](#2-detailed-implementation-status)
3. [Redundant and Potentially Removable Code](#3-redundant-and-potentially-removable-code)
4. [Debugging and Verification Code](#4-debugging-and-verification-code)
5. [Placeholder Art and Visualization](#5-placeholder-art-and-visualization)
6. [Hardcoded Prototype Data](#6-hardcoded-prototype-data)
7. [Duplicate or Diverging Logic](#7-duplicate-or-diverging-logic)
8. [Dead Code and Unused Members](#8-dead-code-and-unused-members)
9. [Prototype / Test Code](#9-prototype--test-code)
10. [Architecture Sanitation](#10-architecture-sanitation)
11. [Code Trimming Plan](#11-code-trimming-plan)
12. [Final Sanitation Checklist](#12-final-sanitation-checklist)
13. [Cleanup Dependency Order](#13-cleanup-dependency-order)
14. [Executive Summary](#14-executive-summary)

---

## 1. Main Status Table

| System | Status | Actually Implemented | Missing | Temporary/Prototype Code | Cleanup Later |
|---|---|---|---|---|---|
| **Grid** | IMPLEMENTED | `GridManager`, `Tile`, `FootprintUtil`, `GridView` | — | Gizmo markers, `DebugRecomputeFog` | Remove `TestShip`/`ObstructionShip` properties, remove `DebugRecomputeFog` |
| **Terrain** | IMPLEMENTED | `TerrainType`, `Tile.SetTerrain`, `MapDefinition` loads into runtime tiles | Movement terrain cost not consumed by `MoveShip` legacy path | Terrain Gizmo coloring in `GridView` | Replace terrain Gizmos with final art |
| **MapDefinition** | IMPLEMENTED | `MapDefinition` ScriptableObject, sparse terrain entries, `OnValidate` normalization | No MapDefinition asset in `Assets/Data/` — grid defaults to all Normal | TestMap.asset in `Assets/Scripts/Grid/` | Move map assets to `Assets/Data/` when terrain is used |
| **Movement** | IMPLEMENTED | Provisional move + Dijkstra (keyboard and pointer/drag); `Space` commits, `Escape`/`C` cancels | Terrain cost not wired into `MoveShip` legacy path used by AI | `MoveShip` legacy Chebyshev method | Remove or retire `MoveShip` after AI migrates to provisional system |
| **Dijkstra** | IMPLEMENTED | `GridPathfinder.FindPath`, `FindReachableCells`, 8-direction, terrain cost, occupancy | — | Reachable-anchor visualization in `GridView.DrawMovementRanges` | Keep pathfinder; remove or evolve visualization |
| **Provisional Movement** | IMPLEMENTED | `ProvisionalMovementState`, `GridManager.PreviewMove`, `ConfirmProvisionalMovement`, `CancelProvisionalMovement` | Multi-ship AI use | Provisional footprint Gizmos | Replace Gizmos with production movement UI |
| **Keyboard Movement** | IMPLEMENTED | Arrow keys preview/commit; `Space` commits and advances; `Q`/`E` rotate | — | All in `TestShipController` (prototype controller) | Replace with production input |
| **Pointer/Drag Input** | IMPLEMENTED | Click-to-press, drag to grid cell, release holds provisional | — | All in `TestShipController` | Replace with production input |
| **Ship Rotation** | IMPLEMENTED | `FootprintUtil.RotateOffsets` (0/90/180/270), validated via `CanPlaceShip` | — | `Q`/`E` in `TestShipController` | Replace with production input |
| **Fog of War** | IMPLEMENTED | `FogManager`, `FogGrid`, `FogState`, `VisionResolver` | Player-facing fog UI | All visualization is Gizmo-only | Replace Gizmos with production fog UI |
| **Passive Detection** | IMPLEMENTED | Halo and Cone passive layers, Chebyshev hull-cell range, `FogState.Identified`/`Marked`, rebuilt each Search | Terrain LOS (Phase 9A vision only; see note) | Halo Gizmo (`DrawDebugHalos`) uses anchor distance, not hull-cell distance — approximate | Correct halo Gizmo or replace with production art |
| **Active Scanning** | IMPLEMENTED | Player activates with `S`, `Q`/`E` rotate, `Enter` confirms; `ActiveScanPreviewState` + cone Gizmo | Active scan does not iterate all living ships; only the currently selected ship activates | Cone Gizmo (`DrawDebugCones`); active scan via keyboard | Replace with production scan UI |
| **Cone Scanning** | IMPLEMENTED | `VisionResolver.GetConeCells`, supercover LOS per cell, bow-derived forward | — | Cone Gizmo is visualization surface | Replace Gizmo with production scan overlay |
| **Cone Pivot** | IMPLEMENTED | `ActiveScanPreviewState.Rotate`, `GridManager.RotateActiveScan` | — | `Q`/`E` keys in `TestShipController.HandleActiveScanInput` | Keep logic; replace key bindings |
| **Vision LOS (Phase 9A)** | IMPLEMENTED | `VisionResolver.TryGetFirstBlockingCell`, supercover Bresenham; `VisionScanResult` captures blocked cells with blocker coords | — | Blocked-cell Gizmo coloring in `DrawDebugCones` | Keep LOS; replace blocked-cell visualization |
| **Combat — Attack Validation** | IMPLEMENTED | Dead-check, charge-readiness, domain, nearest-cell range, fog gate, all in `CombatResolver.ResolveAttack` | Attack line-of-fire (Phase 9B) | `Debug.Log` on every rejection and outcome | Replace logs with production feedback |
| **Combat — Attack Resolution** | IMPLEMENTED | d20 `RollWeapon`, tier damage, destroyed-ship cleanup | Armor, defense saves, ammo consumption, recharge, side effects | `Debug.Log` on outcome and destruction | Implement armor/defense later; remove logs |
| **Combat — Line of Fire** | PLANNED / NOT IMPLEMENTED | — | Phase 9B not started; `CombatResolver` has no LOS check | — | Add to `CombatResolver` after Phase 9A is confirmed |
| **AI** | PARTIALLY IMPLEMENTED | Moves and attacks for `PlayerB.ships[0]` only; greedy weapon selection; calls `ResolveAttack` | Fog-aware target selection, multi-ship control, center-fallback movement, Search scanning | All of `AIController.cs` | Replace with proper fog-aware AI |
| **Deployment** | IMPLEMENTED | `DeploymentService.DeployAll`, `CanPlaceShip` validation now present, hardcoded anchors | Player-controlled deployment phase | Hardcoded anchor values | Eventually support player deployment |
| **Turn Management** | IMPLEMENTED | `TurnManager.AdvancePhase`, five phases, `PhaseChanged` event, player switching at End | — | `LogState` per phase advance | Replace console log with production HUD indicator |
| **Visualization** | PROTOTYPE / TEMPORARY | All visualization is Gizmo-only in `GridView.OnDrawGizmos` | All production rendering: sprites, UI, HUD | Entire `GridView` is Gizmo-based | Replace entirely with production art + UI |
| **UI** | PLANNED / NOT IMPLEMENTED | None | Fog/scan toggles, health bars, phase/ship/weapon indicators, movement preview UI | — | Full UI system to be built |
| **Art / Assets** | PLANNED / NOT IMPLEMENTED | No sprites, textures, prefabs, or models in repository | All production art | — | All art to be added |
| **Match / Game-Over** | PARTIALLY IMPLEMENTED | Destroyed ships removed from grid and live fleet; no win check | Win-condition/game-over flow | Dead-ship log in `CombatResolver` | Add after core gameplay systems complete |
| **Staging Actions** | PLANNED / NOT IMPLEMENTED | Phase enum includes `Staging`; `HandlePhaseChanged` processes it but comment notes nothing is built | Mines, planes, repair ship | — | Implement when Milestone 6+ scope is decided |

> **Phase 9A note:** `VisionResolver.TryGetFirstBlockingCell` (supercover Bresenham LOS) is implemented. It is called during passive and active fog scans. `DrawDebugCones` in `GridView` also calls it for blocked-cell coloring. Phase 9A is functionally implemented but the halo Gizmo remains approximate.

---

## 2. Detailed Implementation Status

### 2.1 Grid

**Current implementation:**  
`GridManager` builds a `Dictionary<Vector2Int, Tile>` of the configured dimensions (default 30×15). It owns authoritative tile occupancy, provides placement validation (`CanPlaceShip`), atomic movement (`MoveShip`, `PreviewMove`, `ConfirmProvisionalMovement`), terrain queries, and wires phase events from `TurnManager` to fog operations. `CombatResolver` and `GridView` are separate collaborators created by `GridManager.Awake`.

**Missing functionality:**  
No in-editor map-paint tools. No non-rectangular board support (`Tile.IsValid` is reserved but unused). No runtime map switching.

**Partial functionality:**  
Terrain cost is stored and read by `GridPathfinder`, but `MoveShip` (the legacy Chebyshev path used by `AIController`) does not consume terrain cost. This means the AI can cross costly terrain freely. The provisional path through `PreviewMove` and Dijkstra does enforce terrain cost.

**Temporary/prototype functionality:**  
`GridManager.TestShip` and `GridManager.ObstructionShip` are first-ship convenience properties (`match.playerA.ships[0]` and `match.playerB.ships[0]`) documented as temporary and used exclusively by `AIController`. `GridManager.DebugRecomputeFog` is a `[ContextMenu]` method documented as safe to remove.

**Future cleanup:**  
- Remove `TestShip` and `ObstructionShip` properties after AI is rewritten to use `MatchState` directly.
- Remove `DebugRecomputeFog` [ContextMenu] after fog verification is complete.
- Remove or retire `MoveShip` legacy method after AI migrates to provisional movement.

---

### 2.2 Terrain

**Current implementation:**  
`TerrainType` (Normal/Costly/Impassable) is stored in each `Tile` via `SetTerrain`. `MapDefinition` is a `ScriptableObject` that holds sparse coordinate-to-terrain entries; `GridManager.BuildGrid` loads it. Terrain queries (`GetTerrainType`, `IsTerrainPassable`, `GetTerrainMovementCost`) are available. `GridPathfinder` consumes terrain cost. Impassable terrain blocks movement paths and vision LOS.

**Missing functionality:**  
No `MapDefinition` asset file exists in `Assets/Data/` — `Assets/Scripts/Grid/TestMap.asset` exists but its contents are unknown without Unity Inspector access. Without a map asset assigned, the entire board defaults to `Normal`.

**Partial functionality:**  
Terrain cost is calculated by `GridPathfinder` but not by the legacy `MoveShip` Chebyshev method.

**Temporary/prototype functionality:**  
Terrain is currently visualized only via colored cubes in `GridView.DrawTerrain` (orange for Costly, dark grey for Impassable). This is Gizmo-based and scene-view only.

**Future cleanup:**  
Replace terrain Gizmo coloring with final tile art assets. Retire `DrawTerrain` when real tile rendering exists.

---

### 2.3 Movement

**Current implementation:**  
Provisional movement: `GridManager.PreviewMove` calculates a Dijkstra path, validates the footprint, and stores a `ProvisionalMovementState`. `ConfirmProvisionalMovement` validates all previews together and commits atomically. `CancelProvisionalMovement` discards all previews. Keyboard (arrows/Q/E) and pointer (click-drag) both feed `PreviewMove`. On entering `Move`, `HandlePhaseChanged` snapshots all current-player ships.

**Legacy movement:**  
`GridManager.MoveShip` remains — it validates by Chebyshev distance from `anchorAtTurnStart` then calls `CanPlaceShip`. It is used only by `AIController.DecideMove`. It does not use Dijkstra or terrain cost.

**Missing functionality:**  
AI does not use the provisional movement system. Richer movement UI (production input, animations).

**Temporary/prototype functionality:**  
All player movement input is in `TestShipController`, documented as throwaway. `DrawProvisionalShips` and `DrawMovementRanges` in `GridView` are TEMPORARY-labeled Gizmo visualizations.

**Future cleanup:**  
- Migrate `AIController` to provisional system, then retire `MoveShip`.
- Replace all Gizmo movement visualization with production UI.
- Remove `TestShipController` input class when production input replaces it.

---

### 2.4 Fog of War

**Current implementation:**  
`FogManager` owns two `FogGrid` instances (one per player). Passive detection is rebuilt from scratch each `Search` phase via `RecomputeAllPassive`. Active scans are player-triggered: the human player presses `S` to activate, `Q`/`E` to rotate, `Enter` to confirm. `VisionResolver` is stateless and handles Halo geometry, Cone geometry, domain filtering, `onlyWhileSurfaced`, dead-ship filtering, and supercover Bresenham LOS. `VisionScanResult` carries detected and blocked cells. Active marks clear at `End`. `FogGrid.GetState` returns the stronger of passive and active layers.

**Missing functionality:**  
- No player-facing fog UI (no UI at all).
- Active scan only activates for the currently selected ship; the plan calls for all living ships to be able to scan.
- `DrawDebugHalos` uses anchor-based distance, not hull-cell distance — it is an approximate visualization, not the authoritative detection result.
- Phase 9B attack line-of-fire not implemented.

**Partial functionality:**  
`FogState.Identified` and `FogState.Marked` are both written; no consumer currently behaves differently based on the two states. The attack gate treats both as known.

**Temporary/prototype functionality:**  
All fog visualization (cones, halos) is Gizmo-based, scene-view only. `FogManager` emits verbose `Debug.Log` lines on every scan summary and every detected/blocked cell.

**Future cleanup:**  
Replace Gizmo visualization with production fog overlay/UI. Remove or suppress verbose scan logs once fog behavior is verified. Correct `DrawDebugHalos` to use hull-cell distance, or replace entirely.

---

### 2.5 Combat

**Current implementation:**  
`CombatResolver.ResolveAttack` performs the full authoritative attack chain: dead-check → charge-readiness → domain match → nearest-cell Chebyshev range (across all attacker/target cell pairs) → fog gate (`IsTargetKnown`) → d20 roll → damage → destroyed-ship cleanup. `CombatResolver.FindChargeState` is a public helper also used by `AIController`. `CombatResolver.IsTargetKnown` checks the attacker's `FogGrid` for any known target cell.

**Missing functionality:**  
- Armor is stored (`ShipInstance.armor`) but never subtracted from damage.
- Defense profiles are defined but `ResolveAttack` does not invoke them.
- Ammo/uses are never decremented from `ChargeState.remaining`. Every weapon fires as if unlimited, despite `ChargeState.IsReady` checking `remaining != 0`.
- `ChargeState.turnsUntilRecharge` is initialized to 0 and never modified.
- `DefenseProfile.sideEffectId` strings are stored but no code reads or executes them.
- Phase 9B terrain line-of-fire check is not implemented in `CombatResolver`.

**Partial functionality:**  
`ChargeState.IsReady` checks `remaining != 0` and `turnsUntilRecharge == 0`, but since `remaining` is never decremented after a successful shot, weapons with finite ammo will always appear ready.

**Future cleanup:**  
Implement armor subtraction, defense resolution, ammo decrement, recharge tick, and sideEffect execution. Add Phase 9B line-of-fire check. Remove `Debug.Log` combat outcome messages once production feedback UI exists.

---

### 2.6 AI

**Current implementation:**  
`AIController` acts once per phase for `PlayerB` using the Update loop. It controls only `gridManager.ObstructionShip` (`match.playerB.ships[0]`). During `Move` it calls `gridManager.MoveShip` directly (Chebyshev, one step toward `gridManager.TestShip`). During `Battle` it scores weapons via `ExpectedValue` and calls `gridManager.Combat.ResolveAttack`. It does nothing during `Search`. It does not read `PlayerB`'s `FogGrid`.

**Missing functionality (planned):**  
- Use `FogManager.GetFogGrid(PlayerB)` to find known targets.
- Not chase or attack hidden targets.
- Move toward map center when no target is known.
- Act for every living AI ship, not only the first.
- Activate `Search` scans.
- Use provisional movement system.

**Partial functionality:**  
`AIController.CanFire` pre-checks charge, domain, and range before calling `ResolveAttack`. However its range check uses `attacker.anchor` rather than all attacker occupied cells — it diverges from `CombatResolver.ResolveAttack` which uses the minimum distance across all cell pairs. This means `CanFire` can report a shot as illegal even when `ResolveAttack` would accept it (or vice versa for very long ships with distant anchors).

**Temporary/prototype functionality:**  
The entire `AIController` class is documented as temporary prototype behavior. `[AI]` prefixed `Debug.Log` calls throughout.

**Future cleanup:**  
Rewrite `AIController` entirely once fog-aware AI is implemented. Remove temporary `[AI]` logs. Remove or rewrite `CanFire` to match `CombatResolver` range logic. `GridManager.TestShip` and `ObstructionShip` become removable once `AIController` uses `MatchState` directly.

---

### 2.7 Deployment

**Current implementation:**  
`DeploymentService.DeployAll` deploys Wolf + Athena for both players. `DeployFleet` now calls `CanPlaceShip` before `PlaceShip` (this was listed as a gap in `PROJECT_STATUS.md` but is now fixed in the source). Player A anchors at x=1, rotation 0. Player B anchors at x=`gridManager.width - 3`, rotation 180. Each ship is assigned `anchorAtTurnStart` at deploy time. `LogStatBlock` is called for each deployed ship.

**Missing functionality:**  
Player-controlled deployment phase. No deployment-phase UI. Roster is hardcoded in `GridManager.Start`.

**Temporary/prototype functionality:**  
Hardcoded roster and anchor values. `LogStatBlock` calls in `DeploymentService.DeployFleet` are verification helpers.

**Future cleanup:**  
Replace hardcoded roster/anchor with player-driven deployment. Remove `LogStatBlock` deployment logs once deployment is verified.

---

### 2.8 Turn Management

**Current implementation:**  
`TurnManager.AdvancePhase` cycles `Move → Staging → Search → Battle → End`, switches player at `End → Move`. Fires `PhaseChanged` event; `GridManager.HandlePhaseChanged` is the only subscriber. `LogState` emits a debug line on every phase change.

**Missing functionality:**  
Win condition/game-over detection. End-phase cooldowns. Search input gating (Space currently advances out of Search even without confirming a scan).

**Temporary/prototype functionality:**  
`TurnManager.LogState` (called in `Start` and `AdvancePhase`) is a debug console message.

**Future cleanup:**  
Replace `LogState` with production HUD phase indicator. Add win-check in `HandlePhaseChanged` for the End phase.

---

### 2.9 Ships and Data Model

**Current implementation:**  
`ShipInstance` is a plain `[Serializable]` C# class holding all runtime state. `ShipData` builds hardcoded Wolf, Athena, and SwordFish instances. `ShipFactory` dispatches to the correct builder. `ShipType` is the card enum.

**Missing functionality:**  
SwordFish is defined but never deployed. No ScriptableObject data pipeline (intentionally deferred per YAGNI). Armor not consumed. Defense execution not implemented.

**Partial functionality:**  
`DefenseProfile.sideEffectId` is stored as a string (`"BecomeSubSurfaceAndSkipNextMove"`) but no code reads or acts on it.

**Future cleanup:**  
Add SwordFish to the deployment roster when gameplay needs it. Implement defense execution. Consider ScriptableObject migration when the number of ships justifies it (YAGNI — do not migrate prematurely).

---

## 3. Redundant and Potentially Removable Code

| File | Class / Method / Field | Why It Appears Redundant | Still Referenced? | Safe to Remove Now? | Remove When | Replacement |
|---|---|---|---|---|---|---|
| `Assets/Scripts/Grid/GridManager.cs` | `TestShip` property | Only exists to give `AIController` first-ship access; `match.playerA.ships[0]` is equivalent | Yes — `AIController.cs` line 45 | No | After AI is rewritten to use `MatchState` directly | `gridManager.Match.playerA.ships[0]` or fog-aware search |
| `Assets/Scripts/Grid/GridManager.cs` | `ObstructionShip` property | Only exists for `AIController`; comment in source explicitly calls it temporary | Yes — `AIController.cs` lines 35, 44 | No | After AI is rewritten | `gridManager.Match.playerB.ships[0]` or fog-aware search |
| `Assets/Scripts/Grid/GridManager.cs` | `MoveShip(ship, newAnchor, newRotation)` | Chebyshev-only legacy movement no longer used by human input; provisional system replaced it | Yes — `AIController.cs` line 67 | No | After AI migrates to provisional movement | `PreviewMove` + `ConfirmProvisionalMovement` |
| `Assets/Scripts/Grid/GridManager.cs` | `DebugRecomputeFog` [ContextMenu] | Developer guide explicitly lists it as safe to remove; not part of phase flow | Yes — invokable via Inspector context menu | Yes — P0 | Immediately | None needed |
| `Assets/Scripts/AI/AIController.cs` | `CanFire(attacker, target, weapon)` | Mirrors `CombatResolver.ResolveAttack` pre-checks for weapon scoring; duplicates charge/domain/range logic; range calculation uses `attacker.anchor` only (diverges from authoritative nearest-cell logic in `CombatResolver`) | Yes — `DecideAttack` uses it | No | After AI is rewritten with fog-awareness | Inline fog-aware weapon scoring, or a `CombatResolver` utility method for scoring |
| `Assets/Scripts/Ships/ShipData.cs` | `BuildSwordFishClass` | SwordFish is in the factory but never in any deployment roster; ship has no defenses or vision layers | Yes — `ShipFactory` dispatches to it; `ShipType.SwordFishClass` is referenced | No | When SwordFish is either added to the game or confirmed cut | Keep if SwordFish is planned; complete data (add defenses/vision) first |
| `docs/PROJECT_STATUS.md` | Entire file | Describes pre-Milestone-5 state (fog as empty placeholder, no VisionResolver, no fleet model); key facts are now wrong (e.g. DeployFleet skips CanPlaceShip — this was fixed) | Yes — referenced in some docs | No — stale data | Update alongside next major status doc update | Update `PROJECT_STATUS.md` content or retire it as superseded by `DEVELOPER_GUIDE.md` |
| `docs/Agent_Context_Prompt.md` | Section "Active scan is automatic" | Describes active scan as automatic per-Search; this was changed — scan is now player-activated | Yes — used as context for agents | No | When doc maintenance pass happens | Update description |

---

## 4. Debugging and Verification Code

### 4.1 `GridManager.DebugRecomputeFog` [ContextMenu]

**File:** `Assets/Scripts/Grid/GridManager.cs` (lines 497–506)  
**Classification:** REMOVE AFTER VERIFICATION  
**Reason:** Manually recomputes passive fog and logs per-cell fog state for each ship. Not part of any phase flow. Developer guide explicitly identifies it as removable. Has no test or gameplay dependency. Safe to remove immediately.

---

### 4.2 `TurnManager.LogState`

**File:** `Assets/Scripts/Turns/TurnManager.cs` (called from `Start` and `AdvancePhase`)  
**Classification:** REPLACE WITH PRODUCTION UI  
**Reason:** Prints `"{player} turn: {phase} phase"` to the console on every phase advance. Provides useful prototype state confirmation. Should remain until a production HUD phase indicator exists.

---

### 4.3 `ShipInstance.LogStatBlock`

**File:** `Assets/Scripts/Ships/ShipInstance.cs` (lines 79–103)  
**Classification:** KEEP UNTIL FEATURE COMPLETE  
**Reason:** Called by `DeploymentService.DeployFleet` on every deployment. Currently the only way to verify ship data is loaded correctly after a refactor. Should remain until deployment is verified through a production UI or automated test. Do not remove while ship stats are still in active development.

---

### 4.4 `FogManager` scan summary and per-cell logs

**File:** `Assets/Scripts/FogOfWar/FogManager.cs`  
**Classification:** KEEP UNTIL FEATURE COMPLETE (then OPTIONAL)  
**Reason:** Two categories of log present:
- `LogScanSummary` — emits one line per scan layer summarizing detected cells, applied fog state, range, detects, context.
- Per-cell `Debug.Log` inside `RecomputePassive` and `RunActiveSearch` — one line per detected cell and one per blocked cell.
- `LogBlockedCells` — emits one line per blocked cell.

These logs are verbose but remain the only verification mechanism for fog correctness until a production fog UI exists. They should remain while fog behavior is still evolving. Once fog UI exists and behavior is confirmed, move toward optional/suppressible logging (a bool flag or stripping in release builds).

---

### 4.5 `CombatResolver` outcome and rejection logs

**File:** `Assets/Scripts/Combat/CombatResolver.cs`  
**Classification:** KEEP UNTIL FEATURE COMPLETE  
**Reason:**  
- `"Target not known."` — meaningful feedback now; will need a production replacement.
- `"{attacker} fires {weapon} at {target}: {outcome}"` — only combat feedback currently visible.
- `"{target} destroyed!"` — only destroyed-ship feedback.
- `RollWeapon` warning log — a legitimate error guard for misconfigured roll tiers; keep.

Remove or replace with production UI once health bars, combat log, and notifications exist.

---

### 4.6 `CombatResolver.RollWeapon` error log

**File:** `Assets/Scripts/Combat/CombatResolver.cs` (line 80)  
**Classification:** KEEP  
**Reason:** `Debug.LogWarning` fires when a d20 roll does not match any configured tier. This is a data-integrity guard for ship card configuration errors. Should remain permanently or be converted to a production-appropriate error handler.

---

### 4.7 `GridManager.PlaceShip` assertion

**File:** `Assets/Scripts/Grid/GridManager.cs` (line 393)  
**Classification:** KEEP  
**Reason:** `Debug.Assert(IsInBounds(cell), ...)` guards against out-of-bounds placement, which would silently fail without it. This is a legitimate programming contract assertion. Keep permanently or replace with an exception.

---

### 4.8 `GridManager.MoveShip` rejection logs

**File:** `Assets/Scripts/Grid/GridManager.cs` (lines 444, 450)  
**Classification:** KEEP UNTIL FEATURE COMPLETE  
**Reason:** Emitted when the AI's `MoveShip` call is rejected for range or placement reasons. Useful while AI movement is still prototype. Remove when AI uses the provisional system (which already provides its own rejection feedback).

---

### 4.9 `GridManager.HandlePhaseChanged` provisional-confirmation warning

**File:** `Assets/Scripts/Grid/GridManager.cs` (line 480)  
**Classification:** KEEP UNTIL FEATURE COMPLETE  
**Reason:** `Debug.LogWarning("Provisional movement confirmation failed; no ships were moved.")` fires if the phase advances to Staging while any provisional state is invalid. Legitimate production-path warning. Keep; consider surfacing it to the player via HUD rather than console.

---

### 4.10 `TestShipController` movement/scan/input logs

**File:** `Assets/Scripts/Grid/TestShipController.cs`  
**Classification:** REMOVE WITH CONTROLLER  
**Reason:** All console logs in `TestShipController` (`"Switched to ship"`, `"Domain toggled"`, `"Provisional movement cancelled"`, `"Move preview rejected"`, `"Previewed move to"`, `"Active scan confirmed"`, `"No valid enemy target"`, etc.) are prototype feedback. They are part of the controller class, which is itself temporary. They will be removed when `TestShipController` is replaced by production input.

---

### 4.11 `AIController` [AI]-prefixed logs

**File:** `Assets/Scripts/AI/AIController.cs`  
**Classification:** REMOVE WITH AI REWRITE  
**Reason:** `[AI] Moved to`, `[AI] Move blocked`, `[AI] No valid weapon`, `[AI] Fired` logs are prototype verification only. They will be removed when `AIController` is rewritten.

---

### 4.12 `GridView.DrawDebugCones` — cone Gizmo

**File:** `Assets/Scripts/Grid/GridView.cs`  
**Classification:** REPLACE WITH PRODUCTION ART  
**Reason:** Per the roadmap comment in `GridView.cs`: "DrawDebugCones/DrawDebugHalos are no longer 'debug' in the throwaway sense — per the roadmap they're becoming the real toggleable fog/scan visualization." The cone Gizmo currently draws yellow/red wireframe cubes for the active scan preview cone, using `VisionResolver.TryGetFirstBlockingCell` to color blocked cells. This is the intended visualization surface for the production scan preview. Do not remove; replace with production scan overlay.

---

### 4.13 `GridView.DrawDebugHalos` — halo Gizmo

**File:** `Assets/Scripts/Grid/GridView.cs`  
**Classification:** REPLACE WITH PRODUCTION ART  
**Reason:** Same roadmap intent as cones. However, this Gizmo is currently **approximate** — it uses `ship.anchor` as the range origin, not all occupied hull cells. `VisionResolver` uses the nearest hull cell. The Gizmo therefore overestimates detection range for multi-cell ships near the anchor. This discrepancy should be corrected before the Gizmo becomes a production visualization, or the Gizmo should be replaced entirely.

---

## 5. Placeholder Art and Visualization

No production art, sprites, prefabs, or materials exist in this repository. All gameplay visualization is Gizmo-based in `GridView.OnDrawGizmos`. The following table maps each Gizmo to its intended final replacement.

| Location | Current Placeholder | Purpose | Final Replacement | Removal Point |
|---|---|---|---|---|
| `GridView.OnDrawGizmos` ship cubes | Cyan/red `DrawCube` per occupied tile | Identifies ship positions and ownership | Player-specific ship sprites or models on the board | Stage D — when ship sprites are added |
| `GridView.DrawProvisionalShips` | Semi-transparent cyan/red cubes at preview positions | Shows provisional movement destination | Production movement preview (highlighted path, ghost ship) | Stage D — when production movement UI is built |
| `GridView.DrawMovementRanges` | Translucent cyan/red cells over reachable anchors | Shows Dijkstra reachable area for moving ship | Production movement range overlay (shaded cells or hex-highlight) | Stage D — when production movement UI is built |
| `GridView.DrawTerrain` | Orange cubes (Costly), dark grey cubes (Impassable), grey wireframe (Normal) | Displays terrain type per tile | Final tile art per terrain type | Stage D — when terrain tile art is added |
| `GridView.DrawStartingZones` | Blue/red translucent zone overlay | Marks player starting areas | Final zone indicator art (border or shading) or removed if deployment is player-controlled | Stage D |
| `GridView.DrawDebugCones` | Yellow/red wireframe cells for active scan cone | Shows active scan preview including LOS-blocked cells | Production scan preview overlay with blocked-cell indicator | Stage D — when production scan UI is built; keep LOS coloring logic |
| `GridView.DrawDebugHalos` | Cyan wireframe cells around each ship's passive halo range | Shows passive detection coverage | Production fog visualization (fog-of-war overlay, visibility shading) | Stage D — when production fog UI is built |

---

## 6. Hardcoded Prototype Data

### 6.1 Wolf Class `movementRange = 6`

**File:** `Assets/Scripts/Ships/ShipData.cs` (line 11)  
**Comment in source:** `// for testing`  
**Recommendation:** Replace with the design-document value when final ship stats are confirmed. This is prototype tuning, not a real stat. The value makes the Wolf extremely mobile for testing purposes.  
**Action:** Move to real game data when ship stats are finalized. Do not convert to a ScriptableObject field prematurely.

---

### 6.2 `ShipInstance.movementRange` default = 3

**File:** `Assets/Scripts/Ships/ShipInstance.cs` (line 22)  
**Comment in source:** `// placeholder; real numbers come from the design doc later`  
**Recommendation:** This default is only a fallback if `ShipData` does not set the value. Both Wolf (=6) and Athena (=4) builders set their own values. The default is harmless but misleading. Replace with `0` as a sentinel when final stats are in.  
**Action:** P3 optional; consider removing the default value once all ships are properly statted.

---

### 6.3 Hardcoded deployment anchors

**File:** `Assets/Scripts/Match/DeploymentService.cs`  
**Values:** Player A anchor x=1, rotation 0; Player B anchor x=`gridManager.width - 3`, rotation 180. Y increments by 3 per ship.  
**Recommendation:** Keep for now. These are sensible defaults for a 30×15 board. They should be replaced when player-controlled deployment is implemented.  
**Action:** P2 — replace with deployment phase when that feature is built.

---

### 6.4 Hardcoded fleet roster in `GridManager.Start`

**File:** `Assets/Scripts/Grid/GridManager.cs` (lines 53–54)  
**Values:** Both players receive `[WolfClass, AthenaClass]` hardcoded.  
**Recommendation:** Keep for now. This is the prototype match setup. Replace with a fleet-selection or configuration system when deployment and game setup are implemented.  
**Action:** P2 — replace with match/lobby configuration when that feature is built.

---

### 6.5 Wolf Absolute Vision range = 4 (formerly 1 in Milestone 4 plan)

**File:** `Assets/Scripts/Ships/ShipData.cs` (Wolf "Default Absolute Vision" VisionLayer, range=4)  
**Note:** Milestone 4 plan showed range=1 for the Default Absolute Vision layer. Current source has range=4. This may be a deliberate design change or a testing value.  
**Recommendation:** Verify against design document. Needs manual confirmation.  
**Action:** Confirm with design; if range=4 is correct, no change needed.

---

### 6.6 `DefenseProfile.sideEffectId` strings

**File:** `Assets/Scripts/Ships/ShipData.cs`  
**Values:** `"BecomeSubSurfaceAndSkipNextMove"` on Crash Dive.  
**Current state:** Stored as a string; no code reads or acts on it.  
**Recommendation:** Keep the data. The string is the planned integration point for defense side effects. Implement the execution logic when defense resolution is built (Milestone 6+).  
**Action:** P1 — implement alongside defense resolution.

---

### 6.7 `ChargeState` ammo never decremented

**File:** `Assets/Scripts/Combat/CombatResolver.cs`  
**Issue:** `ResolveAttack` does not decrement `weaponCharge.remaining` after a successful attack. All weapons with finite ammo (Wolf MRK-1 Torpedo: 4, Spear: 2, Hippocampus: 3; Athena Anti-Ship Missile: 2, Anti-Sub Rocket: 2) fire without limit. `ChargeState.IsReady` checks `remaining != 0`, so weapons initialized with ammo always pass.  
**Recommendation:** Keep the data structures as-is. Implement ammo decrement in `CombatResolver.ResolveAttack` as part of Milestone 6 combat resolution.  
**Action:** P1 — implement decrement alongside armor/defense resolution.

---

## 7. Duplicate or Diverging Logic

### 7.1 `AIController.CanFire` vs `CombatResolver.ResolveAttack` — range calculation divergence

**Authoritative implementation:**  
`CombatResolver.ResolveAttack` (lines 34–40) — calculates minimum Chebyshev distance across all pairs of attacker-occupied cells × target-occupied cells.

**Duplicate:**  
`AIController.CanFire` (lines 123–131) — calculates minimum Chebyshev distance from `attacker.anchor` to each target-occupied cell only. Does not iterate attacker occupied cells.

**Why duplication exists:**  
`CanFire` is a pre-flight weapon scoring check used by `DecideAttack` before calling `ResolveAttack`. It was written as a simplified mirror of the combat validation checks.

**Divergence consequence:**  
For a two-cell ship (Wolf: `(anchor, anchor+(1,0))`), the anchor-based range check may report a shot as out-of-range when `ResolveAttack`'s nearest-cell check would accept it (the second hull cell could be closer to the target). It could also incorrectly report a shot as in-range if the anchor is closer than the nearest hull cell to the target. In practice this is a small error for the Wolf's 1×2 footprint but would become more significant for larger ships.

**Should it be removed/replaced:**  
Yes. `CanFire` should either: (a) be removed and the weapon-scoring loop in `DecideAttack` should query `CombatResolver` for range validity, or (b) be corrected to use the same min-occupied-cell logic. Option (a) is cleaner and eliminates the divergence entirely.

**When:**  
When AI is rewritten (P1).

---

### 7.2 `TestShipController.HandleAttackInput` input-level checks vs `CombatResolver.ResolveAttack`

**Authoritative implementation:**  
`CombatResolver.ResolveAttack` — dead check, charge, domain, range, fog, d20, damage.

**Input-level checks in `TestShipController`:**  
`HandleAttackInput` (lines 183–196) checks: (a) tile exists, (b) occupant exists, (c) occupant is not friendly, (d) `selectedWeaponIndex` is in bounds.

**Why duplication exists:**  
These are input-level guards to prevent calling `ResolveAttack` with obviously invalid parameters. They are not combat rules — they are UI filtering.

**Should it be removed/replaced:**  
No. These are intentional input-level checks, not duplicated business logic. The developer guide explicitly calls this pattern out: "Do not recommend removing harmless input-level checks merely because an authoritative validation exists elsewhere." Keep as-is.

---

### 7.3 `AIController.CanFire` charge/domain checks vs `CombatResolver.ResolveAttack`

**Authoritative implementation:**  
`CombatResolver.ResolveAttack` checks charge readiness via `FindChargeState` and domain match.

**In `CanFire`:**  
Lines 113–121 repeat the same charge-readiness and domain checks.

**Why duplication exists:**  
Pre-flight weapon selection: the AI needs to filter weapons before scoring to avoid scoring uncharged or domain-mismatched weapons.

**Assessment:**  
The charge and domain duplication here is acceptable and intentional — it is a read-only pre-check, not a second execution path. The range check (above, §7.1) is the problematic divergence. The charge and domain checks are not harmful.

**Should it be removed/replaced:**  
No action needed on charge/domain. Fix the range check only.

---

### 7.4 `GridManager.HandlePhaseChanged` provisional snapshot vs `TestShipController` anchor snapshot

**GridManager side:**  
`HandlePhaseChanged(Phase.Move)` creates a `ProvisionalMovementState` for each current-player ship, which stores `OriginalAnchor` and `OriginalRotation`.

**TestShipController side:**  
`HandleMoveInput` block (lines 88–94) snapshots `anchorAtTurnStart` for all Player A ships when the phase first becomes `Move`.

**Why duplication exists:**  
`ProvisionalMovementState.OriginalAnchor` is the provisional system's snapshot (used for path validation from the movement start position). `anchorAtTurnStart` on `ShipInstance` is the legacy movement budget origin used by `MoveShip` (legacy Chebyshev method). They serve different consumers.

**Assessment:**  
This is not a duplication of business logic — the two snapshots serve different systems. `anchorAtTurnStart` can eventually be removed when `MoveShip` is retired and the AI migrates to provisional movement. Until then, both must be maintained.

**When:**  
Remove `anchorAtTurnStart` snapshot logic from `TestShipController` after `MoveShip` is retired (P1).

---

## 8. Dead Code and Unused Members

### 8.1 `Tile.IsValid` flag

**File:** `Assets/Scripts/Grid/Tile.cs`  
**Field:** `public bool IsValid = true;`  
**Status:** Set to `true` at construction, never modified, never read by any current system (`GridManager`, `GridPathfinder`, `VisionResolver`, `CombatResolver`). Comment says "reserved for non-rectangular boards later."  
**Evidence:** Searched all `.cs` files — no code reads `IsValid` except the field declaration itself.  
**Safe to remove now?** Not recommended. It is a reserved extension point documented in the architecture. Remove only when the project explicitly decides against non-rectangular boards, or when implementing non-rectangular support.  
**Action:** P3 — leave until the project's non-rectangular board decision is made.

---

### 8.2 `ShipInstance.movementRange` default = 3

**File:** `Assets/Scripts/Ships/ShipInstance.cs` line 22  
**Status:** Default value; overwritten by every `ShipData` builder. The default is only reached if a ship is created without going through `ShipData`. No current code path does this.  
**Safe to remove default?** Keep as a safety default. P3 consideration: change to 0 as a more obvious "uninitialized" sentinel once all ships are properly statted.

---

### 8.3 `DefenseProfile.sideEffectId` — no execution logic

**File:** `Assets/Scripts/Combat/DefenseProfile.cs`  
**Status:** Field present. Only value in use: `"BecomeSubSurfaceAndSkipNextMove"` on Wolf's Crash Dive. No code in `CombatResolver` reads or executes `sideEffectId`. Defense resolution itself is not implemented.  
**Assessment:** Not dead code — this is planned data awaiting its execution layer. Keep.  
**Action:** P1 — implement alongside defense resolution in Milestone 6.

---

### 8.4 `ChargeState.turnsUntilRecharge`

**File:** `Assets/Scripts/Combat/ChargeState.cs`  
**Status:** Field initialized to 0 in all `ChargeState` constructors. Never modified by any code. `ChargeState.IsReady` checks it (`turnsUntilRecharge == 0`), which always returns true since it is never incremented.  
**Assessment:** Not dead — it is planned recharge state. The recharge tick logic simply has not been implemented. Keep.  
**Action:** P1 — implement recharge tick in Milestone 6.

---

### 8.5 `Assets/Settings/InputSystem_Actions.inputactions`

**File:** `Assets/Settings/InputSystem_Actions.inputactions`  
**Status:** A Unity Input System actions asset. All current input is handled through `Input.GetKeyDown`/`Input.GetMouseButton` in `TestShipController` — the legacy input system. The Input System package asset appears unused at runtime.  
**Needs manual Unity Inspector/scene verification** — the asset may have been auto-generated by Unity during project setup and may not be wired to any script or Player Input component.  
**Action:** P3 — verify in Unity Inspector whether any script or scene object references this asset. Remove if unused.

---

### 8.6 `Assets/_Recovery/` — three old scene files

**Files:** `Assets/_Recovery/0.unity`, `0 (1).unity`, `0 (2).unity`  
**Status:** These appear to be backup or recovered scene files from early project history. They are not referenced by the project build settings (which uses `Assets/Scenes/SampleScene.unity`).  
**Needs manual Unity build settings verification** — confirm these are not in the build and not referenced by any script.  
**Action:** P0 — if confirmed not in build settings and not referenced, delete. They are noise in the project.

---

### 8.7 `Assets/Data/` — empty folder

**Status:** The `Assets/Data/` directory exists but is empty. It was presumably intended for `MapDefinition` and other data assets.  
**Assessment:** Not removable (folder is a convention, not dead code), but `Assets/Scripts/Grid/TestMap.asset` should be moved here when terrain is in active use.  
**Action:** P2 — move `TestMap.asset` to `Assets/Data/` and create map assets there.

---

### 8.8 `SwordFishClass` — defined but never deployed

**Files:** `Assets/Scripts/Ships/ShipType.cs`, `ShipFactory.cs`, `ShipData.cs`  
**Status:** `SwordFishClass` is a valid `ShipType` enum value with a complete `BuildSwordFishClass` builder. However: it is not in either player's `fleetRoster` in `GridManager.Start`. The builder gives it no `defenses` or `visionLayers`.  
**Safe to remove?** No. It is a legitimate planned ship card. It should be completed (add defenses and vision) and added to the deployment roster when the game design calls for it.  
**Action:** P2 — complete the SwordFish card definition and add to roster when the design confirms it.

---

## 9. Prototype / Test Code

### 9.1 `TestShipController`

**File:** `Assets/Scripts/Grid/TestShipController.cs`  
**Still required?** Yes — currently the only Player A input path. Without it, Player A cannot move, rotate, attack, or perform active scans.  
**What depends on it?** All Player A gameplay actions: move, rotate, scan, attack, phase advance.  
**What will replace it?** Production input system with proper UI (weapon selector, phase advance button, scan activation).  
**When can it be removed?** Stage D — after production input replaces every behavior it provides.

---

### 9.2 `AIController`

**File:** `Assets/Scripts/AI/AIController.cs`  
**Still required?** Yes — currently the only Player B automation. Without it, Player B cannot act at all.  
**What depends on it?** Player B movement and attack during their turns.  
**What will replace it?** A rewritten fog-aware AI that controls all living Player B ships.  
**When can it be removed?** The current class should be rewritten in-place, not deleted. Remove prototype-specific logic (`CanFire` divergent range check, direct `MoveShip` call, `TestShip`/`ObstructionShip` references, `[AI]` logs) when rewriting.

---

### 9.3 `AIController.actedThisPhase` timing issue

**File:** `Assets/Scripts/AI/AIController.cs`  
**Issue:** `actedThisPhase` is set to `true` **after** `DecideMove`/`DecideAttack` execute. If an exception is thrown inside those methods, the flag remains `false` and the AI will retry on the next `Update` frame.  
**Developer guide note:** Lists "AI sets its 'acted this phase' flag before acting" as a known housekeeping item.  
**Still required?** Yes — fixing this requires only moving `actedThisPhase = true` to before the decision method calls.  
**When:** P1 — fix during AI rewrite.

---

### 9.4 `DeploymentService` stat block logging

**File:** `Assets/Scripts/Match/DeploymentService.cs`  
**Code:** `ship.LogStatBlock($"{type} [{player.owner}]")` — called for every ship deployed.  
**Still required?** Useful while ship stats are changing. Not required for gameplay.  
**What will replace it?** Potentially nothing explicit — once ship data is stable, deployment logs become noise.  
**When can it be removed?** P2 — after ship stats are finalized and the deployment system is verified.

---

### 9.5 `TestMap.asset` in `Assets/Scripts/Grid/`

**File:** `Assets/Scripts/Grid/TestMap.asset`  
**Status:** A `MapDefinition` ScriptableObject asset located inside the Scripts folder (unconventional location). Content not directly inspectable without Unity Editor; may contain test terrain entries.  
**Still required?** Possibly — it may be assigned to the `GridManager.mapDefinition` field in the scene. Needs manual Unity Inspector/scene verification.  
**When can it be removed/moved?** P2 — move to `Assets/Data/` and replace with final map assets when terrain is in active use.

---

## 10. Architecture Sanitation

### 10.1 `AIController` bypasses provisional movement

**Issue:** `AIController.DecideMove` calls `gridManager.MoveShip` (legacy Chebyshev) directly. This means the AI's movement does not go through the provisional system, does not consume terrain cost, does not integrate with `HandlePhaseChanged`'s provisional-snapshot creation, and does not participate in multi-ship atomic confirmation.  
**Intended architecture:** All movement should use `PreviewMove` → `ConfirmProvisionalMovement`. The `GridManager.HandlePhaseChanged(Move)` already creates provisional snapshots for all current-player ships.  
**Smallest fix:** Rewrite `DecideMove` to call `gridManager.PreviewMove` and let the existing `HandlePhaseChanged(Staging)` confirm it atomically, the same path the human player uses.

---

### 10.2 `AIController` reads global ship references instead of `MatchState`

**Issue:** `AIController` reads `gridManager.TestShip` and `gridManager.ObstructionShip` — shortcut properties that expose `match.playerA.ships[0]` and `match.playerB.ships[0]`. These are temporary accessors documented as such in the `GridManager` source. The AI should read from `gridManager.Match.playerB.ships` for its own ships and from its `FogGrid` for enemy targets.  
**Intended architecture:** AI uses fog-aware target selection from its own `FogGrid`; it reads its own fleet from `MatchState`.  
**Smallest fix:** Replace all `gridManager.TestShip`/`ObstructionShip` references with `gridManager.Match.playerA/playerB.ships[0]` as an interim step, then expand to full fog-aware multi-ship iteration.

---

### 10.3 `DrawDebugHalos` uses anchor-based range, not hull-cell range

**Issue:** `GridView.DrawDebugHalos` calculates halo coverage using `ship.anchor` as the single source point. `VisionResolver.IsWithinHalo` uses all occupied cells of the source ship. For a two-cell Wolf, the Gizmo draws a halo that is slightly smaller/differently positioned than the actual detection range.  
**Intended architecture:** `GridView` reads state but does not duplicate detection logic. The halo Gizmo should iterate `ship.GetOccupiedCells()` as sources, exactly as `VisionResolver` does.  
**Smallest fix:** Replace `ship.anchor` with `ship.GetOccupiedCells()` in `DrawDebugHalos`.

---

### 10.4 `WeaponProfile` constructor parameter ordering inconsistency

**Issue:** `WeaponProfile` constructor signature is `(id, ammo, targetDomain, rollTiers, weaponRange)` — `weaponRange` is last. All `ShipData` builders use named parameters (e.g. `weaponRange: 4`), so order does not matter in practice. But the field declaration order in `WeaponProfile.cs` has `weaponRange` after `rollTiers`, which is unusual — weapon range is a primary stat typically declared before the roll table.  
**Assessment:** No functional bug since named parameters are used. P3 cosmetic — reorder fields for readability if desired.

---

### 10.5 `FogManager.RecomputeAllPassive` does not call `RunActiveSearch` anymore

**Issue:** Earlier in development, `HandlePhaseChanged(Search)` called both `RecomputeAllPassive` and `RunActiveSearch(CurrentPlayer)` automatically. The current source (`GridManager.HandlePhaseChanged` lines 484–491) calls only `RecomputeAllPassive` on `Search`. `RunActiveSearch` is now called only from `GridManager.ConfirmActiveScan`, which requires player activation. This is an intentional design change (player-selected scan), but the `FogManager.RunActiveSearch` signature changed to accept explicit `bow` and `forward` parameters as a result. This is correct and clean — noting it here so it is not mistaken for missing behavior.

---

## 11. Code Trimming Plan

### Stage A: Safe Cleanup Now

Items that are clearly unused or dead and safe to remove immediately.

| File | Code | Reason | Dependency | Safe Removal Point | Replacement |
|---|---|---|---|---|---|
| `Assets/Scripts/Grid/GridManager.cs` | `DebugRecomputeFog` method and `[ContextMenu]` attribute | Explicitly listed in DEVELOPER_GUIDE as safe to remove; not in phase flow | None | Immediately | None |
| `Assets/_Recovery/` | `0.unity`, `0 (1).unity`, `0 (2).unity` | Backup/recovery scenes; not in build; noise | Needs manual Unity build-settings verification | After verifying not in build settings | None |

---

### Stage B: Cleanup After Current Implementation

Items that should remain until the currently planned AI rewrite and combat completion are done.

| File | Code | Reason | Dependency | Safe Removal Point | Replacement |
|---|---|---|---|---|---|
| `Assets/Scripts/Grid/GridManager.cs` | `TestShip` property | Temporary AI accessor | AI must stop using it | After AI uses `MatchState` directly | `gridManager.Match.playerA.ships[0]` or fog-aware search |
| `Assets/Scripts/Grid/GridManager.cs` | `ObstructionShip` property | Temporary AI accessor | AI must stop using it | After AI uses `MatchState` directly | `gridManager.Match.playerB.ships[0]` |
| `Assets/Scripts/Grid/GridManager.cs` | `MoveShip(ship, anchor, rotation)` legacy method | Only used by AI; provisional system replaced it for human input | AI must migrate to provisional movement | After AI uses `PreviewMove` | `PreviewMove` + `ConfirmProvisionalMovement` |
| `Assets/Scripts/AI/AIController.cs` | `CanFire` method | Divergent range logic; duplicates `CombatResolver` charge/domain/range checks with incorrect range calc | AI rewrite | During AI rewrite | Inline fog-aware scoring or `CombatResolver` utility |
| `Assets/Scripts/AI/AIController.cs` | All `[AI]` debug logs | Prototype-only feedback | AI rewrite | During AI rewrite | Remove |
| `Assets/Scripts/AI/AIController.cs` | `actedThisPhase = true` after action | Race condition; should be before | AI rewrite or hotfix | P1 — fix before AI rewrite or as a standalone fix | Move flag before `DecideMove`/`DecideAttack` calls |
| `Assets/Scripts/Turns/TurnManager.cs` | `LogState()` call | Console-only phase feedback | Production HUD | After production phase indicator HUD exists | Phase indicator in production UI |
| `Assets/Scripts/Match/DeploymentService.cs` | `ship.LogStatBlock(...)` in `DeployFleet` | Deployment verification; not gameplay | Ship stats stabilized | After ship stats are finalized | Remove |
| `Assets/Scripts/FogOfWar/FogManager.cs` | All `Debug.Log` scan summary and per-cell lines | Fog verification; no UI alternative yet | Production fog UI | After fog UI exists and behavior is confirmed | Production fog visualization |
| `Assets/Scripts/Combat/CombatResolver.cs` | `"Target not known."` and outcome logs | Only combat feedback available | Production combat feedback UI | After health bars and combat log UI exist | Production HUD combat log |
| `Assets/Scripts/Grid/TestShipController.cs` | `Debug.Log("Domain toggled"...)` and `ship.LogStatBlock` call | Domain state feedback | Production UI for domain toggle | After production domain indicator exists | Remove |
| `Assets/Scripts/Grid/TestShipController.cs` | Movement and scan preview logs | Provisional state feedback | Production movement UI | After production movement UI exists | Remove |

---

### Stage C: Final Prototype Sanitation

Items that can be removed after full planned gameplay systems are implemented.

| File | Code | Reason | Dependency | Safe Removal Point | Replacement |
|---|---|---|---|---|---|
| `Assets/Scripts/Grid/TestShipController.cs` | Entire class | Prototype Player A controller | Production input system replaces all behaviors | After production input handles move, scan, attack, phase advance | Production input system |
| `Assets/Scripts/Ships/ShipInstance.cs` | `LogStatBlock` method | Console stat verification helper | Deployment logging removed | After deployment logging is removed or production ship inspector exists | Production ship stats display |
| `Assets/Scripts/Grid/GridManager.cs` | Provisional anchor snapshot in `HandlePhaseChanged(Move)` for `anchorAtTurnStart` sync | `anchorAtTurnStart` is only needed by `MoveShip` legacy path | `MoveShip` retired | After `MoveShip` is removed | The provisional system's own `OriginalAnchor` snapshot already handles the correct origin |
| `docs/PROJECT_STATUS.md` | Entire document (or update it) | Describes pre-Milestone-5 state; multiple facts now incorrect | Historical reference | After documentation pass | Update content to reflect current state, or mark as historical/archived |
| `docs/Agent_Context_Prompt.md` | "Active scan is automatic" section | Stale — scan is now player-activated | Documentation pass | During next docs update | Update description |

---

### Stage D: Production Replacement

Items that must be replaced by proper UI, art, or production systems rather than simply deleted.

| File | Code | Reason | Dependency | Safe Removal Point | Replacement |
|---|---|---|---|---|---|
| `Assets/Scripts/Grid/GridView.cs` | `DrawDebugCones` | Visualization surface for scan preview; LOS coloring logic is correct | Production scan preview UI/overlay | After production scan preview replaces it | Production scan overlay with LOS-blocked cell indicator |
| `Assets/Scripts/Grid/GridView.cs` | `DrawDebugHalos` | Visualization surface for passive fog coverage | Production fog visibility UI | After production fog UI replaces it | Production fog overlay or ship detection indicator |
| `Assets/Scripts/Grid/GridView.cs` | Ship cube Gizmos (cyan/red `DrawCube`) | Placeholder ship representation | Production ship sprites/models | After production ship art is added | Ship sprite or model renderer |
| `Assets/Scripts/Grid/GridView.cs` | `DrawProvisionalShips` | Placeholder provisional movement preview | Production movement UI | After production movement UI is built | Movement preview with path animation |
| `Assets/Scripts/Grid/GridView.cs` | `DrawMovementRanges` | Placeholder reachable area display | Production movement UI | After production movement range highlight is built | Shaded movement range overlay |
| `Assets/Scripts/Grid/GridView.cs` | `DrawTerrain` | Placeholder terrain coloring | Production terrain art | After terrain tile art is added | Final tile art per terrain type |
| `Assets/Scripts/Grid/GridView.cs` | `DrawStartingZones` | Placeholder deployment zone indicator | Production deployment zone art or player-controlled deployment | After deployment zone is art-replaced | Final zone indicator or removed if zones are implicit |
| `Assets/Settings/InputSystem_Actions.inputactions` | Entire asset | Unused legacy Input System actions asset | Verify it is not wired in scene | After confirming no scene reference | None, or the new production input system |

---

## 12. Final Sanitation Checklist

### Code

- [ ] Remove `GridManager.DebugRecomputeFog` and `[ContextMenu]` attribute
- [ ] Remove `GridManager.TestShip` property (after AI rewrite)
- [ ] Remove `GridManager.ObstructionShip` property (after AI rewrite)
- [ ] Remove `GridManager.MoveShip` legacy method (after AI migrates to provisional)
- [ ] Remove `AIController.CanFire` method (during AI rewrite; replace with corrected logic)
- [ ] Fix `AIController.actedThisPhase` timing (set flag before `DecideMove`/`DecideAttack`)
- [ ] Remove stale comments describing old movement behavior in `TestShipController`
- [ ] Remove stale `placeholder` comment on `ShipInstance.movementRange`
- [ ] Correct `DrawDebugHalos` to use hull-cell distance instead of anchor-only distance
- [ ] Remove `TestShipController.Update` phase/turn guard redundancy if any after production input exists
- [ ] Remove or simplify `ProvisionalMovementState`'s `OriginalAnchor` duplication with `anchorAtTurnStart` after `MoveShip` is retired

### Debugging

- [ ] Remove `GridManager.DebugRecomputeFog` context menu
- [ ] Remove all `[AI]` prefixed `Debug.Log` calls in `AIController`
- [ ] Remove `TurnManager.LogState` calls (replace with production phase HUD)
- [ ] Remove `ShipInstance.LogStatBlock` call from `DeploymentService.DeployFleet`
- [ ] Remove `TestShipController` movement/scan/domain `Debug.Log` calls (with controller removal)
- [ ] Remove or suppress verbose `FogManager` per-cell scan logs (replace with production fog UI)
- [ ] Remove `CombatResolver` outcome `Debug.Log` calls (replace with production combat log)
- [ ] Keep `CombatResolver.RollWeapon` `Debug.LogWarning` (data-integrity guard — keep permanently)
- [ ] Keep `GridManager.PlaceShip` `Debug.Assert` (programming contract — keep or upgrade to exception)
- [ ] Keep `GridManager.HandlePhaseChanged` provisional-confirmation `Debug.LogWarning` (surface to player HUD)

### Art / UI

- [ ] Replace ship cube Gizmos with production ship sprites or models
- [ ] Replace `DrawDebugHalos` Gizmo with production fog/visibility overlay
- [ ] Replace `DrawDebugCones` Gizmo with production scan preview overlay
- [ ] Replace `DrawProvisionalShips` Gizmo with production movement preview
- [ ] Replace `DrawMovementRanges` Gizmo with production movement range highlight
- [ ] Replace `DrawTerrain` Gizmo with terrain tile art
- [ ] Replace `DrawStartingZones` Gizmo with production zone indicator or player deployment UI
- [ ] Build fog/scan toggle UI
- [ ] Build health bar UI
- [ ] Build phase/player/weapon indicator HUD
- [ ] Build movement and scan preview production UI

### Data

- [ ] Verify `TestMap.asset` is assigned or unassigned in the scene; move to `Assets/Data/`
- [ ] Confirm Wolf `movementRange = 6` against final design document
- [ ] Confirm Wolf "Default Absolute Vision" range = 4 against design document
- [ ] Replace hardcoded fleet roster in `GridManager.Start` with match/lobby setup
- [ ] Replace hardcoded deployment anchors in `DeploymentService` with player deployment
- [ ] Implement ammo decrement in `CombatResolver.ResolveAttack` (Milestone 6)
- [ ] Implement defense resolution and `sideEffectId` execution (Milestone 6)
- [ ] Implement recharge tick for `ChargeState.turnsUntilRecharge` (Milestone 6)
- [ ] Complete `SwordFishClass` ship card (add defenses and vision layers) when design confirms it

### Architecture

- [ ] Verify AI only reads `MatchState` and its own `FogGrid`, not `TestShip`/`ObstructionShip`
- [ ] Verify AI uses provisional movement path (not legacy `MoveShip`)
- [ ] Verify single range validation path (remove `CanFire` divergence)
- [ ] Verify `anchorAtTurnStart` is only maintained while `MoveShip` exists
- [ ] Verify no second placement validator exists in controllers (currently clean)
- [ ] Verify `DrawDebugHalos` uses hull-cell range after fix
- [ ] Verify no ScriptableObject data pipeline is introduced prematurely (YAGNI)
- [ ] Confirm `Assets/_Recovery/` scenes are not in build settings before deletion
- [ ] Confirm `InputSystem_Actions.inputactions` has no active scene or script references before cleanup

---

## 13. Cleanup Dependency Order

```
GridManager.DebugRecomputeFog [ContextMenu]
    ↓
safe to remove now
    ↓
remove immediately (P0)


AIController.actedThisPhase timing bug
    ↓
fix before or during AI rewrite
    ↓
remove risk of frame-repeated AI action (P1)


AIController.CanFire (divergent range)
    ↓
keep until
    ↓
AI is rewritten with fog-aware target selection
    ↓
remove CanFire, use corrected inline scoring (P1)


GridManager.TestShip / ObstructionShip properties
    ↓
keep until
    ↓
AIController stops referencing them
    ↓
remove both properties from GridManager (P1)


GridManager.MoveShip (legacy Chebyshev)
    ↓
keep until
    ↓
AIController.DecideMove migrates to PreviewMove + ConfirmProvisionalMovement
    ↓
retire MoveShip method (P1)


anchorAtTurnStart snapshot in TestShipController
    ↓
keep until
    ↓
MoveShip is retired (no longer needs anchorAtTurnStart for Chebyshev budget)
    ↓
remove from TestShipController and from ShipInstance if no other consumer (P1)


FogManager Debug.Log scan lines
    ↓
keep until
    ↓
production fog UI exists and fog correctness is confirmed visually
    ↓
remove verbose per-cell logs; keep summary or convert to optional (P2)


TurnManager.LogState console log
    ↓
keep until
    ↓
production phase/player HUD indicator exists
    ↓
remove LogState (P2)


CombatResolver outcome Debug.Log calls
    ↓
keep until
    ↓
production combat log / health bar UI exists
    ↓
remove console logs (P2)


DeploymentService.LogStatBlock calls
    ↓
keep until
    ↓
ship stats are finalized and deployment is verified
    ↓
remove (P2)


ShipInstance.LogStatBlock method
    ↓
keep until
    ↓
DeploymentService.LogStatBlock calls are removed
    (no other caller → method becomes dead)
    ↓
remove method (P2)


DrawDebugCones Gizmo
    ↓
keep until
    ↓
production active scan preview overlay exists
    ↓
remove or replace Gizmo; keep LOS coloring logic (P — Stage D)


DrawDebugHalos Gizmo
    ↓
keep until
    ↓
production fog visibility UI / ship detection overlay exists
    ↓
remove Gizmo (P — Stage D)


All other GridView Gizmo methods (ship cubes, zones, terrain, movement)
    ↓
keep until
    ↓
production rendering for each replaces the Gizmo
    ↓
remove each Gizmo as its replacement lands (P — Stage D)


TestShipController (entire class)
    ↓
keep until
    ↓
production input system handles: move, rotate, scan activate/rotate/confirm,
weapon selection, attack input, phase advance
    ↓
remove TestShipController (P — Stage D)
```

---

## 14. Executive Summary

### Implemented

The following systems are functionally present and verified in source:

- **Grid foundation:** 30×15 `Dictionary<Vector2Int, Tile>` board, occupancy, placement validation (`CanPlaceShip`), terrain (Normal/Costly/Impassable) loaded from `MapDefinition`.
- **Movement:** Dijkstra 8-direction pathfinding (`GridPathfinder`), provisional movement with keyboard and pointer/drag input, multi-ship atomic commit, Escape/C cancel. Space advances phase only when all previews are valid.
- **Ships and data model:** `ShipInstance` (all runtime state), `ShipData` builders for Wolf, Athena, and SwordFish. Weapon, defense, vision, and charge profiles all defined.
- **Turns:** Five-phase cycle (`Move → Staging → Search → Battle → End`), `PhaseChanged` event, one-way decoupling between `TurnManager` and subscribers.
- **Fog of War:** Passive per-player halo and cone detection with domain filtering, `onlyWhileSurfaced`, dead-ship exclusion, supercover Bresenham LOS (Phase 9A). Active cone scan is player-activated: `S` activates, `Q`/`E` rotate, `Enter` confirms. Active marks clear at End. `FogGrid` dual-layer (passive/active) state with no-downgrade upgrade logic.
- **Combat:** `CombatResolver.ResolveAttack` with dead-check, charge-readiness, domain, nearest-cell range, fog gate, d20 resolution, and destroyed-ship cleanup.
- **Match foundation:** `MatchState`, `PlayerState`, `DeploymentService` (with `CanPlaceShip` validation).

### In Progress

- **AI:** Moves and attacks for Player B's first ship only; ignores fog; uses legacy `MoveShip`. Planned fog-aware multi-ship behavior not yet built.
- **Combat resolution:** Armor, defense saves, ammo consumption, recharge, and side effects are all data-defined but not executed.
- **Phase 9B attack line-of-fire:** Design is complete (in `Plan.md`); `CombatResolver` has no LOS check yet.
- **Active scan scope:** Player can activate a scan for the currently selected ship only; the plan calls for all living ships.

### Planned / Not Implemented

- Win condition and game-over flow.
- Player-facing fog and combat UI (health bars, phase indicator, weapon selector, scan overlay, fog visibility).
- Staging actions (mines, planes, repair ship).
- Player-controlled deployment.
- Defense saving throws and side effect execution.
- Recharge processing.
- Ammo consumption.
- Production art (no sprites, textures, prefabs, or models exist in the repository).

### Cleanup Candidates

- **P0 (safe to remove now):** 2 items — `DebugRecomputeFog` [ContextMenu]; `Assets/_Recovery/` backup scenes (after build-settings verification).
- **P1 (after current AI/combat implementation):** ~10 items — `TestShip`/`ObstructionShip` properties, legacy `MoveShip`, `AIController.CanFire`, `actedThisPhase` timing fix, `[AI]` logs, `TurnManager.LogState`, `FogManager` verbose scan logs, `CombatResolver` outcome logs, deployment `LogStatBlock` calls.
- **P2 (final prototype sanitation):** ~8 items — `TestShipController` full class, `ShipInstance.LogStatBlock`, hardcoded roster/anchors, `TestMap.asset` relocation, stale documentation updates, SwordFish card completion.
- **P3 (optional quality improvements):** ~3 items — `Tile.IsValid` reserved flag, `ShipInstance.movementRange` default sentinel, `WeaponProfile` field ordering.
- **Stage D (production replacement):** The entire `GridView` Gizmo system (7 drawing methods) must be replaced by production art and UI, not simply deleted.

**Total cleanup candidates:** ~27 identified across all priorities.

### Highest-Risk Cleanup Items

The following items would break the project if removed prematurely:

1. **`GridManager.MoveShip`** — AI movement depends on it entirely. Removing it before `AIController` is rewritten would break Player B completely.
2. **`GridManager.TestShip` / `ObstructionShip`** — Referenced in three places in `AIController`. Removing them before the AI rewrite would break both AI movement and attack target selection.
3. **`ShipInstance.LogStatBlock`** — Called from `DeploymentService.DeployFleet` on every deployment. Removing it without also removing the call site would cause a compile error.
4. **`GridView.DrawDebugCones` / `DrawDebugHalos`** — These are the only active scan preview and fog coverage visualizations. Removing them before production replacements exist would make fog and scan behavior completely invisible.
5. **`TestShipController`** — This is the only Player A input path. Removing it before production input exists would make the human side unplayable.

### Final Sanitation Goal

After the full planned implementation is complete (fog-aware multi-ship AI, armor and defense resolution, ammo consumption, Phase 9B line-of-fire, win condition, production input, production art and UI), the repository should contain:

- No `TestShipController` class — replaced by a production input system.
- No `AIController` Gizmo or console-log verification code — replaced by production-quality AI.
- No `GridView` Gizmo-based rendering — replaced by sprite/model rendering, production fog overlay, and production movement/scan UI.
- No hardcoded fleet roster or anchor values — replaced by a match/lobby or deployment phase.
- No `Debug.Log` calls for game events (phase changes, attacks, fog scans) — replaced by production HUD and combat log.
- `GridManager` retains only gameplay authority code: board, occupancy, provisional movement, terrain, fog lifecycle wiring.
- `CombatResolver` retains full combat resolution including armor, defense, ammo, recharge, and Phase 9B line-of-fire.
- `VisionResolver` and `FogManager` retain fog logic; verbose scan logs are removed or suppressed.
- `ShipData` either remains as hardcoded builders or is migrated to `ScriptableObject` assets in `Assets/Data/` when the number of ships justifies it.
