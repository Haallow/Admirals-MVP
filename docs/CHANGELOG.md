# Admirals — Changelog

Entries are in reverse chronological order. Each entry records what changed,
what files were touched, and any known debt introduced.

At project end, this file feeds the final AGENTS.md and DEVELOPER_GUIDE.md
update pass.

---

## [Plane Staging Click Selection and Undeploy on Cancel]

**Summary:**
Added direct click-selection and undeploy functionality for reconnaissance planes in the Staging phase:
- In `PlaneUnit.cs`:
  - Added `ShipInstance launchedFrom`, `string profileId`, and `bool deployedThisTurn` fields to track the origin carrier and deployment lifecycle.
- In `GridManager.cs`:
  - `DeployPlane` passes `ship` and `planeProfile.id` to the `PlaneUnit` constructor and sets `deployedThisTurn = true`.
  - `PreviewPlaneMove` enforces that planes cannot move on the turn they are placed (`if (plane.deployedThisTurn)` returns `false` and logs rejection); plane movement is only allowed starting in the next Staging phase.
  - Added `UndeployPlane(PlaneUnit plane)`: verifies active Staging phase and turn ownership, ensures `plane.deployedThisTurn == true`, removes plane from `match.planes`, increments the launch ship's sortie `ChargeState.remaining` (capped at `maxCapacity`), and triggers `Fog.RecomputeAllPassive(match)`.
  - In `HandlePhaseChanged`, cleared `deployedThisTurn = false` for the acting player's planes on entry to `Phase.Search`, ensuring planes cannot be undeployed in subsequent turns and unlocking movement for future Staging phases.
- In `TestShipController.cs`:
  - In `Update` during `Phase.Staging`, clicking a cell occupied by a friendly plane now activates and selects that plane (`isControllingPlane = true`, `currentPlaneIndex`).
  - Added `KeyCode.C` handling during `planePlacementPending` to cancel plane placement mode.
  - In `HandleStagingInput`, automatically activates newly deployed planes immediately upon deployment so they can be selected or undeployed right away.
  - In `HandlePlaneMovementInput`:
    - Pressing `KeyCode.C` on a plane deployed this turn calls `gridManager.UndeployPlane(plane)` to remove it and refund the sortie; on planes from prior turns, `C` reverts position to `positionAtTurnStart`.
    - Left-clicking on friendly ships or other friendly planes does not move the active plane, allowing seamless unit selection switching.

**Modified files:**
- `Assets/Scripts/Combat/PlaneUnit.cs` — added `launchedFrom`, `profileId`, `deployedThisTurn`
- `Assets/Scripts/Grid/GridManager.cs` — added `UndeployPlane` method and phase clearing in `HandlePhaseChanged`
- `Assets/Scripts/Grid/TestShipController.cs` — click-selection for planes, immediate activation on deploy, and `C` undeploy/revert
- `docs/DEVELOPER_GUIDE.md` — updated Staging phase description
- `docs/AGENTS.md` — updated runtime flow
- `docs/CHANGELOG.md` — recorded this update

**Debt introduced:**
- None.

---

## [Plane Movement Moved to Staging Phase]

**Summary:**
Configured reconnaissance planes to only move during the Staging phase instead of the Move phase:
- In `PlaneUnit.cs`, updated the `positionAtTurnStart` comment to reflect movement range checks during the Staging phase.
- In `GridManager.cs`:
  - `PreviewPlaneMove` now enforces `Phase.Staging` (rejects attempts to move planes in any other phase) and checks player ownership.
  - In `HandlePhaseChanged`, moved `plane.positionAtTurnStart` snapshotting from `Phase.Move` to `Phase.Staging`.
- In `TestShipController.cs`:
  - Space advance commits plane moves (`ConfirmPlaneMove`) when advancing from `Phase.Staging` rather than `Phase.Move`.
  - Tab cycling switches between ships and planes only during `Phase.Staging`; in `Phase.Move` (and other phases), Tab only cycles ships.
  - In `Update`, plane movement inputs (`HandlePlaneMovementInput`) are routed during `Phase.Staging` instead of `Phase.Move`.
  - Clicking any friendly ship resets `isControllingPlane = false` to immediately return focus to ship control.
- In `docs/DEVELOPER_GUIDE.md` & `docs/AGENTS.md`, updated the phase transition diagrams and descriptions.

**Modified files:**
- `Assets/Scripts/Combat/PlaneUnit.cs` — updated comment for Staging movement
- `Assets/Scripts/Grid/GridManager.cs` — added phase gate in `PreviewPlaneMove` and moved snapshotting to `Phase.Staging`
- `Assets/Scripts/Grid/TestShipController.cs` — routed plane selection, inputs, and confirmation to Staging phase
- `docs/DEVELOPER_GUIDE.md` — updated phase transition diagram
- `docs/AGENTS.md` — updated runtime flow
- `docs/CHANGELOG.md` — recorded this update

**Debt introduced:**
- None.

---

## [One Attack Per Ship Per Battle Phase Limit]

**Summary:**
Implemented a rule restricting each ship to at most one attack per Battle phase:
- Added `hasAttackedThisPhase` bool to `ShipInstance` to track whether a ship has fired during the active Battle phase.
- Updated `CombatResolver.ResolveAttack`:
  - Rejects attacks if `attacker.hasAttackedThisPhase` is already true.
  - Rejects attacks if not currently in `Phase.Battle` or if not the attacking player's turn (when `TurnManager` is present).
  - Sets `attacker.hasAttackedThisPhase = true` when all validations pass and the weapon fires (`RollWeapon`, ammo deduction).
  - Added public `CanShipAttack(ShipInstance ship)` helper to query ship attack eligibility.
- Updated `GridManager`:
  - Exposed `TurnManager` property.
  - Added `ResetShipAttackStates()` to reset `hasAttackedThisPhase = false` across all ships.
  - Hooked `ResetShipAttackStates()` into `HandlePhaseChanged` on entry into `Phase.Battle` and `Phase.End`.
- Updated `TestShipController.HandleAttackInput`:
  - Added guard rejecting attacks if the selected ship has already attacked this Battle phase, while preserving friendly-ship click selection.

**Modified files:**
- `Assets/Scripts/Ships/ShipInstance.cs` — added `hasAttackedThisPhase` field
- `Assets/Scripts/Combat/CombatResolver.cs` — enforced one-attack limit, phase/turn gate, and added `CanShipAttack`
- `Assets/Scripts/Grid/GridManager.cs` — exposed `TurnManager`, added `ResetShipAttackStates` and wired phase hooks
- `Assets/Scripts/Grid/TestShipController.cs` — added pre-attack guard for `ship.hasAttackedThisPhase`
- `docs/DEVELOPER_GUIDE.md` — updated `CombatResolver.ResolveAttack` validation order
- `docs/AGENTS.md` — updated operating model notes
- `docs/CHANGELOG.md` — recorded this update

**Debt introduced:**
- None.

---

## [Clear Marked Orange Contact on Ship Destruction]

**Summary:**
Configured automatic fog mark cleanup when a ship is destroyed in combat or from mine detonations:
- When a ship's health reaches 0, its occupied cells are immediately removed from both passive and active fog dictionaries via `FogManager.ClearMarksForShip(target)`.
- Eliminates lingering light orange sensor contact markers (`DrawSensorContact`) on tiles after the occupant ship has been sunk.
- In `GridView.cs`, defensively added an occupant health check (`tile.Occupant.currentHealth <= 0`) to prevent any rendered artifacts on the death frame.

**Modified files:**
- `Assets/Scripts/FogOfWar/FogGrid.cs` — added `ClearCell(Vector2Int)` and `ClearCells(IEnumerable<Vector2Int>)`
- `Assets/Scripts/FogOfWar/FogManager.cs` — added `ClearMarksForShip(ShipInstance)` and `ClearMarksForCells(IEnumerable<Vector2Int>)`
- `Assets/Scripts/Combat/CombatResolver.cs` — called `gridManager.Fog?.ClearMarksForShip(target)` upon ship destruction
- `Assets/Scripts/Grid/GridManager.cs` — called `Fog?.ClearMarksForShip(ship)` upon mine destruction in `ResolveMinesFor`
- `Assets/Scripts/Grid/GridView.cs` — defensively skipped destroyed ship occupants in tile rendering loop
- `docs/CHANGELOG.md` — recorded this update

**Debt introduced:**
- None.

---

## [Fix Enemy Ship Movement Flash in Fog]

**Summary:**
Identified and resolved the root cause of enemy ships flashing in the fog during their Move phase:
- **Root Cause**: When the turn phase transitioned to `Phase.Move` for Player B (AI), `GridManager.HandlePhaseChanged` populated `provisionalMoves` with Player B's ships. In `GridView.DrawProvisionalShips()`, all provisional ships were unconditionally rendered with a red cube and white wireframe without checking owner or fog state. Once the Move phase ended or confirmed, `provisionalMoves` was cleared and the ships disappeared from view, creating a visual flash.
- **Fix in `GridView.cs`**:
  - `DrawProvisionalShips()` now skips any ship that does not belong to `PlayerId.PlayerA` unless `revealAllInFog` is active.
  - `HasProvisionalPreview()` returns `false` for enemy ships unless `revealAllInFog` is active, ensuring enemy ships on tiles are solely rendered via the fog-gated main loop.
- **Fix in `GridManager.cs`**:
  - `HandlePhaseChanged` now only creates and confirms `provisionalMoves` when `turnManager.CurrentPlayer == PlayerId.PlayerA`. Player B (AI) moves authoritatively via `MoveShip` and does not use provisional drafts.

**Modified files:**
- `Assets/Scripts/Grid/GridView.cs` — filtered enemy ships in `HasProvisionalPreview` and `DrawProvisionalShips`
- `Assets/Scripts/Grid/GridManager.cs` — gated provisional movement snapshot and confirmation to Player A
- `docs/CHANGELOG.md` — recorded this bugfix

**Debt introduced:**
- None.

---

## [Active Search Sonar Domain Updated to Both]

**Summary:**
Updated the target domain for `Active Search Sonar` across surface ships (`AthenaClass`, `CruiserClass`, `CarrierClass`) from `DomainType.SubSurface` to `DomainType.Both`:
- Active search sonar scans now detect both `Surface` and `SubSurface` enemy vessels.
- All sonar layers preserve their original `visionType` (`VisionType.Sensor`), ensuring active scans mark detected contacts (`FogState.Marked`) with the light orange indicator rather than identifying them.
- `WolfClass` submarine and `SwordFishClass` already had `DomainType.Both` configured.

**Modified files:**
- `Assets/Scripts/Ships/ShipData.cs` — updated `Active Search Sonar` detects domain to `DomainType.Both` for `AthenaClass`, `CruiserClass`, and `CarrierClass`
- `docs/CHANGELOG.md` — recorded this update

**Debt introduced:**
- None.

---

## [Fog of War Visuals: Absolute vs Sensor Vision & Inspector Toggle]

**Summary:**
Resolved the "Red Cube Fog Trap" by conditionally rendering enemy units and sensor contacts according to Player A's Fog of War knowledge (`FogState`):
- **Absolute Vision (`Identified`)**: Draws all things that are inside absolute vision. Specifically draws the enemy ship cells inside absolute vision as solid red cubes (`Color.red`).
- **Sensor Type Vision (`Marked`)**: Marks the detected tile with a light orange contact indicator (`sensorMarkedColor`) with a golden wireframe highlight to visually indicate that something is present while keeping the identity unknown. The red enemy ship cube is not drawn.
- **Hidden / Fog (`Unknown`)**: Enemy ships, mines, and planes in unrevealed cells are not drawn at all, preventing players from clicking unknown targets.
- **Inspector Toggle (`revealAllInFog`)**: Added a serialized boolean on `GridView` (with `sensorMarkedColor`) allowing developers to toggle fog culling off during development/debugging (and automatically enabled when outside Play mode).
- **Mines & Planes**: Enemy mines and planes now respect fog visibility rules, only displaying when known or when `revealAllInFog` is active. Scanned enemy mines render via the light orange sensor contact marker.

**Modified files:**
- `Assets/Scripts/Grid/GridView.cs` — added `revealAllInFog`, `sensorMarkedColor`, `DrawSensorContact`, and updated `OnDrawGizmos`, `DrawMines`, and `DrawPlanes`
- `docs/DEVELOPER_GUIDE.md` — documented visual differentiation between Identified, Marked, and Unknown states
- `docs/AGENTS.md` — documented GridView fog rendering and dev toggle
- `docs/CHANGELOG.md` — recorded this update

**Debt introduced:**
- None.

---

## [Mouse Drag & Click Rotation for Active Scan]

**Summary:**
Added mouse drag and click location support for rotating/aiming the non-passive active scan cone in the Search phase:
- While active scan preview is open, clicking or holding and dragging the mouse (left or right click) aims the cone in the nearest cardinal direction toward the cursor.
- Clicking the active ship in Search phase when no scan is active opens the active scan preview.
- Keyboard controls (`S` to activate, `Q`/`E` to rotate quarter-turns, `Enter`/`Space` to confirm, `Esc`/`C` to cancel) remain fully functional.

**Modified files:**
- `Assets/Scripts/FogOfWar/ActiveScanPreviewState.cs` — added `SetForward(Vector2Int)`
- `Assets/Scripts/Grid/GridManager.cs` — added `SetActiveScanForward(Vector2Int)`
- `Assets/Scripts/Grid/TestShipController.cs` — added mouse click/drag rotation and click-to-activate in `HandleActiveScanInput`

**Debt introduced:**
- None.

---

## [Remove Passive Vision Halo Gizmo]

**Summary:**
Removed `DrawDebugHalos` from `GridView.OnDrawGizmos` and deleted the method, stopping the rendering of passive vision halo wireframe gizmos. Non-passive cone scan preview (`DrawDebugCones`) remains intact and active.

**Modified files:**
- `Assets/Scripts/Grid/GridView.cs` — removed `DrawDebugHalos()` call and method; preserved `DrawDebugCones()`

**Debt introduced:**
- None.

---

## [Remove Ship Movement Range Gizmo]

**Summary:**
Removed `DrawMovementRanges` from `GridView.OnDrawGizmos`, which previously drew shaded cube and wireframe gizmos showing reachable movement anchors for provisional ship states.

**Modified files:**
- `Assets/Scripts/Grid/GridView.cs` — removed `DrawMovementRanges()` call and method

**Debt introduced:**
- None.

---

## [Click-to-Select Active Ship]

**Summary:**
Left-clicking any tile occupied by a Player A ship now selects that ship as the active ship
(`currentShipIndex`). Works in all phases. Logs `[Click] Switched to ship N: ShipType` to
the console. Resets `selectedWeaponIndex` to 0 and exits plane-control mode on switch.
Does not conflict with the existing pointer-drag movement handler, which only initiates a
drag when the clicked cell is a preview cell of the **currently active** ship — clicking a
different ship's tile never triggers that path.

**Modified files:**
- `Assets/Scripts/Grid/TestShipController.cs` — added click-to-select block in `Update()`

**Debt introduced:**
- None. Feature is additive; no existing behaviour changed.

---

## [Revert Multi-Ship Active Scan, Fleet Scan Limit (1 per turn), & Submarine Domain Gate]


**Summary:**
Reverted multi-ship concurrent active scan preview system back to the single-ship
scan model per project design. Single `activeScanPreview` state restored in
`GridManager`, with `ActiveScanPreview` property and zero-parameter scan methods
(`RotateActiveScan`, `ConfirmActiveScan`, `CancelActiveScan`). `GridView.DrawDebugCones`
renders the single preview cone.
Enforced **Fleet Active Scan Limit = 1 per player per Search phase**: once any ship in
the fleet confirms an active scan, no further active scans can be initiated for that player
until their next Search phase.
`TestShipController` updated with fleet scan rejection and Space auto-confirm during Search phase.
`AIActiveScanPlanner` updated to evaluate all living ships and select the single highest-scoring
ship and facing to execute the AI's 1 fleet scan per turn.
Also enforced WolfClass submarine domain toggle gate so domain (Surface/SubSurface) can only
be toggled during Move phase when the submarine is the active ship.

**Modified files:**
- `Assets/Scripts/Grid/GridManager.cs` — revert activeScanPreviews dict to single activeScanPreview, add fleetScannedThisPhase (1 scan per fleet per Search phase)
- `Assets/Scripts/Grid/GridView.cs` — revert DrawDebugCones to single ActiveScanPreview
- `Assets/Scripts/Grid/TestShipController.cs` — revert active scan calls, restore Space auto-confirm, gate WolfClass domain toggle to Move phase, restore Tab switch logs
- `Assets/Scripts/AI/Aiactivescanner.cs` — select single best ship for fleet active scan limit
- `docs/DEVELOPER_GUIDE.md` — update active scan flow, method signatures, and fleet scan limit
- `docs/Implementation_Status_And_Cleanup_Audit.md` — restore Enter confirms in active scan notes
- `docs/CHANGELOG.md` — record reversion and fleet scan limit entry

---

## [Carrier Plane — Reconnaissance Unit]

**Summary:**
Carrier can now deploy a reconnaissance plane during Staging. The plane is a
movable board unit that provides passive absolute halo vision (range 3, sees
over terrain) and expires after 3 of the owning player's End phases (fuel).
Planes are airborne — they do not occupy tiles, do not block ship movement,
and do not participate in the exclusion zone check.

Deployment: during Staging, select the Carrier and press `P`, then click a
passable cell within launch range 4 (Chebyshev from any Carrier hull cell).
Each Carrier has 1 sortie (no recharge).

Movement: during Move phase, Tab past all ships to reach planes. Arrow keys
move the selected plane (movement range 5, no terrain/occupancy restrictions).

Vision: plane's passive absolute halo (range 3) is included in the Search-phase
passive recompute. Planes bypass terrain LOS — they see over Impassable tiles.

Fuel: decremented at each owning player's End phase. Plane removed at 0.

Gizmo: diamond wireframe marker (green = PlayerA, magenta = PlayerB).

No AI plane deployment in this version.

**New files:**
- `Assets/Scripts/Combat/PlaneProfile.cs` — plane definition (id, count, launchRange, movementRange, visionRange, fuelTurns)
- `Assets/Scripts/Combat/PlaneUnit.cs` — live board plane (owner, position, fuel, movement, vision layer)

**Modified files:**
- `Assets/Scripts/Ships/ShipInstance.cs` — added `planes`, `planeCharges`; extended `InitializeCharges`, `LogStatBlock`
- `Assets/Scripts/Ships/ShipData.cs` — Carrier gets `Recon Plane` (count 1, launch 4, move 5, vision 3, fuel 3)
- `Assets/Scripts/Match/MatchState.cs` — added `planes : List<PlaneUnit>`
- `Assets/Scripts/Grid/GridManager.cs` — added `DeployPlane`, `PreviewPlaneMove`, `ConfirmPlaneMove`; `HandlePhaseChanged` extended for plane position snapshots (Move) and fuel tick + removal (End)
- `Assets/Scripts/FogOfWar/FogManager.cs` — `RecomputePassive` extended to include plane vision layers (no LOS blocking)
- `Assets/Scripts/Grid/GridView.cs` — `DrawPlanes` added (diamond wireframe, green/magenta by owner)
- `Assets/Scripts/Grid/TestShipController.cs` — Staging `P` key + click for plane deploy; Move Tab cycle extended to include planes; `HandlePlaneMovementInput` for arrow-key plane movement

**Known debt introduced:**
- Plane movement is committed immediately on arrow-key input (position updated directly), unlike ships which use the full provisional movement system. Acceptable for MVP.
- No AI plane deployment. Future AI planner needed.
- Plane weapons, shootability, and air combat are explicitly out of scope.

---

## [Mine Recharge Fix — deploy from storage regardless of recharge timer]

**Summary:**
`DeployMine` previously used `ChargeState.IsReady` to find a mine charge,
which requires `turnsUntilRecharge == 0`. This blocked deployment even when
`remaining > 0` (mines physically in storage). Fixed to check `remaining > 0`
directly — the recharge timer only governs replenishment of spent mines, not
the ability to deploy mines already in storage.

**Modified files:**
- `Assets/Scripts/Grid/GridManager.cs` — `DeployMine` charge lookup changed from `IsReady` to `remaining > 0`; rejection log updated to "no mines in storage"

---

## [Mine Recharge — 1 mine per 2 turns, cap 2]

**Summary:**
Mines now recharge. `ChargeState` gains `maxCapacity` and a `Recharge()` method
that increments `remaining` up to the cap. `MineProfile` gains `rechargeTime`.
`ShipInstance.TickRecharge` extended to loop `mineCharges`: decrements
`turnsUntilRecharge` and calls `Recharge()` when it hits 0. SwordFish Contact
Mine set to `rechargeTime: 2`, `count: 2`. Deploying a mine sets
`turnsUntilRecharge = rechargeTime` on the charge.

**Modified files:**
- `Assets/Scripts/Combat/ChargeState.cs` — added `maxCapacity`, `Recharge()`, updated constructor
- `Assets/Scripts/Combat/MineProfile.cs` — added `rechargeTime` field and constructor parameter
- `Assets/Scripts/Ships/ShipInstance.cs` — `InitializeCharges` passes `maxCapacity` to mine charges; `TickRecharge` extended to loop `mineCharges` and call `Recharge()` on tick-to-zero
- `Assets/Scripts/Ships/ShipData.cs` — SwordFish Contact Mine updated to `rechargeTime: 2`
- `Assets/Scripts/Grid/GridManager.cs` — `DeployMine` sets `mineCharge.turnsUntilRecharge` after spend; log includes recharge countdown

---

## [Mines — SwordFish Staging action]

**Summary:**
SwordFish can deploy a Contact Mine (600 flat damage, no roll, no armor, no
defenses) one cell behind its stern during the Staging phase with the `M` key.
Mines trigger on confirmed movement by any ship (friendly or enemy). Enemy
mines are hidden until revealed by active sonar scan.

**New files:**
- `Assets/Scripts/Combat/MineProfile.cs` — mine definition (id, count, damage)
- `Assets/Scripts/Combat/MineTile.cs` — live board mine (owner, position, damage)

**Modified files:**
- `Assets/Scripts/Ships/ShipInstance.cs` — added `mines`, `mineCharges`; extended `InitializeCharges`, `LogStatBlock`
- `Assets/Scripts/Ships/ShipData.cs` — SwordFish gets `Contact Mine` (count 2, damage 600)
- `Assets/Scripts/Match/MatchState.cs` — added `mines : List<MineTile>`
- `Assets/Scripts/Grid/GridManager.cs` — added `DeployMine`, `ResolveMinesFor`; wired into `ConfirmProvisionalMovement` and `MoveShip`
- `Assets/Scripts/FogOfWar/FogManager.cs` — `RunActiveSearch` now reveals enemy mine cells as `Marked`
- `Assets/Scripts/Grid/GridView.cs` — `DrawMines` added (yellow = PlayerA, orange = PlayerB)
- `Assets/Scripts/Grid/TestShipController.cs` — Staging phase branch added; `HandleStagingInput` with `M` key

**Known debt introduced:**
- Ship destruction on mine hit is duplicated from `CombatResolver`. Consolidate into a shared `DestroyShip` helper in a later branch.
- `MoveShip` (AI legacy path) has the mine trigger; once AI migrates to provisional movement, the `MoveShip` mine call can be removed.

---

## [Recharge Tick Infrastructure]

**Summary:**
`ShipInstance.TickRecharge()` decrements `turnsUntilRecharge` by 1 (floor 0)
for every weapon and defense charge. Called at `Phase.End` for the acting
player's living ships only. Currently inert — nothing sets `turnsUntilRecharge`
above 0 yet. Will activate automatically when mines, planes, or other future
systems set a recharge value on spend.

**Modified files:**
- `Assets/Scripts/Ships/ShipInstance.cs` — added `TickRecharge()`
- `Assets/Scripts/Grid/GridManager.cs` — `HandlePhaseChanged(End)` calls `TickRecharge` for acting player's ships

---

## [Armor Reduction]

**Summary:**
`CombatResolver.ApplyArmor(rawDamage, armor)` applies
`round(rawDamage * (1 - armor * 0.0015))` — 0.15% reduction per armor point.
Miss (rawDamage == 0) unaffected. Non-zero results clamped to minimum 1.
Combat log now shows raw and reduced values.

**Modified files:**
- `Assets/Scripts/Combat/CombatResolver.cs` — added `ApplyArmor`, wired into `ResolveAttack`

---

## [Ammo Deduction]

**Summary:**
`CombatResolver.ResolveAttack` now decrements `weaponCharge.remaining` after
each successful d20 roll. The `-1` infinite sentinel is never decremented.
Combat log includes remaining ammo after each shot.

**Modified files:**
- `Assets/Scripts/Combat/CombatResolver.cs` — added decrement + ammo log

---

## [Attack Rejection Logs]

**Summary:**
Every `ResolveAttack` rejection path now logs a specific reason (dead target,
not ready, domain mismatch, out of range, target unknown, line of fire
blocked). `TestShipController` outcome log simplified to `"Attack rejected."`
since the cause already appears above it.

**Modified files:**
- `Assets/Scripts/Combat/CombatResolver.cs` — rejection logs per path
- `Assets/Scripts/Grid/TestShipController.cs` — simplified outcome log

---

## [Phase 9B — Terrain Line of Fire]

**Summary:**
`CombatResolver.HasClearLineOfFire` tests every in-range attacker×target cell
pair via `VisionResolver.TryGetFirstBlockingCell`. At least one clear pair
required; all blocked → rejected before d20 roll. Logs `BLOCKED_LINE_OF_FIRE`.
`AIController.CanFire` also applies the gate. Reuses Phase 9A supercover
Bresenham traversal.

**Modified files:**
- `Assets/Scripts/Combat/CombatResolver.cs` — `HasClearLineOfFire`, wired into `ResolveAttack`
- `Assets/Scripts/AI/AIController.cs` — `CanFire` now calls `HasClearLineOfFire`

---

## [DrawDebugHalos LOS Fix]

**Summary:**
`GridView.DrawDebugHalos` now uses hull-cell range and calls
`VisionResolver.TryGetFirstBlockingCell` per source cell, matching the
authoritative `VisionResolver.IsVisibleFromAnySource`. Clear cells: cyan,
terrain-blocked: dark red.

**Modified files:**
- `Assets/Scripts/Grid/GridView.cs` — `DrawDebugHalos` rewritten

---

## [Multi-Ship Active Scan (Phase 9A fix)]

**Summary:**
Active scan preview converted from a single field to a per-ship
`Dictionary<ShipInstance, ActiveScanPreviewState>`. Added
`HashSet<ShipInstance> shipsScannedThisPhase` — one confirmed scan per ship
per Search phase. `ActivateActiveScan` and `ConfirmActiveScan` both gated to
`Phase.Search`. All four methods now take an explicit `ShipInstance` parameter.

**Modified files:**
- `Assets/Scripts/Grid/GridManager.cs` — per-ship dictionary, scan limit, phase gating
- `Assets/Scripts/Grid/TestShipController.cs` — all scan calls pass explicit ship
- `Assets/Scripts/Grid/GridView.cs` — `DrawDebugCones` iterates `ActiveScanPreviews`
- `Assets/Scripts/AI/AiActiveScanner.cs` — `ConfirmActiveScan(ship)` argument fixed

---

## [One-Tile Ship Exclusion Zone]

**Summary:**
`GridManager.HasClearExclusionZone` added. Rejects any footprint whose cells
are within Chebyshev distance 1 of any other ship's cells. Wired into
`CanPlaceShip` (committed tiles, `checkCommittedTiles: true`) and
`IsValidPreviewFootprint` / `IsValidConfirmationFootprint` (preview cells only,
`checkCommittedTiles: false` so ships aren't blocked by their own start-of-phase
positions during movement).

**Modified files:**
- `Assets/Scripts/Grid/GridManager.cs` — `HasClearExclusionZone`, wired into three validation methods

---

## [CanAdvancePhase Guard]

**Summary:**
`GridManager.CanAdvancePhase(Phase)` added — pure read, mirrors the validation
loop of `ConfirmProvisionalMovement` without committing. `TestShipController`
Space handler now calls it before every `AdvancePhase()`. `HandlePhaseChanged(Staging)`
downgrades `LogWarning` to `Debug.Assert` since the pre-check makes failure
unreachable.

**Modified files:**
- `Assets/Scripts/Grid/GridManager.cs` — `CanAdvancePhase` added
- `Assets/Scripts/Grid/TestShipController.cs` — Space handler updated

---

## [New AI System — AIController rewrite + planners]

**Summary:**
Full AI rewrite. `AIController` now uses `PhaseChanged` event (no Update loop).
New planner classes: `AITurnContext`, `AIMovementPlanner`, `AIAttackPlanner`,
`AIScoring`, `AIActiveScanPlanner`, `AIEnemyMemory`. AI is now fog-aware,
controls all living PlayerB ships, runs active scans during Search, and has
dead-reckoning enemy prediction. `AIAttackPlanner.CanFire` mirrors
`CombatResolver` range + LOS checks exactly.

**New files:**
- `Assets/Scripts/AI/AiController.cs` — rewritten orchestrator
- `Assets/Scripts/AI/AiTurnContext.cs`
- `Assets/Scripts/AI/AiMovementPlanner1.cs`
- `Assets/Scripts/AI/AIAttackPlanner.cs`
- `Assets/Scripts/AI/AIScoring.cs`
- `Assets/Scripts/AI/AiActiveScanner.cs`
- `Assets/Scripts/AI/AiEnemyMemory.cs`

**Known debt:**
- AI still uses `MoveShip` legacy path, not provisional movement.
- `GridManager.TestShip` / `ObstructionShip` / `MoveShip` remain until AI migrates.

---

## [ShipData syntax fixes]

**Summary:**
Fixed rogue commas after method closing braces in `BuildSwordFishClass` and
`BuildCruiserClass`. Renamed `CruiserClass` → `BuildCruiserClass` and
`CarrierClass` → `BuildCarrierClass`. Fixed `ShipFactory` case labels
(`ShipType.CarrierClass(ship);` → `ShipType.CarrierClass:`).

**Modified files:**
- `Assets/Scripts/Ships/ShipData.cs`
- `Assets/Scripts/Ships/ShipFactory.cs`

---

## [CruiserClass and CarrierClass ship cards]

**Summary:**
Added `BuildCruiserClass` (4-cell, 1200 HP, no weapons yet) and
`BuildCarrierClass` (5-cell, 1600 HP, Deck Gun + Anti-Ship Missile ×5) to
`ShipData`. Added `CruiserClass` and `CarrierClass` to `ShipType` enum and
`ShipFactory` dispatch.

**Modified files:**
- `Assets/Scripts/Ships/ShipData.cs`
- `Assets/Scripts/Ships/ShipFactory.cs`
- `Assets/Scripts/Ships/ShipType.cs`
