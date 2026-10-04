# AI System Developer Guide

**Admirals Fleet Commander AI - Stage 2 Complete**  
Last Updated: 2024  
Author: Kiro AI Assistant

---

## Table of Contents

1. [System Overview](#system-overview)
2. [Architecture](#architecture)
3. [File Structure](#file-structure)
4. [Core Components](#core-components)
5. [How It Works: Execution Flow](#how-it-works-execution-flow)
6. [Fleet Commander & Stances](#fleet-commander--stances)
7. [Movement Planning](#movement-planning)
8. [Attack Planning](#attack-planning)
9. [Search Coordination](#search-coordination)
10. [Adding New Ship Classes](#adding-new-ship-classes)
11. [Tuning Parameters](#tuning-parameters)
12. [Debugging & Logging](#debugging--logging)
13. [Known Limitations](#known-limitations)
14. [Future Stages](#future-stages)

---

## System Overview

The AI system controls PlayerB's fleet through a hierarchical decision-making architecture:

- **Fleet-level strategy** (FleetCommander): Picks a stance based on material score, HP, and contacts
- **Per-ship tactical movement** (AIMovementPlanner): Scores tiles using attack value, danger, and formation terms
- **Per-ship targeting** (AIAttackPlanner): Selects weapons and targets
- **Search coordination** (AIActiveScanPlanner): Picks which ship scans and which direction

**Design Principles:**
- **Fog-honest**: The commander works only on contacts (clusters of known fog cells), never real enemy ShipInstances
- **Modular**: Each planner is a static class with a single public entry point
- **No static state**: FleetCommander is an instance owned by AIController, reset each match
- **Additive scoring**: Formation and stance terms are nudges on top of core attack/danger scoring, not hard overrides

---

## Architecture

```
AIController (MonoBehaviour, orchestrator)
  +- FleetCommander (strategy layer, instance)
  �   +- Evaluates stance once per Move phase
  �   +- Builds FleetSnapshot (contacts, roles, fragility, centroid)
  �   +- Logs stance changes and roster
  �
  +- AIMovementPlanner (per-ship tile scoring, static)
  �   +- Scores attack value (kill bonus, damage, target weakness)
  �   +- Scores danger (expected incoming damage from known enemies)
  �   +- Scores formation terms (cohesion, fragility pushback, scout forward)
  �   +- Returns Decision (destination, likelyTarget, isFleeing)
  �
  +- AIAttackPlanner (per-ship weapon selection, static)
  �   +- Filters weapons by range and domain
  �   +- Picks highest expected-value weapon
  �   +- Respects claimed-target deconfliction
  �
  +- AIActiveScanPlanner (fleet-wide scan selection, static)
  �   +- Filters ships to surface-capable active cones
  �   +- Ranks Scout role first, then by fog coverage score
  �   +- Runs exactly one scan per Search phase
  �
  +- AIEnemyMemory (last-seen tracking, static)
  �   +- Remembers last observed position + heading per enemy
  �   +- Ages hidden ships (turns-since-seen counter)
  �   +- Provides dead-reckoning predictions for active scan aiming
  �
  +- AITurnContext (snapshot builder, static)
      +- MyShips (living ships owned by AI)
      +- EnemyShips (all living enemy ships, fog-honest via KnownEnemies)
      +- MyFog (FogGrid for AI's side)
      +- KnownEnemies (subset of EnemyShips currently visible in fog)
```

---

## File Structure

### AI Folder (`Assets/Scripts/AI/`)

| File | Purpose |
|---|---|
| `AiController.cs` | Orchestrator: wires phase events to planners, owns FleetCommander instance |
| `AIMovementPlanner1.cs` | Tile scoring: attack + danger + formation terms ? destination |
| `AIAttackPlanner.cs` | Weapon selection: range/domain filtering, expected-value ranking |
| `AIActiveScanPlanner.cs` | Scanner selection: Scout-first, surface-capable cones, fog coverage |
| `AIScoring.cs` | Shared math: weapon expected value (damage � probability) |
| `AIEnemyMemory.cs` | Last-seen tracking: position, heading, turns-since-seen, dead-reckoning |
| `AITurnContext.cs` | Snapshot builder: rosters + fog ? MyShips/EnemyShips/KnownEnemies |
| `AiContact.cs` | Contact builder: clusters adjacent known fog cells, deduces candidate classes |

### Fleet Folder (`Assets/Scripts/Fleet/`)

| File | Purpose |
|---|---|
| `FleetCommander.cs` | Strategy layer: stance chooser, snapshot builder, kill tracking |
| `FleetSnapshot.cs` | Data holder: Members (role, fragility), Contacts, Centroid, MaterialScore |
| `FleetStance.cs` | Enum: Hunt, Advance, Hold, DefensiveFallback, Retreat, Flank |
| `FleetRoles.cs` | Role assignment: Scout/Anchor/Striker per ship class, footprint catalog |

---

## Core Components

### AIController (Orchestrator)

**Lifecycle:**
1. `OnEnable`: Subscribe to `TurnManager.PhaseChanged`
2. `HandlePhaseChanged`: Lazy-create FleetCommander on first phase event
3. **Move phase**:
   - Build AITurnContext
   - Update AIEnemyMemory (advanceTurn: true)
   - Call `FleetCommander.Evaluate()` ? FleetSnapshot
   - For each ship sequentially: `ExecuteMove(ship, context, snapshot, claimedTargets)`
4. **Battle phase**:
   - Build AITurnContext (sees Search phase results)
   - Update AIEnemyMemory (advanceTurn: false, no aging)
   - Call `FleetCommander.RefreshContacts()` ? updated snapshot
   - For each living ship: `ExecuteAttack(ship, context, claimedTargets)`
5. **Search phase**:
   - Call `AIActiveScanPlanner.RunActiveScans(context, gridManager)`

**Sequential vs Parallel:**
- Movement is sequential: each ship's new position is visible to the next ship's pathfinding
- Attack is sequential: a ship destroyed earlier in the phase is skipped later
- Future: parallel movement is a design decision still open (see PINNED in context)

---

### FleetCommander (Strategy Layer)

**Responsibilities:**
- Pick a stance once per Move phase (hysteresis + dwell-time prevent flip-flopping)
- Build FleetSnapshot: Members (role, fragility, HP), Contacts (from fog), Centroid, MaterialScore
- Track enemy-alive estimate (starts at fleet size, decrements on confirmed kills)
- Log stance changes, roster, and contacts

**Stance Selection Logic (ChooseStance):**

```csharp
MaterialScore = (OwnAlive / EnemyAliveEstimate) � (0.5 + 0.5 � FleetHpFraction)
```

| Condition | Stance |
|---|---|
| MaterialScore < 0.35 OR FleetHp < 0.30 | **Retreat** (safety override, no dwell-time) |
| MaterialScore < 0.60 | **DefensiveFallback** |
| Contacts.Count == 0 | **Hunt** (exiting Hunt ignores dwell-time) |
| MaterialScore = 0.90 | **Advance** |
| Default | **Hold** |

**Hysteresis:** Current stance's threshold widens by 0.08 (e.g., Retreat ? Fallback needs score > 0.43, not just > 0.35)  
**Dwell-Time:** Minimum 2 turns in a stance before switching (except Retreat entry and Hunt exit)

**Fog Honesty:**
- FleetCommander never reads `context.EnemyShips` or `context.KnownEnemies`
- Works only on `context.MyShips`, `context.MyFog`, and its own `enemyAliveEstimate` counter
- AIContactBuilder scans the board and clusters adjacent known cells
- Identified cells (passive Absolute vision) reveal class; Marked cells (sensor hits) give position only
- Classes are deduced from footprint length when not identified (not guaranteed for partial scans)

---

### AIMovementPlanner (Tactical Movement)

**Signature:**
```csharp
public static Decision ChooseDestination(
    ShipInstance self,
    AITurnContext context,
    FleetSnapshot snapshot,
    GridManager gridManager,
    HashSet<ShipInstance> claimedTargets)
```

**Returns:**
```csharp
struct Decision {
    Vector2Int destination;
    ShipInstance likelyTarget;  // null if repositioning
    bool isFleeing;
}
```

**Scoring Pipeline (per tile):**

1. **For each (tile, enemy) pair where a weapon exists:**
   - Base attack score = KillBonus (50) or DamageFraction � MaxDamageScore (40)
   - TargetWeakness bonus (10 � how hurt the target already is)
   - Danger penalty (35 � expected incoming damage fraction)
   - SelfRisk penalty (15 � self's missing HP fraction)
   - AlreadyClaimed penalty (20 if a sibling ship lined up on this target)
   - **Formation terms** (Stage 2):
     - Cohesion pull: toward centroid, scaled by stance (0.0 in Hunt, 1.0 in Retreat)
     - Fragility pushback: high-fragility ships (>0.6) penalized for closing with contacts
     - Scout forward: scouts rewarded for advancing toward contacts

2. **Best (tile, enemy, weapon) triple wins**

3. **Flee override:** If `currentHP / maxHP = 0.3` and best attack is not lethal ? `FindSafestTile` (lowest danger, farthest from enemies)

4. **No-target fallback:**
   - If `KnownEnemies.Count > 0`: `ClosestTileToAnyEnemy` (with formation terms)
   - Else if `CurrentStance == Hunt`: `ClosestTileToPoint(towardEnemyZone)` (opposite edge from home)
   - Else: `ClosestTileToCenter` (defensive regroup)

**All scoring terms are scaled as fractions of ship maxHealth** so weights stay consistent across 400 HP (SwordFish) and 2000 HP (Athena) ships.

---

### Formation Terms (Stage 2)

**Cohesion Pull:**
```csharp
deltaTowardCentroid = currentDist - tileDist;  // positive = moving closer
cohesionScore = deltaTowardCentroid � CohesionBaseWeight (8) � multiplier
```

| Stance | Multiplier |
|---|---|
| Retreat / DefensiveFallback | 1.0 |
| Hold | 0.7 |
| Hunt | 0.0 (no clustering) |
| Advance | 0.2 |

**Fragility Pushback:**
```csharp
if (EffectiveFragility > 0.6 && NearestContact exists) {
    deltaTowardContact = currentDist - tileDist;  // positive = moving closer to threat
    if (deltaTowardContact > 0)
        score -= deltaTowardContact � FragilityPushbackWeight (12);
}
```

**Scout Forward Bonus:**
```csharp
if (Role == Scout && NearestContact exists) {
    deltaTowardContact = currentDist - tileDist;
    if (deltaTowardContact > 0)
        score += deltaTowardContact � ScoutForwardWeight (6);
}
```

**EffectiveFragility:**
```csharp
EffectiveFragility = 1 - (1 - StaticFragility) � HpFraction
```
A wounded ship becomes more fragile regardless of class.

---

### AIAttackPlanner (Weapon Selection)

**Signature:**
```csharp
public static bool TryChooseTarget(
    ShipInstance self,
    List<ShipInstance> knownEnemies,
    GridManager gridManager,
    HashSet<ShipInstance> claimedTargets,
    out ShipInstance target,
    out WeaponProfile weapon)
```

**Logic:**
1. For each known enemy:
   - Filter weapons: `CanEngageTarget(weapon, self.currentDomain, target.currentDomain)`
   - Check range: `IsInRange(self.anchor, target, weapon.range, gridManager)`
   - Rank by `AIScoring.ExpectedValue(weapon)` (damage � roll probability)
2. Pick highest expected-value (weapon, target) pair
3. Apply claimed-target penalty: if `claimedTargets.Contains(target)`, soft-discourage (not a hard block)
4. Return false if no valid weapon/target exists

**Claimed-Target Deconfliction:**
- AIController maintains a `HashSet<ShipInstance> claimedTargets` per phase
- When a ship successfully attacks, the target is added to the set
- Later ships in the same phase see the penalty but can still pile on if it's the only option

---

### AIActiveScanPlanner (Search Coordination)

**Stage 1.5 Complete: Deliberate Scanner Selection**

**Logic:**
1. **Filter candidates:** Ships with a non-passive Cone layer where `detects != DomainType.SubSurface`
   - Athena's sub-surface-only sonar is excluded (would waste the fleet's single scan)
2. **Rank candidates:**
   - Priority 1: Scout role (Wolf)
   - Priority 2: Highest fog-coverage score (unknown cells in best facing direction)
3. **Pick one, activate, confirm, done**

**Facing Selection (ScoreFacing):**
```csharp
score = unknownCellsCovered + alignment � BearingWeight (100)
alignment = dot(candidateFacing, towardPrediction)  // from AIEnemyMemory
```

**GridManager Gate:**
- `fleetScannedThisPhase` flag blocks duplicate scans
- Only the chosen ship's scan resolves; no other ship attempts

---

## How It Works: Execution Flow

### Turn Start (PlayerB Move Phase)

```
1. TurnManager.AdvancePhase(Phase.Move)
2. AIController.HandlePhaseChanged(Phase.Move)
   +- Lazy-create FleetCommander (first phase only)
   +- Build AITurnContext (MyShips, EnemyShips, MyFog, KnownEnemies)
   +- AIEnemyMemory.Remember(advanceTurn: true) ? age hidden ships
   +- FleetCommander.Evaluate(context) ? FleetSnapshot
   �   +- Build Members (role, fragility, HP fraction)
   �   +- AIContactBuilder.Build(gridManager, MyFog) ? Contacts
   �   +- ChooseStance(snapshot) ? apply hysteresis + dwell-time
   �   +- Log stance, roster, contacts
   �   +- Return snapshot
   +- For each ship in MyShips (sequential):
       +- AIMovementPlanner.ChooseDestination(ship, context, snapshot, gridManager, claimedTargets)
       �   +- GetPlaceableReachableTiles ? candidates
       �   +- For each (tile, enemy): score attack + danger + formation
       �   +- Check flee condition (low HP + no lethal shot)
       �   +- Fallback: ClosestToEnemy / ClosestToPoint / ClosestToCenter
       �   +- Return Decision
       +- GridManager.MoveShip(ship, decision.destination, rotationDegrees)
       +- If success + likelyTarget: add target to claimedTargets
```

### Search Phase

```
1. TurnManager.AdvancePhase(Phase.Search)
2. AIController.HandlePhaseChanged(Phase.Search)
   +- AIActiveScanPlanner.RunActiveScans(context, gridManager)
       +- SelectScanner(context, gridManager)
       �   +- Filter: surface-capable active Cone layers
       �   +- Rank: Scout role first, then by fog-coverage score
       �   +- Return best ship (or null)
       +- TryActiveScan(chosenShip, context, gridManager)
           +- ScoreFacing ? best cardinal direction
           +- GridManager.ActivateActiveScan(ship, bestForward)
           +- GridManager.ConfirmActiveScan() ? fleetScannedThisPhase = true
           +- Return
```

### Battle Phase

```
1. TurnManager.AdvancePhase(Phase.Battle)
2. AIController.HandlePhaseChanged(Phase.Battle)
   +- Build AITurnContext (sees Search phase results)
   +- AIEnemyMemory.Remember(advanceTurn: false) ? update sightings, no aging
   +- FleetCommander.RefreshContacts(context) ? updated snapshot (no stance change)
   +- For each ship in MyShips (sequential):
       +- Skip if currentHealth <= 0
       +- AIAttackPlanner.TryChooseTarget(ship, KnownEnemies, gridManager, claimedTargets)
       �   +- For each known enemy:
       �   �   +- Filter weapons by domain + range
       �   �   +- Rank by ExpectedValue
       �   +- Return (target, weapon) or false
       +- If no target in range: log + continue
       +- GridManager.Combat.ResolveAttack(ship, target, weapon)
       +- If resolved: add target to claimedTargets
```

---

## Adding New Ship Classes

### Step 1: Define Ship Card in ShipData.cs

```csharp
public static void BuildNewShipClass(ShipInstance ship)
{
    ship.shipType = ShipType.NewShipClass;
    ship.maxHealth = 1200;
    ship.currentHealth = 1200;
    ship.movementRange = 4;
    ship.currentDomain = DomainType.Surface;
    
    // Footprint (multi-cell)
    ship.footprintOffsets = new List<Vector2Int> {
        new Vector2Int(0, 0),
        new Vector2Int(1, 0),
        new Vector2Int(2, 0)  // 3-cell horizontal
    };
    
    // Weapons
    ship.weapons = new List<WeaponProfile> { /* ... */ };
    
    // Vision layers
    ship.visionLayers = new List<VisionLayer> {
        new VisionLayer(
            id: "active_sonar",
            shape: ShapeType.Cone,
            range: 5,
            detects: DomainType.Both,  // ? Surface or Both for scanning candidacy
            visionType: VisionType.Sensor,
            isPassive: false
        )
    };
}
```

### Step 2: Add Role to FleetRoles.cs

```csharp
private static readonly Dictionary<ShipType, FleetRoleEntry> Table =
    new Dictionary<ShipType, FleetRoleEntry>
    {
        { ShipType.WolfClass,      new FleetRoleEntry(FleetRole.Scout,   0.70f) },
        { ShipType.AthenaClass,    new FleetRoleEntry(FleetRole.Anchor,  0.20f) },
        { ShipType.SwordFishClass, new FleetRoleEntry(FleetRole.Striker, 0.80f) },
        { ShipType.NewShipClass,   new FleetRoleEntry(FleetRole.Anchor,  0.40f) },  // ? ADD THIS
    };
```

**Role Guidelines:**
- **Scout**: Fast, expendable, goes deep (Wolf, future recon units)
- **Anchor**: High HP, low fragility, holds the line (Athena, Cruiser)
- **Striker**: High damage, fragile, stays back (SwordFish, Carrier)

**StaticFragility Guidelines:**
- 0.0-0.3: Very sturdy (Athena 0.20)
- 0.4-0.6: Moderate (NewShip 0.40)
- 0.7-1.0: Fragile (Wolf 0.70, SwordFish 0.80)

### Step 3: Test

- Add the ship to a fleet roster in `GridManager.Start()`:
  ```csharp
  var playerB = new PlayerState(PlayerId.PlayerB, new List<ShipType> {
      ShipType.WolfClass,
      ShipType.AthenaClass,
      ShipType.NewShipClass  // ? ADD HERE
  });
  ```
- Run Play mode
- Check `[AI][Fleet] roster:` log � verify role and fragility are correct
- Watch formation behavior: Scouts advance, Anchors hold center, Strikers drop back when damaged

---

## Tuning Parameters

### Movement Planner Weights (`AImovementplanner1.cs`)

| Constant | Current | Effect | Tuning Guidance |
|---|---|---|---|
| `KillBonus` | 50 | Reward for lethal shots | Highest weight � guaranteed kills dominate |
| `MaxDamageScore` | 40 | Reward for high-damage non-lethal hits | Second priority |
| `TargetWeaknessWeight` | 10 | Bonus for shooting wounded enemies | Small nudge toward finishing hurt targets |
| `DangerWeight` | 35 | Penalty for exposed tiles | Balance with KillBonus � danger shouldn't prevent a kill |
| `SelfRiskWeight` | 15 | Extra caution when self is hurt | Makes wounded ships more defensive |
| `AlreadyClaimedPenalty` | 20 | Discourage piling on a claimed target | Less than KillBonus so kills still allowed |
| `CohesionBaseWeight` | 8 | Pull toward fleet centroid | Scaled by stance multiplier (0.0-1.0) |
| `FragilityPushbackWeight` | 12 | Fragile ships avoid contacts | Stronger than Cohesion so fragile ships actually drop back |
| `ScoutForwardWeight` | 6 | Scouts advance toward contacts | Weaker than Pushback � scouts go forward but not recklessly |
| `FragilityThreshold` | 0.6 | EffectiveFragility cutoff for pushback | 0.6 = Wolf always, Athena when wounded |

**To make the fleet more aggressive:**
- Increase `MaxDamageScore` (40 ? 50)
- Decrease `DangerWeight` (35 ? 25)

**To make the fleet tighter:**
- Increase `CohesionBaseWeight` (8 ? 12)
- Increase Advance/Hunt cohesion multipliers (0.2 ? 0.4)

**To make fragile ships drop back earlier:**
- Increase `FragilityPushbackWeight` (12 ? 18)
- Lower `FragilityThreshold` (0.6 ? 0.5)

### FleetCommander Thresholds (`FleetCommander.cs`)

| Constant | Current | Effect |
|---|---|---|
| `RetreatScoreBelow` | 0.35 | Enter Retreat when material score < 0.35 |
| `RetreatHpBelow` | 0.30 | Enter Retreat when fleet HP < 30% |
| `FallbackScoreBelow` | 0.60 | Enter DefensiveFallback when < 0.60 |
| `AdvanceScoreAtLeast` | 0.90 | Enter Advance when = 0.90 (winning decisively) |
| `HysteresisMargin` | 0.08 | Widen current stance's band to prevent flip-flopping |
| `MinDwellTurns` | 2 | Minimum turns before switching (except Retreat/Hunt) |

**To make the AI more risk-averse:**
- Raise `RetreatScoreBelow` (0.35 ? 0.45)
- Raise `FallbackScoreBelow` (0.60 ? 0.70)

**To make the AI more aggressive:**
- Lower `AdvanceScoreAtLeast` (0.90 ? 0.80)
- Lower `RetreatHpBelow` (0.30 ? 0.20)

### Scanner Selection (`Aiactivescanner.cs`)

| Constant | Current | Effect |
|---|---|---|
| `BearingWeight` | 100 | How much AIEnemyMemory predictions outweigh fog coverage |

**Current behavior:** A predicted enemy position always wins over pure exploration.  
**To make scans more exploratory:** Lower to 50-70.

---

## Debugging & Logging

### Console Log Tags

| Tag | Source | Content |
|---|---|---|
| `[AI][Fleet]` | FleetCommander | Stance changes, material score, roster, contacts |
| `[AI][Scan]` | AIActiveScanPlanner | Scanner selection, facing choice, scan confirmation |
| `[AI][Move]` | AIMovementPlanner | (Debug only) Tile candidates, scores, chosen destination |
| `[AI]` | AIController | Move/attack results, intent (repositioning/lining up/fleeing) |

### Example Log Sequence (one Move phase)

```
[AI][Fleet] stance=Hunt (turn 1 in stance) score=1.00 ownAlive=2/2 enemyEst=2 fleetHp=1.00 contacts=0 nearest=-1 progress=0.00 centroid=(27, 2) home=(27, 2)
[AI][Fleet] roster: WolfClass=Scout frag=0.70(hp 1.00); AthenaClass=Anchor frag=0.20(hp 1.00);
[AI] WolfClass moved to (26, 2) (repositioning)
[AI] AthenaClass moved to (26, 3) (repositioning)
```

### Enabling Debug Logs

Currently `[AI][Move]` logs are in `ClosestTileToCenter` only. To add more:

```csharp
// In AIMovementPlanner.ChooseDestination, after scoring:
Debug.Log($"[AI][Move] {self.shipType} tile={tile} enemy={enemy.shipType} score={score:F1} (attack={attackScore:F1} formation={formationScore:F1})");
```

### Unity Gizmos (Planned Stage 2+)

Future: visual overlays for stance, roles, formation vectors, rally points. Not yet implemented.

---

## Known Limitations

### Stage 2 (Current)

1. **Hunt stance with zero contacts:** Ships advance toward predicted enemy zone but may cluster at map center if no enemies are ever encountered. Non-issue in real play (enemy scanning reveals contacts).

2. **Sequential movement only:** Later ships see earlier ships' new positions. Parallel movement planning (all ships decide simultaneously) is a design decision still open.

3. **Contact deduction from footprint is not guaranteed:** A partially-scanned 3-cell ship might read as a 2-cell ship. The AI treats all candidates equally until an Identified cell reveals the true class.

4. **Formation weights are global constants:** Not per-ship or per-stance. If needed, add a `GetFormationWeight(stance, role)` multiplier.

5. **No terrain awareness yet:** "Use terrain (cover, chokepoints, Impassable/Costly tiles)" is planned but not implemented. The AI currently treats all Passable tiles equally.

6. **No staging-phase actions:** Mines, planes, repair ships are excluded (design decision for later).

7. **Per-ship planners still read true enemy stats:** `AIMovementPlanner` and `AIAttackPlanner` use `context.KnownEnemies` (real ShipInstances with true HP/weapons). FleetCommander is fog-honest via AIContacts, but the planners aren't yet. Fixing this (option B in design notes) requires refactoring the planners to accept `List<AIContact>` instead. Not done yet � flagged as a future improvement.

---

## Future Stages

### Stage 3: Stance-Driven Directional Movement

**Goal:** Make stances control **where the fleet goes** as a whole, not just formation tightness.

**Plan:**
- Add `ScoreDirectionalBias()` method
- Retreat: strong bias away from contacts, toward home edge
- DefensiveFallback: medium bias away from contacts
- Hold: zero directional bias (sticky positioning)
- Advance: medium bias toward contacts

**Status:** Designed, not implemented. User paused to request developer guide.

### Stage 4: Coordinated Battle Phase

**Goal:** Focus fire, target priority set by the commander, hold-fire coordination.

**Candidates:**
- Target priority list (commander sorts known enemies by threat � fragility)
- Focus fire: multiple ships on the same high-priority target
- Hold fire: don't reveal position if no high-value targets exist

**Status:** Not started.

### Stage 5: Tuning & Staging Hooks

**Goal:** Fine-tune weights via playtesting, add staging-phase actions (mines, planes, repair).

**Status:** Not started.

---

## Quick Reference: Key Decisions

| Decision | Current Choice | Alternatives Considered |
|---|---|---|
| Fleet commander state | Instance (owned by AIController) | Static (rejected: doesn't reset between matches) |
| Stance dwell-time | 2 turns minimum | 1 turn (too flip-floppy), 3 turns (too sticky) |
| Cohesion in Hunt | 0.0 (no pull) | 0.4 (spreads too slowly), negative (forced spread, complex) |
| Fragility threshold | 0.6 | 0.5 (too many ships retreat), 0.7 (only Wolf retreats) |
| Scanner selection | Scout-first, then fog coverage | Round-robin (unfair to scouts), random (wastes Wolf) |
| Attack claimed-target | Soft penalty (still allowed) | Hard block (can't pile on for kills) |
| Formation terms | Additive nudges | Hard overrides (breaks attack logic) |

---

## Contact & Contributions

**Original Implementation:** Kiro AI Assistant (2024)  
**Maintained By:** [Your Team Name]  
**Questions:** Ping the AI folder owner before modifying scoring weights or stance logic.

**Before committing changes to AI/, Fleet/ folders:**
1. Test with at least 3 different fleet compositions
2. Verify fog-honesty (FleetCommander never reads context.EnemyShips directly)
3. Run with `[AI][Fleet]` logs enabled and verify stance transitions are reasonable
4. Check that ships don't freeze at map center in Hunt stance (unless zero contacts for 10+ turns, which is unrealistic)

---

*End of Developer Guide. Last updated after Stage 2 completion (Search Coordination + Formation Terms).*
