# AI Refactor Change Log

## Overview
This supplemental change log covers the later AI cleanup and refactor work that was not included in the earlier deployment and combat documentation. The goal was to reduce the size and complexity of the main AI controller while preserving the same gameplay behavior and turn flow.

The refactor focused on splitting the enemy AI into smaller, responsibility-based files so each part of the logic is easier to follow, debug, and extend.

---

## 1. Main AI Controller Reduced to a Coordinator

### `AIController` remains the entry point
The main script in `Assets/Scripts/AI/AiController.cs` still owns the top-level flow for the AI:

- checks the current phase
- verifies it is the AI's turn
- prevents the AI from acting twice in one phase
- routes execution into the correct planner for the current state

### What was removed from the controller
The previous monolithic version contained the actual tactical logic for:

- deployment decisions
- movement selection
- search pattern selection
- attack decision-making
- shared scoring and threat math

Those details were moved out of the controller and into separate files so the file is now easier to read and maintain.

---

## 2. Deployment Responsibility Moved Out

### New file: `Assets/Scripts/AI/AIDeploymentPlanner.cs`
This file handles AI deployment choices.

Responsibilities:
- choose a valid deployment anchor and rotation
- score candidate placements
- confirm the ship's placement through `GridManager`
- call `TurnManager.ConfirmDeployment(...)` once placement succeeds

This keeps the deployment decision logic separate from the top-level AI coordinator.

---

## 3. Movement Logic Moved to a Dedicated Planner

### New file: `Assets/Scripts/AI/AIMovePlanner.cs`
This file owns the movement decision phase.

Responsibilities:
- gather every legal move destination within the ship's movement range
- evaluate each tile by expected damage potential
- determine whether the AI should advance or retreat based on health state
- pick the best valid move or fallback toward the enemy if no attack is possible

This replaced the old large inline movement block that lived directly inside the controller.

---

## 4. Search Logic Split into Its Own File

### New file: `Assets/Scripts/AI/AISearchPlanner.cs`
This file handles the AI search phase.

Responsibilities:
- iterate over available search patterns
- test each pattern against board bounds and enemy occupied cells
- score candidate search anchors
- select the best pattern and board anchor
- call `GridManager.ResolveSearch(...)`
- advance the turn after the search decision is resolved

This prevents the AI controller from mixing search logic with move and battle logic.

---

## 5. Attack Logic Split Out

### New file: `Assets/Scripts/AI/AIAttackPlanner.cs`
This file is responsible for battle-phase decisions.

Responsibilities:
- check all available weapons
- reject weapons that are not ready or not legal to fire
- score weapons by expected value
- choose the best weapon to fire
- resolve the actual attack through `GridManager.ResolveAttack(...)`

This keeps the battle-phase logic isolated and easier to tune.

---

## 6. Shared AI Calculations Consolidated into Utilities

### New file: `Assets/Scripts/AI/AIUtilities.cs`
This file contains the shared AI logic used repeatedly by the planners.

Included helpers:
- `GetReachableAnchors(...)`
- `TryMove(...)`
- `ClosestReachableTileToEnemy(...)`
- `FindSafestReachableTile(...)`
- `NearestEnemyDistance(...)`
- `ScoreAttack(...)`
- `DangerAtTile(...)`
- `BestExpectedDamageAtAnchor(...)`
- `CanFireFromAnchor(...)`
- `CanFire(...)`
- `ExpectedValue(...)`

### Why this mattered
This was the key design improvement. Multiple planners all needed the same underlying rules:

- which tiles are legal
- whether a weapon can hit
- how threatening a tile is
- how much damage a weapon is statistically worth

Rather than repeating these calculations in multiple places, they now live in one utility layer.

---

## 7. Resulting Structure

The AI is now organized by responsibility:

- `AIController` = turn loop and coordinator
- `AIDeploymentPlanner` = deployment decisions
- `AIMovePlanner` = movement decisions
- `AISearchPlanner` = search action decisions
- `AIAttackPlanner` = battle action decisions
- `AIUtilities` = shared scoring and validation calculations

This makes the code easier to read, easier to extend, and much less fragile when new combat systems or AI behavior are introduced.

---

## 8. Validation

The refactor was checked by compiling the project:

- `dotnet build Assembly-CSharp.csproj --no-restore`

### Result
The project still builds successfully.

There are still 2 pre-existing Unity serialization warnings unrelated to the AI split:
- `WeaponProfile.cs` warning for nullable `ammo`
- `DefenseProfile.cs` warning for nullable `uses`

These warnings did not block the build and were not introduced by the controller refactor.

---

## 9. Summary
The later AI work focused on maintainability and readability, not gameplay redesign. The original behavior was preserved while the code was reorganized into smaller, clearer components. This makes future AI updates, tuning, and bug fixes much safer and easier to work with.
