# Admirals — Changelog

Entries are in reverse chronological order. Each entry records what changed,
what files were touched, and any known debt introduced.

At project end, this file feeds the final AGENTS.md and DEVELOPER_GUIDE.md
update pass.

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
