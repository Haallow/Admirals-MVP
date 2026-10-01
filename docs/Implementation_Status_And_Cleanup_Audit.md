# Admirals — Implementation Status and Code Cleanup Audit

**Date:** 2026-10-01  
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
| **Movement** | IMPLEMENTED | Provisional move + Dijkstra (keyboard and pointer/drag); `Space` commits, `Escape`/`C` cancels; reachable-anchor Gizmo removed | Terrain cost not wired into `MoveShip` legacy path used by AI | `MoveShip` legacy Chebyshev method | Remove or retire `MoveShip` after AI migrates to provisional system |
| **Dijkstra** | IMPLEMENTED | `GridPathfinder.FindPath`, `FindReachableCells`, 8-direction, terrain cost, occupancy | — | — | Keep pathfinder; `GridView.DrawMovementRanges` removed |
| **Provisional Movement** | IMPLEMENTED | `ProvisionalMovementState`, `GridManager.PreviewMove`, `ConfirmProvisionalMovement`, `CancelProvisionalMovement` | Multi-ship AI use | Provisional footprint Gizmos | Replace Gizmos with production movement UI |
| **Keyboard Movement** | IMPLEMENTED | Arrow keys preview/commit; `Space` commits and advances; `Q`/`E` rotate | — | All in `TestShipController` (prototype controller) | Replace with production input |
| **Pointer/Drag Input** | IMPLEMENTED | Click-to-press, drag to grid cell, release holds provisional | — | All in `TestShipController` | Replace with production input |
| **Ship Rotation** | IMPLEMENTED | `FootprintUtil.RotateOffsets` (0/90/180/270), validated via `CanPlaceShip` | — | `Q`/`E` in `TestShipController` | Replace with production input |
| **Fog of War** | IMPLEMENTED | `FogManager`, `FogGrid`, `FogState`, `VisionResolver`, `ActiveScanPreviewState`; Absolute (`Identified`) vs Sensor (`Marked`) visual differentiation, dev `revealAllInFog` toggle, dead-ship mark cleanup (`ClearMarksForShip`) | Player-facing fog UI | All visualization is Gizmo-based | Replace Gizmos with production fog UI |
| **Passive Detection** | IMPLEMENTED | Halo and Cone passive layers, Chebyshev hull-cell range, `FogState.Identified`/`Marked`, rebuilt each Search, supercover Bresenham LOS blocks Impassable terrain (Phase 9A); plane unit absolute halo vision (range 3, sees over terrain LOS); halo Gizmo (`DrawDebugHalos`) removed | — | — | Keep detection; production fog visualizer |
| **Active Scanning** | IMPLEMENTED | Player activates with `S`, `Q`/`E` rotate, `Enter` confirms; `ActiveScanPreviewState` + cone Gizmo; fleet limit of 1 active scan per phase enforced; AI also scans via `AiActiveScanner` | Active scan does not iterate all living ships in parallel; 1 scan per fleet per Search phase | Cone Gizmo (`DrawDebugCones`); active scan via keyboard | Replace with production scan UI |
| **Cone Scanning** | IMPLEMENTED | `VisionResolver.GetConeCells`, supercover LOS per cell, bow-derived forward | — | Cone Gizmo is visualization surface | Replace Gizmo with production scan overlay |
| **Cone Pivot** | IMPLEMENTED | `ActiveScanPreviewState.Rotate`, `GridManager.RotateActiveScan` | — | `Q`/`E` keys in `TestShipController.HandleActiveScanInput` | Keep logic; replace key bindings |
| **Vision LOS (Phase 9A)** | IMPLEMENTED | `VisionResolver.TryGetFirstBlockingCell`, supercover Bresenham; `VisionScanResult` captures blocked cells with blocker coords | — | Blocked-cell Gizmo coloring in `DrawDebugCones` | Keep LOS; replace blocked-cell visualization |
| **Combat — Attack Validation** | IMPLEMENTED | Dead-check, charge-readiness, domain, nearest-cell range, fog gate, line of fire, turn/phase gate, and single attack limit per Battle phase (`hasAttackedThisPhase`); every rejection logs reason | — | `Debug.Log` on every rejection and outcome | Replace logs with production feedback |
| **Combat — Attack Resolution** | IMPLEMENTED | d20 `RollWeapon`, tier damage, armor reduction (`ApplyArmor`), ammo deduction (`remaining--`), destroyed-ship occupancy cleanup, and dead-ship contact mark clearing (`ClearMarksForShip`) | Defense saving throws and side effects execution | `Debug.Log` on outcome and destruction | Implement defenses/side-effects later; remove logs |
| **Combat — Line of Fire** | IMPLEMENTED | `CombatResolver.HasClearLineOfFire` tests every in-range attacker×target cell pair via `VisionResolver.TryGetFirstBlockingCell`; fully-blocked attacks rejected before d20 roll; logs `BLOCKED_LINE_OF_FIRE` | — | `Debug.Log` on block rejection | Replace log with production feedback |
| **AI** | IMPLEMENTED (WITH PLANNERS) | Event-driven modular architecture (`AiController`, `AiTurnContext`, `AiEnemyMemory`, `AiActiveScanner`, `AIMovementPlanner1`, `AIAttackPlanner`, `AIScoring`); controls all living Player B ships, fog-aware, 1 fleet active scan per phase, respects `hasAttackedThisPhase` | AI movement provisional migration | Legacy `MoveShip` execution path | Migrate AI movement to provisional Dijkstra |
| **Deployment** | IMPLEMENTED | `DeploymentService.DeployAll`, `CanPlaceShip` validation present, hardcoded anchors | Player-controlled deployment phase | Hardcoded anchor values | Eventually support player deployment |
| **Turn Management** | IMPLEMENTED | `TurnManager.AdvancePhase`, five phases, `PhaseChanged` event, player switching at End; Staging handles mine recharge and plane deploy-turn unlock; End decrements plane fuel and clears active marks | Win condition / game-over check | `LogState` per phase advance | Replace console log with production HUD indicator |
| **Visualization** | PROTOTYPE / TEMPORARY | Gizmo rendering in `GridView.OnDrawGizmos` (starting zones, terrain, sensor contacts, identified ships, provisional previews, cones, mines, planes); `DrawDebugHalos` and `DrawMovementRanges` removed | All production rendering: sprites, UI, HUD | Entire `GridView` is Gizmo-based | Replace entirely with production art + UI |
| **UI** | PLANNED / NOT IMPLEMENTED | None | Fog/scan toggles, health bars, phase/ship/weapon indicators, movement preview UI | — | Full UI system to be built |
| **Art / Assets** | PLANNED / NOT IMPLEMENTED | No sprites, textures, prefabs, or models in repository | All production art | — | All art to be added |
| **Match / Game-Over** | PARTIALLY IMPLEMENTED | Destroyed ships removed from grid and live fleet; no win check | Win-condition/game-over flow | Dead-ship log in `CombatResolver` | Add after core gameplay systems complete |
| **Staging Actions** | IMPLEMENTED | Contact Mines (`SwordFishClass`, `M` deploy, 600 damage, 2-turn recharge); Reconnaissance Planes (`CarrierClass`, `P` deploy, launch range 4, selectable in Staging, Tab cycle, deploy-turn movement lock, undeploy `C` refunds sortie, fuel tick on End, halo vision range 3) | Repair ship action | Keyboard keys `M`, `P`, `C` in `TestShipController` | Replace keyboard bindings with production UI buttons |

> **Current Implementation Highlights:**
> - **Line of Sight & Fire (Phases 9A/9B):** `VisionResolver.TryGetFirstBlockingCell` gates both fog scans and weapon attacks.
> - **Combat Complete:** Single attack limit per Battle phase (`hasAttackedThisPhase`), armor calculation (`ApplyArmor`), ammo deduction (`remaining--`), and dead ship mark removal (`ClearMarksForShip`) are all live.
> - **Fog of War Visual Differentiation:** Solid red enemy ship cubes render only when `Identified` (Absolute vision); sensor detections render as light orange `Marked` contact markers without revealing the ship cube; `revealAllInFog` toggle available for inspection.
> - **Modular AI:** AI is event-driven (`PhaseChanged`) with dedicated planners for memory, active scanning, movement, and attack.
> - **Staging Actions:** Contact mines and carrier reconnaissance planes are fully functional in Staging.

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
Provisional movement: `GridManager.PreviewMove` calculates a Dijkstra path, validates the footprint, and stores a `ProvisionalMovementState`. `ConfirmProvisionalMovement` validates all previews together and commits atomically. `CancelProvisionalMovement` discards all previews. Keyboard (arrows/Q/E) and pointer (click-drag) both feed `PreviewMove`. On entering `Move`, `HandlePhaseChanged` snapshots all current-player ships. `GridView.DrawMovementRanges` has been removed.

**Legacy movement:**  
`GridManager.MoveShip` remains — it validates by Chebyshev distance from `anchorAtTurnStart` then calls `CanPlaceShip`. It is used by the AI's `AIMovementPlanner1` execution step. It does not use Dijkstra or terrain cost.

**Missing functionality:**  
AI does not use the provisional movement system yet. Richer movement UI (production input, animations).

**Temporary/prototype functionality:**  
All player movement input is in `TestShipController`, documented as throwaway. `DrawProvisionalShips` in `GridView` is a TEMPORARY-labeled Gizmo visualization (enemy provisional previews are suppressed to prevent fog flashing).

**Future cleanup:**  
- Migrate AI movement to provisional Dijkstra system, then retire `MoveShip`.
- Replace all Gizmo movement visualization with production UI.
- Remove `TestShipController` input class when production input replaces it.

---

### 2.4 Fog of War

**Current implementation:**  
`FogManager` owns two `FogGrid` instances (one per player). Passive detection is rebuilt from scratch each `Search` phase via `RecomputeAllPassive`. Active scans are player-triggered: the human player presses `S` to activate, `Q`/`E` to rotate, `Enter` to confirm; a fleet limit of 1 active scan per phase is enforced. `VisionResolver` is stateless and handles Halo geometry, Cone geometry, domain filtering, `onlyWhileSurfaced`, dead-ship filtering, and supercover Bresenham LOS. `VisionScanResult` carries detected and blocked cells. Active marks clear at `End`. `FogGrid.GetState` returns the stronger of passive and active layers. Stale contact marks for sunken ships are automatically cleared via `FogManager.ClearMarksForShip`. `DrawDebugHalos` has been removed. Carrier planes provide absolute halo vision (range 3) that sees over terrain LOS.

**Visual Differentiation:**  
`GridView` visually separates knowledge levels:
- `Identified` (Absolute vision): Draws solid red enemy ship cubes.
- `Marked` (Sensor vision): Draws a light orange cube with gold wireframe (`sensorMarkedColor`); the enemy ship cube itself is hidden.
- `Unknown`: Enemy ships, planes, and mines are completely hidden in fog.
- An Inspector toggle (`revealAllInFog`) allows developers to reveal all units during testing.

**Missing functionality:**  
- Player-facing fog UI (production radar blips / sonar ping VFX instead of Gizmos).

**Partial functionality:**  
Attack gating treats both `Marked` and `Identified` as known.

**Temporary/prototype functionality:**  
All fog visualization (cones, contacts) is Gizmo-based, scene-view only. `FogManager` emits verbose `Debug.Log` lines on every scan summary and every detected/blocked cell.

**Future cleanup:**  
Replace Gizmo visualization with production fog overlay/UI. Remove or suppress verbose scan logs once fog behavior is verified.

---

### 2.5 Combat

**Current implementation:**  
`CombatResolver.ResolveAttack` performs the full authoritative attack chain:
1. Dead-check (rejects dead targets).
2. Phase and turn gating: Battle phase only, attacking player's turn.
3. Single attack limit: Attacker must not have already attacked this Battle phase (`hasAttackedThisPhase == false`).
4. Charge readiness: Requires `remaining != 0` and `turnsUntilRecharge == 0`.
5. Domain match: Target must inhabit weapon's target domain.
6. Range check: Nearest-cell Chebyshev distance across all attacker/target cell pairs.
7. Fog gate: Target must be known in attacker's fog (`IsTargetKnown`).
8. Line of fire: `HasClearLineOfFire` tests every in-range cell pair via `VisionResolver.TryGetFirstBlockingCell`.
9. d20 roll: `RollWeapon` resolves damage tier.
10. Armor reduction: `ApplyArmor(rawDamage, target.armor)` reduces damage by 0.15% per armor point (`effectiveDamage = round(rawDamage * (1 - armor * 0.0015))`).
11. Single-attack flag: `attacker.hasAttackedThisPhase = true`.
12. Ammo deduction: `weaponCharge.remaining--` decrements finite ammunition.
13. Sunk cleanup: If target health reaches 0, removes target from occupancy and live fleet, and clears its sensor contact marks via `FogManager.ClearMarksForShip`.

**Missing functionality:**  
- Defense profiles are defined in data but `ResolveAttack` does not yet invoke saving throws.
- `DefenseProfile.sideEffectId` strings are stored but no code executes them.

**Future cleanup:**  
Implement defense saving throws and side-effect execution. Replace `Debug.Log` combat messages with production combat log HUD.

---

### 2.6 AI

**Current implementation:**  
The AI has been overhauled into an event-driven modular architecture:
- `AiController` subscribes to `TurnManager.PhaseChanged` for Player B turns.
- `AiTurnContext` captures a per-turn snapshot of living AI ships, friendly memory, and fleet scan usage.
- `AiEnemyMemory` tracks observed enemy positions, turn recency, and predicted locations across turns.
- `AiActiveScanner` scores candidate sensor ships and cone orientations, firing up to 1 fleet active scan per Search phase.
- `AIMovementPlanner1` evaluates movement candidates toward predicted/observed enemy targets or map center if no enemy has been sighted. (Legacy debt: Movement currently executes via `GridManager.MoveShip` Chebyshev steps rather than provisional Dijkstra paths).
- `AIAttackPlanner` evaluates legal attacks against known targets in Player B's fog, respects the one-attack-per-phase constraint (`hasAttackedThisPhase`), and selects weapons by expected damage.
- `AIScoring` provides scoring formulas for scans, moves, and attacks.

**Legacy debt:**  
- AI movement execution still calls `GridManager.MoveShip` directly instead of `PreviewMove` and `ConfirmProvisionalMovement`.
- AI movement does not consume terrain movement costs.

**Future cleanup:**  
Migrate `AIMovementPlanner1` execution to use `GridManager.PreviewMove` and atomic confirmation.

---

### 2.7 Deployment

**Current implementation:**  
`DeploymentService.DeployAll` deploys Wolf + Athena + SwordFish / Carrier rosters. `DeployFleet` calls `CanPlaceShip` before `PlaceShip`. Player A anchors at x=1, rotation 0. Player B anchors at x=`gridManager.width - 3`, rotation 180. Each ship is assigned `anchorAtTurnStart` at deploy time. `LogStatBlock` is called for each deployed ship.

**Missing functionality:**  
Player-controlled deployment phase. No deployment-phase UI. Roster is hardcoded in `GridManager.Start`.

**Temporary/prototype functionality:**  
Hardcoded roster and anchor values. `LogStatBlock` calls in `DeploymentService.DeployFleet` are verification helpers.

**Future cleanup:**  
Replace hardcoded roster/anchor with player-driven deployment. Remove `LogStatBlock` deployment logs once deployment is verified.

---

### 2.8 Turn Management

**Current implementation:**  
`TurnManager.AdvancePhase` cycles `Move → Staging → Search → Battle → End`, switches player at `End → Move`. Fires `PhaseChanged` event; `GridManager.HandlePhaseChanged` and `AiController.HandlePhaseChanged` subscribe.
- **Staging:** Handles contact mine recharge (`turnsUntilRecharge` ticks down) and plane deploy-turn unlock (`canMoveInStaging = true`).
- **Battle:** Ships execute attacks (one attack per ship per Battle phase).
- **End:** Decrements plane fuel (`currentFuel--`, destroys plane at 0 fuel) and clears active marks via `FogManager.ClearAllActiveMarks`.

**Missing functionality:**  
Win condition/game-over detection. End-phase cooldowns. Search input gating (Space currently advances out of Search even without confirming a scan).

**Temporary/prototype functionality:**  
`TurnManager.LogState` (called in `Start` and `AdvancePhase`) is a debug console message.

**Future cleanup:**  
Replace `LogState` with production HUD phase indicator. Add win-check in `HandlePhaseChanged` for the End phase.

---

### 2.9 Ships and Data Model

**Current implementation:**  
`ShipInstance` is a plain `[Serializable]` C# class holding all runtime state (including `hasAttackedThisPhase`). `ShipData` builds hardcoded Wolf, Athena, SwordFish, and Carrier instances. `ShipFactory` dispatches to the correct builder. `ShipType` is the card enum.

**Missing functionality:**  
No ScriptableObject data pipeline (intentionally deferred per YAGNI). Defense saving throws not implemented.

**Partial functionality:**  
`DefenseProfile.sideEffectId` is stored as a string (`"BecomeSubSurfaceAndSkipNextMove"`) but no code reads or acts on it.

**Future cleanup:**  
Implement defense execution. Consider ScriptableObject migration when the number of ships justifies it (YAGNI — do not migrate prematurely).

---

### 2.10 Staging Actions (Mines & Reconnaissance Planes)

**Current implementation:**  
Staging phase supports tactical deployments:
- **Contact Mines:** `SwordFishClass` deploys mines in Staging via `M` key. Deployed to adjacent empty pass-through tiles. Detonates on enemy entry dealing 600 damage and removes itself. Recharge ticks in Staging (1 charge per 2 turns, max 2). Rendered in cyan for friendly player, hidden in fog for enemy.
- **Reconnaissance Planes:** `CarrierClass` deploys planes in Staging via `P` key (launch range 4). Click-selectable in Staging, Tab cycling. Movement locked on deploy turn, unlocked in subsequent Staging phases. `C` key undeploys freshly deployed plane and refunds carrier sortie charge (`remaining++`). Decrements fuel at End phase, destroyed at 0 fuel. Provides absolute halo vision (range 3) that sees over terrain LOS.

**Temporary/prototype functionality:**  
Keyboard bindings (`M`, `P`, `C`) in `TestShipController`. Gizmo-based plane and mine rendering in `GridView`.

**Future cleanup:**  
Replace keyboard shortcuts with production UI action buttons. Replace Gizmos with plane sprites/models and mine VFX.

---

## 3. Redundant and Potentially Removable Code

| File | Class / Method / Field | Why It Appears Redundant | Still Referenced? | Safe to Remove Now? | Remove When | Replacement |
|---|---|---|---|---|---|---|
| `Assets/Scripts/Grid/GridManager.cs` | `TestShip` property | Convenience property; `match.playerA.ships[0]` is equivalent | Retained for backwards compatibility | No | After all test callers migrate | `gridManager.Match.playerA.ships[0]` or fog-aware search |
| `Assets/Scripts/Grid/GridManager.cs` | `ObstructionShip` property | Convenience property; `match.playerB.ships[0]` is equivalent | Retained for backwards compatibility | No | After all test callers migrate | `gridManager.Match.playerB.ships[0]` or fog-aware search |
| `Assets/Scripts/Grid/GridManager.cs` | `MoveShip(ship, newAnchor, newRotation)` | Chebyshev-only legacy movement no longer used by human input; provisional system replaced it | Yes — `AIMovementPlanner1.cs` | No | After AI migrates to provisional movement | `PreviewMove` + `ConfirmProvisionalMovement` |
| `Assets/Scripts/Grid/GridManager.cs` | `DebugRecomputeFog` [ContextMenu] | Developer guide explicitly lists it as safe to remove; not part of phase flow | Yes — invokable via Inspector context menu | Yes — P0 | Immediately | None needed |
| `Assets/Scripts/Grid/GridView.cs` | `DrawDebugHalos` | Removed from codebase | No — REMOVED | Yes — ALREADY REMOVED | Completed | Authoritative fog detection |
| `Assets/Scripts/Grid/GridView.cs` | `DrawMovementRanges` | Removed from codebase | No — REMOVED | Yes — ALREADY REMOVED | Completed | Production movement UI |
| `Assets/Scripts/Ships/ShipData.cs` | `BuildSwordFishClass` | Formerly undeployed; now actively deployed with contact mine staging capability | Yes — deployed and used | No (Keep) | N/A — In active use | Ship card definition |
| `docs/PROJECT_STATUS.md` | Entire file | Describes pre-Milestone-5 state; key facts are now wrong | Yes — referenced in some docs | No — stale data | Update alongside next major status doc update | Update `PROJECT_STATUS.md` content or retire it as superseded by `DEVELOPER_GUIDE.md` |
| `docs/Agent_Context_Prompt.md` | Section "Active scan is automatic" | Describes active scan as automatic per-Search; this was changed — scan is player-activated | Yes — used as context for agents | No | When doc maintenance pass happens | Update description |

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

### 4.11 AI Planners Structured Logs

**File:** `Assets/Scripts/AI/`  
**Classification:** KEEP UNTIL PRODUCTION HUD/LOG EXISTS  
**Reason:** `[AI Movement]`, `[AI ActiveScan]`, and `[AI Attack]` logs provide diagnostic visibility into AI decision-making across living ships. Keep until production combat log and telemetry exist.

---

### 4.12 `GridView.DrawDebugCones` — cone Gizmo

**File:** `Assets/Scripts/Grid/GridView.cs`  
**Classification:** REPLACE WITH PRODUCTION ART  
**Reason:** Per the roadmap comment in `GridView.cs`: "DrawDebugCones is no longer 'debug' in the throwaway sense — per the roadmap it's becoming the real toggleable fog/scan visualization." The cone Gizmo currently draws yellow/red wireframe cubes for the active scan preview cone, using `VisionResolver.TryGetFirstBlockingCell` to color blocked cells. This is the intended visualization surface for the production scan preview. Do not remove; replace with production scan overlay.

---

### 4.13 `GridView.DrawDebugHalos` — halo Gizmo (REMOVED)

**File:** `Assets/Scripts/Grid/GridView.cs`  
**Status:** REMOVED  
**Reason:** Passive detection is authoritative in `FogManager` and `VisionResolver`. The standalone halo Gizmo has been removed from `GridView` to reduce visual clutter on the board.

---

## 5. Placeholder Art and Visualization

No production art, sprites, prefabs, or materials exist in this repository. All gameplay visualization is Gizmo-based in `GridView.OnDrawGizmos`. The following table maps each Gizmo to its intended final replacement.

| Location | Current Placeholder | Purpose | Final Replacement | Removal Point |
|---|---|---|---|---|
| `GridView.OnDrawGizmos` ship cubes | Cyan/red `DrawCube` per occupied tile | Identifies ship positions and ownership; red enemy cubes only drawn when `Identified` in absolute vision (or `revealAllInFog`) | Player-specific ship sprites or models on the board | Stage D — when ship sprites are added |
| `GridView.DrawSensorContact` | Light orange cube with gold wireframe (`sensorMarkedColor`) | Shows sensor contact when a tile is `Marked` in fog without revealing ship identity | Production radar blip or sonar ping VFX | Stage D — when production sensor VFX is built |
| `GridView.DrawProvisionalShips` | Semi-transparent cyan/red cubes at preview positions | Shows provisional movement destination (enemy previews suppressed in normal play) | Production movement preview (highlighted path, ghost ship) | Stage D — when production movement UI is built |
| `GridView.DrawTerrain` | Orange cubes (Costly), dark grey cubes (Impassable), grey wireframe (Normal) | Displays terrain type per tile | Final tile art per terrain type | Stage D — when terrain tile art is added |
| `GridView.DrawStartingZones` | Blue/red translucent zone overlay | Marks player starting areas | Final zone indicator art (border or shading) or removed if deployment is player-controlled | Stage D |
| `GridView.DrawDebugCones` | Yellow/red wireframe cells for active scan cone | Shows active scan preview including LOS-blocked cells | Production scan preview overlay with blocked-cell indicator | Stage D — when production scan UI is built; keep LOS coloring logic |
| `GridView.DrawMines` | Cyan/red wireframe markers for contact mines | Shows deployed contact mines (enemy mines hidden in fog unless revealed) | Production mine VFX/models | Stage D |
| `GridView.DrawPlanes` | Yellow wireframe markers with launch range indicator | Shows active carrier reconnaissance aircraft | Production aircraft sprites/models | Stage D |
| `GridView.DrawDebugHalos` | Formerly cyan/dark-red wireframe cells | Formerly showed passive detection coverage | REMOVED | Completed |
| `GridView.DrawMovementRanges` | Formerly translucent cells over reachable anchors | Formerly showed Dijkstra reachable area | REMOVED | Completed |

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
**Values:** Deploys Wolf, Athena, SwordFish, and Carrier.  
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

### 6.7 `ChargeState` ammo deduction — IMPLEMENTED

**File:** `Assets/Scripts/Combat/CombatResolver.cs`  
**Current state:** `ResolveAttack` now decrements `weaponCharge.remaining--` upon resolving an attack with finite ammunition. Weapons with 0 remaining charges are not ready for subsequent attacks until replenished.  
**Action:** Completed.

---

## 7. Duplicate or Diverging Logic

### 7.1 AI Range Calculation — RESOLVED

**State:** The AI was rewritten with modular planners (`AIAttackPlanner.cs`). It now queries the authoritative `CombatResolver.ResolveAttack` logic and checks target availability directly against known enemy cells from fog. Divergence in prototype `CanFire` has been resolved.

---

### 7.2 `TestShipController.HandleAttackInput` input-level checks vs `CombatResolver.ResolveAttack`

**Authoritative implementation:**  
`CombatResolver.ResolveAttack` — dead check, phase/turn check, single-attack limit (`hasAttackedThisPhase`), charge, domain, range, fog, line of fire, d20, damage, ammo deduction, and mark cleanup.

**Input-level checks in `TestShipController`:**  
`HandleAttackInput` checks: (a) tile exists, (b) occupant exists, (c) occupant is not friendly, (d) `selectedWeaponIndex` is in bounds.

**Why duplication exists:**  
These are input-level guards to prevent calling `ResolveAttack` with obviously invalid parameters. They are not combat rules — they are UI filtering.

**Should it be removed/replaced:**  
No. These are intentional input-level checks, not duplicated business logic. The developer guide explicitly calls this pattern out: "Do not recommend removing harmless input-level checks merely because an authoritative validation exists elsewhere." Keep as-is.

---

### 7.3 `GridManager.HandlePhaseChanged` provisional snapshot vs `TestShipController` anchor snapshot

**GridManager side:**  
`HandlePhaseChanged(Phase.Move)` creates a `ProvisionalMovementState` for each current-player ship, which stores `OriginalAnchor` and `OriginalRotation`.

**TestShipController side:**  
`HandleMoveInput` snapshots `anchorAtTurnStart` for all Player A ships when the phase first becomes `Move`.

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

### 8.4 `ChargeState.turnsUntilRecharge` — IN ACTIVE USE

**File:** `Assets/Scripts/Combat/ChargeState.cs`  
**Status:** Now actively consumed by the contact mine recharge cycle in the Staging phase (`GridManager.HandlePhaseChanged` ticks down `turnsUntilRecharge` every 2 turns, restoring mine charges up to capacity).  
**Assessment:** No longer dormant — active gameplay mechanic.  
**Action:** Keep.

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

### 8.8 `SwordFishClass` — DEPLOYED AND IN ACTIVE USE

**Files:** `Assets/Scripts/Ships/ShipType.cs`, `ShipFactory.cs`, `ShipData.cs`  
**Status:** Deployed in fleet rosters. Provides contact mine deployment during the Staging phase with 2-turn recharge cycles.  
**Assessment:** Active gameplay ship card.  
**Action:** Keep.

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

### 10.1 AI movement bypasses provisional movement (Legacy Debt)

**Issue:** `AIMovementPlanner1` calculates a target coordinate and calls `gridManager.MoveShip` (legacy Chebyshev) directly. This means the AI's movement does not go through the provisional system, does not consume terrain cost, and does not participate in multi-ship atomic confirmation.  
**Intended architecture:** All movement should use `PreviewMove` → `ConfirmProvisionalMovement`.  
**Smallest fix:** Rewrite the AI movement execution step to call `gridManager.PreviewMove` and confirm atomically.

---

### 10.2 AI reads global ship references — RESOLVED

**Was:** `AIController` read `gridManager.TestShip` and `gridManager.ObstructionShip`.  
**Fixed:** The overhauled AI planners (`AiTurnContext`, `AIMovementPlanner1`, `AIAttackPlanner`) query `gridManager.Match.playerB.ships` and `gridManager.Match.playerA.ships` directly, respecting Fog of War knowledge.

---

### 10.3 `DrawDebugHalos` and `DrawMovementRanges` — RESOLVED

**Status:** Both methods have been removed from `GridView.cs`. Authoritative passive detection is computed by `FogManager` and `VisionResolver`, with visual rendering handled via `DrawSensorContact` and selective enemy ship rendering.

---

### 10.4 `WeaponProfile` constructor parameter ordering inconsistency

**Issue:** `WeaponProfile` constructor signature is `(id, ammo, targetDomain, rollTiers, weaponRange)` — `weaponRange` is last. All `ShipData` builders use named parameters (e.g. `weaponRange: 4`), so order does not matter in practice. But the field declaration order in `WeaponProfile.cs` has `weaponRange` after `rollTiers`.  
**Assessment:** No functional bug since named parameters are used. P3 cosmetic — reorder fields for readability if desired.

---

### 10.5 `FogManager.RecomputeAllPassive` does not call `RunActiveSearch` anymore

**Issue:** Earlier in development, `HandlePhaseChanged(Search)` called both `RecomputeAllPassive` and `RunActiveSearch(CurrentPlayer)` automatically. The current source calls only `RecomputeAllPassive` on `Search`. `RunActiveSearch` is now called only from `GridManager.ConfirmActiveScan` (player confirmation) and `AiActiveScanner.ExecuteScan` (AI scan). This is an intentional design change enforcing the 1-scan-per-fleet limit.

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

Items that should remain until subsequent feature milestones are reached.

| File | Code | Reason | Dependency | Safe Removal Point | Replacement |
|---|---|---|---|---|---|
| `Assets/Scripts/Grid/GridManager.cs` | `TestShip` property | Convenience property; `match.playerA.ships[0]` is equivalent | Test code | After test callers migrate | Direct MatchState query |
| `Assets/Scripts/Grid/GridManager.cs` | `ObstructionShip` property | Convenience property; `match.playerB.ships[0]` is equivalent | Test code | After test callers migrate | Direct MatchState query |
| `Assets/Scripts/Grid/GridManager.cs` | `MoveShip(ship, anchor, rotation)` legacy method | Only used by AI movement execution step | AI provisional migration | After AI uses `PreviewMove` | `PreviewMove` + `ConfirmProvisionalMovement` |
| `Assets/Scripts/Turns/TurnManager.cs` | `LogState()` call | Console-only phase feedback | Production HUD | After production phase indicator HUD exists | Phase indicator in production UI |
| `Assets/Scripts/Match/DeploymentService.cs` | `ship.LogStatBlock(...)` in `DeployFleet` | Deployment verification; not gameplay | Ship stats stabilized | After ship stats are finalized | Remove |
| `Assets/Scripts/FogOfWar/FogManager.cs` | All `Debug.Log` scan summary and per-cell lines | Fog verification; no UI alternative yet | Production fog UI | After fog UI exists and behavior is confirmed | Production fog visualization |
| `Assets/Scripts/Combat/CombatResolver.cs` | Rejection and outcome logs | Diagnostic feedback | Production combat feedback UI | After health bars and combat log UI exist | Production HUD combat log |
| `Assets/Scripts/Grid/TestShipController.cs` | Movement, scan, attack, and staging logs | Prototype feedback | Production movement/combat UI | After production UI exists | Remove |

---

### Stage C: Final Prototype Sanitation

Items that can be removed after full planned gameplay systems are implemented.

| File | Code | Reason | Dependency | Safe Removal Point | Replacement |
|---|---|---|---|---|---|
| `Assets/Scripts/Grid/TestShipController.cs` | Entire class | Prototype Player A controller | Production input system replaces all behaviors | After production input handles move, scan, attack, staging, phase advance | Production input system |
| `Assets/Scripts/Ships/ShipInstance.cs` | `LogStatBlock` method | Console stat verification helper | Deployment logging removed | After deployment logging is removed | Production ship stats display |
| `Assets/Scripts/Grid/GridManager.cs` | Provisional anchor snapshot in `HandlePhaseChanged(Move)` for `anchorAtTurnStart` sync | `anchorAtTurnStart` is only needed by `MoveShip` legacy path | `MoveShip` retired | After `MoveShip` is removed | `ProvisionalMovementState.OriginalAnchor` |
| `docs/PROJECT_STATUS.md` | Entire document (or update it) | Describes pre-Milestone-5 state; multiple facts now incorrect | Historical reference | After documentation pass | Update content to reflect current state |

---

### Stage D: Production Replacement

Items that must be replaced by proper UI, art, or production systems rather than simply deleted.

| File | Code | Reason | Dependency | Safe Removal Point | Replacement |
|---|---|---|---|---|---|
| `Assets/Scripts/Grid/GridView.cs` | `DrawDebugCones` | Visualization surface for scan preview; LOS coloring logic is correct | Production scan preview UI/overlay | After production scan preview replaces it | Production scan overlay with LOS-blocked cell indicator |
| `Assets/Scripts/Grid/GridView.cs` | `DrawSensorContact` | Placeholder contact indicator | Production sensor radar blip / sonar ping VFX | After sensor VFX is added | Production contact VFX |
| `Assets/Scripts/Grid/GridView.cs` | Ship cube Gizmos (cyan/red `DrawCube`) | Placeholder ship representation | Production ship sprites/models | After production ship art is added | Ship sprite or model renderer |
| `Assets/Scripts/Grid/GridView.cs` | `DrawProvisionalShips` | Placeholder provisional movement preview | Production movement UI | After production movement UI is built | Movement preview with path animation |
| `Assets/Scripts/Grid/GridView.cs` | `DrawTerrain` | Placeholder terrain coloring | Production terrain art | After terrain tile art is added | Final tile art per terrain type |
| `Assets/Scripts/Grid/GridView.cs` | `DrawStartingZones` | Placeholder deployment zone indicator | Production deployment zone art | After deployment zone is art-replaced | Final zone indicator art |
| `Assets/Scripts/Grid/GridView.cs` | `DrawMines` | Placeholder contact mine wireframe | Production mine models/VFX | After mine art is added | Final mine art |
| `Assets/Scripts/Grid/GridView.cs` | `DrawPlanes` | Placeholder reconnaissance plane wireframe | Production aircraft sprites/models | After plane art is added | Final aircraft art |
| `Assets/Settings/InputSystem_Actions.inputactions` | Entire asset | Unused legacy Input System actions asset | Verify it is not wired in scene | After confirming no scene reference | None, or production input system |

---

## 12. Final Sanitation Checklist

### Code

- [ ] Remove `GridManager.DebugRecomputeFog` and `[ContextMenu]` attribute
- [ ] Remove `GridManager.TestShip` property (after AI/test migration)
- [ ] Remove `GridManager.ObstructionShip` property (after AI/test migration)
- [ ] Remove `GridManager.MoveShip` legacy method (after AI migrates to provisional)
- [x] Replace `AIController.CanFire` divergent logic — **DONE** (handled by `AIAttackPlanner`)
- [x] Fix `AIController.actedThisPhase` timing — **DONE** (replaced by event-driven `PhaseChanged` subscription)
- [ ] Remove stale comments describing old movement behavior in `TestShipController`
- [ ] Remove stale `placeholder` comment on `ShipInstance.movementRange`
- [x] Correct `DrawDebugHalos` to use hull-cell range and LOS — **DONE** (subsequently removed from `GridView`)
- [x] Remove `DrawMovementRanges` from `GridView` — **DONE**
- [ ] Remove or simplify `ProvisionalMovementState`'s `OriginalAnchor` duplication with `anchorAtTurnStart` after `MoveShip` is retired

### Debugging

- [ ] Remove `GridManager.DebugRecomputeFog` context menu
- [ ] Remove `TurnManager.LogState` calls (replace with production phase HUD)
- [ ] Remove `ShipInstance.LogStatBlock` call from `DeploymentService.DeployFleet`
- [ ] Remove `TestShipController` input/staging/combat logs (with controller removal)
- [ ] Remove or suppress verbose `FogManager` per-cell scan logs (replace with production fog UI)
- [ ] Remove `CombatResolver` outcome `Debug.Log` calls (replace with production combat log)
- [ ] Keep `CombatResolver.RollWeapon` `Debug.LogWarning` (data-integrity guard — keep permanently)
- [ ] Keep `GridManager.PlaceShip` `Debug.Assert` (programming contract — keep or upgrade to exception)
- [ ] Keep `GridManager.HandlePhaseChanged` provisional-confirmation `Debug.LogWarning` (surface to player HUD)

### Art / UI

- [ ] Replace ship cube Gizmos with production ship sprites or models
- [ ] Replace `DrawSensorContact` Gizmo with production radar blip / sonar ping VFX
- [ ] Replace `DrawDebugCones` Gizmo with production scan preview overlay
- [ ] Replace `DrawProvisionalShips` Gizmo with production movement preview
- [ ] Replace `DrawTerrain` Gizmo with terrain tile art
- [ ] Replace `DrawStartingZones` Gizmo with production zone indicator or player deployment UI
- [ ] Replace `DrawMines` and `DrawPlanes` Gizmos with production sprites/models
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
- [x] Implement ammo decrement in `CombatResolver.ResolveAttack` — **DONE**
- [x] Implement armor damage reduction in `CombatResolver.ResolveAttack` — **DONE**
- [x] Implement one-attack-per-ship Battle phase constraint (`hasAttackedThisPhase`) — **DONE**
- [x] Implement dead-ship contact mark cleanup (`FogManager.ClearMarksForShip`) — **DONE**
- [x] Implement recharge tick for `ChargeState.turnsUntilRecharge` — **DONE** (consumed by contact mines)
- [x] Complete `SwordFishClass` ship card with contact mine deployment — **DONE**
- [ ] Implement defense saving throws and `sideEffectId` execution (Milestone 6+)

### Architecture

- [x] Verify AI reads `MatchState` and its own `FogGrid` — **DONE** (via `AiTurnContext`)
- [ ] Verify AI uses provisional movement path (currently still calls `MoveShip`)
- [x] Verify single range validation path — **DONE**
- [ ] Verify `anchorAtTurnStart` is only maintained while `MoveShip` exists
- [ ] Verify no second placement validator exists in controllers (clean)
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


AI movement execution (AIMovementPlanner1 -> MoveShip)
    ↓
keep until
    ↓
AI migrates to PreviewMove + ConfirmProvisionalMovement
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


All other GridView Gizmo methods (ship cubes, contacts, zones, terrain, provisional, mines, planes)
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
weapon selection, attack input, staging deployment, phase advance
    ↓
remove TestShipController (P — Stage D)
```

---

## 14. Executive Summary

### Implemented

The following systems are functionally present and verified in source:

- **Grid foundation:** 30×15 `Dictionary<Vector2Int, Tile>` board, occupancy, placement validation (`CanPlaceShip`), terrain (Normal/Costly/Impassable) loaded from `MapDefinition`.
- **Movement:** Dijkstra 8-direction pathfinding (`GridPathfinder`), provisional movement with keyboard and pointer/drag input, multi-ship atomic commit, Escape/C cancel. Space advances phase only when all previews are valid. Reachable-anchor Gizmo removed.
- **Ships and data model:** `ShipInstance` (all runtime state including `hasAttackedThisPhase`), `ShipData` builders for Wolf, Athena, SwordFish, and Carrier. Weapon, defense, vision, and charge profiles all defined.
- **Turns:** Five-phase cycle (`Move → Staging → Search → Battle → End`), `PhaseChanged` event, decoupled subscribers. Staging handles mine recharge and plane deploy-turn unlock; End handles plane fuel decrement and clears active marks.
- **Fog of War:** Passive per-player halo and cone detection with domain filtering, `onlyWhileSurfaced`, dead-ship exclusion, supercover Bresenham LOS (Phase 9A). Active cone scan is player-activated: `S` activates, `Q`/`E` rotate, `Enter` confirms, enforced fleet limit of 1 scan per phase. Active marks clear at End. `FogGrid` dual-layer state. Sunk ship contact marks cleared via `ClearMarksForShip`. Visual differentiation between `Identified` (red cube) and `Marked` (orange contact); `revealAllInFog` toggle for dev testing; standalone halo Gizmo removed. Reconnaissance aircraft provide absolute halo vision (range 3, sees over terrain LOS).
- **Combat:** `CombatResolver.ResolveAttack` with dead-check, turn/phase gate, single attack per ship limit (`hasAttackedThisPhase`), charge-readiness, domain, nearest-cell range, fog gate, terrain line-of-fire (Phase 9B via `HasClearLineOfFire`), d20 resolution, armor damage reduction (`ApplyArmor`), ammo deduction (`remaining--`), destroyed-ship occupancy cleanup, and contact mark removal. Every rejection logs its specific cause.
- **AI:** Overhauled into an event-driven modular architecture (`AiController`, `AiTurnContext`, `AiEnemyMemory`, `AiActiveScanner`, `AIMovementPlanner1`, `AIAttackPlanner`, `AIScoring`). Controls all living Player B ships, tracks enemy positions and predicts targets across turns, executes 1 active cone scan per phase for the fleet, respects `hasAttackedThisPhase`, and attacks known targets by expected damage. (Legacy debt: movement execution still calls `GridManager.MoveShip`).
- **Staging Actions:** Contact Mines (`SwordFishClass`, `M` key, 600 damage, 2-turn recharge). Reconnaissance Planes (`CarrierClass`, `P` key, launch range 4, selectable in Staging, Tab cycle, deploy-turn movement lock, undeploy `C` refunds sortie, fuel tick on End, halo vision range 3).
- **Match foundation:** `MatchState`, `PlayerState`, `DeploymentService` (with `CanPlaceShip` validation).

### In Progress / Legacy Debt

- **AI movement execution:** `AIMovementPlanner1` currently executes moves via `GridManager.MoveShip` Chebyshev steps rather than provisional Dijkstra paths.
- **Combat resolution:** Defense saving throws and side effects are defined in data profiles but not yet evaluated during attack resolution.

### Planned / Not Implemented

- Win condition and game-over flow.
- Player-facing UI (health bars, phase indicator, weapon selector, scan overlay, fog visibility, staging buttons).
- Player-controlled deployment phase.
- Production art (sprites, textures, prefabs, or models).

### Cleanup Candidates

- **P0 (safe to remove now):** 2 items — `DebugRecomputeFog` [ContextMenu]; `Assets/_Recovery/` backup scenes (after build-settings verification).
- **P1 (after current AI/combat implementation):** ~6 items — `TestShip`/`ObstructionShip` properties, legacy `MoveShip`, `TurnManager.LogState`, `FogManager` verbose scan logs, `CombatResolver` rejection logs, deployment `LogStatBlock` calls.
- **P2 (final prototype sanitation):** ~6 items — `TestShipController` full class, `ShipInstance.LogStatBlock`, hardcoded roster/anchors, `TestMap.asset` relocation, stale documentation updates.
- **P3 (optional quality improvements):** ~3 items — `Tile.IsValid` reserved flag, `ShipInstance.movementRange` default sentinel, `WeaponProfile` field ordering.
- **Stage D (production replacement):** The `GridView` Gizmo system (starting zones, terrain, sensor contacts, identified ships, provisional previews, cones, mines, planes) must be replaced by production art and UI.

### Highest-Risk Cleanup Items

1. **`GridManager.MoveShip`** — AI movement execution currently depends on it. Removing it before `AIMovementPlanner1` migrates to `PreviewMove` would break Player B movement.
2. **`ShipInstance.LogStatBlock`** — Called from `DeploymentService.DeployFleet` on every deployment. Removing it without also removing the call site would cause a compile error.
3. **`TestShipController`** — This is the only Player A input path. Removing it before production input exists would make the human side unplayable.
4. **`GridView.DrawDebugCones`** — Active scan preview visualization. Removing it before production scan overlay exists would make scanning preview invisible.

### Final Sanitation Goal

After the full planned implementation is complete, the repository should contain:

- No `TestShipController` class — replaced by a production input system.
- No `GridView` Gizmo-based rendering — replaced by sprite/model rendering, production fog overlay, and production movement/scan UI.
- No hardcoded fleet roster or anchor values — replaced by a match/lobby or deployment phase.
- No `Debug.Log` calls for game events — replaced by production HUD and combat log.
- `GridManager` retains only gameplay authority code: board, occupancy, provisional movement, terrain, fog lifecycle wiring.
- `CombatResolver` retains full combat resolution including armor, defense saving throws, side effects, ammo, recharge, and line-of-fire.
- `VisionResolver` and `FogManager` retain fog logic; verbose scan logs are removed or suppressed.
- `ShipData` either remains as hardcoded builders or is migrated to `ScriptableObject` assets in `Assets/Data/` when the number of ships justifies it.
- AI utilizes provisional movement and full tactical planning across all phases.
