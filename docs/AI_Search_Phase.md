# Technical Change Log

## Overview
This update adds a deployment stage, a split board layout, AI deployment logic, and a more tactical AI move/attack system. The project now has a clearer turn flow and a stronger separation between board validation, player input, and AI decision-making.

The changes are spread across the following systems:
- AI logic in `Assets/Scripts/AI/AiController.cs`
- Deployment AI in `Assets/Scripts/AI/DeploymentAI.cs`
- Board validation and placement logic in `Assets/Scripts/Grid/GridManager.cs`
- Player input and deployment flow in `Assets/Scripts/Grid/TestShipController.cs`
- Turn-state management in `Assets/Scripts/Turns/TurnManager.cs`

---

## 1. Turn and Phase Flow

### `Phase` Enum
The project uses a phase-based turn system to control what actions are legal at a given moment.

Relevant states:
- `Deployment`
- `Move`
- `Search`
- `Battle`

This means the game no longer instantly jumps into combat once a scene starts. Instead, the turn loop now gates setup and battle behavior through explicit states.

### `TurnManager.AdvancePhase()`
Purpose:
- Moves the game from one phase to the next.

What it does:
- If current phase is `Deployment`, it refuses to advance further and waits for placement confirmation.
- If phase is `Move`, it moves to `Search`.
- If phase is `Search`, it moves to `Battle`.
- If phase is `Battle`, it swaps to the next player and returns to `Move`.

Why this matters:
- It acts as the central turn controller for the game loop.
- It prevents players from acting out of sequence.

### `TurnManager.ConfirmDeployment(PlayerId player)`
Purpose:
- Records when a player has finalized ship placement.

What it does:
- Stores a confirmation flag for `PlayerA` or `PlayerB`.
- Once both players confirm, it calls `StartCombat()`.

Why this matters:
- It blocks combat until both sides are ready.
- It allows hidden AI placement during the setup phase without prematurely revealing enemy ships.

### `TurnManager.StartCombat()`
Purpose:
- Starts the actual combat phase after deployment is complete.

What it does:
- Resets `currentPlayer` to `PlayerA`.
- Sets the start phase to `Move`.
- Begins the battle loop in a consistent state.

---

## 2. Player Deployment System

### `TestShipController.Update()`
Purpose:
- Handles both deployment input and regular play input depending on the current phase.

What it does:
- If the phase is `Deployment`, it invokes the deployment preview and deployment control logic.
- Otherwise, it waits for space input to move the phase forward and handles movement or attack input based on current player and phase.

Why this matters:
- It unifies whether the player is in setup or combat without spreading input logic across several scripts.

### `TestShipController.HandleDeploymentInput(ShipInstance ship)`
Purpose:
- Allows the human player to place ships during the deployment phase.

What it does:
- Reads arrow keys to move a placement cursor.
- Reads `Q` and `E` to rotate the ship.
- Uses `GridManager.CanDeployShip()` to validate the placement before updating the cursor or rotation.
- On `Enter`, calls `GridManager.DeployShip()` and then confirms the placement with `TurnManager.ConfirmDeployment()`.

Why this matters:
- Keeps board rules centralized in the grid manager.
- Prevents invalid placements such as off-grid tiles or the wrong side of the board.

### Deployment Cursor and Preview
The player is not just choosing a final anchor. They are also moving a cursor in a deployment zone and receiving a visual preview of their selected ship footprint.

This preview is controlled through:
- `GridManager.SetDeploymentPreview(...)`
- `GridManager.OnDrawGizmos()`

The preview is drawn in a transparent blue and only appears when the preview is active.

---

## 3. Grid and Placement Rules

### `GridManager.BuildGrid()`
Purpose:
- Generates the runtime grid tile dictionary.

What it does:
- Creates a list of `Tile` objects for every valid board cell.
- Used at startup to support placement, occupancy checks, and hit detection.

### `GridManager.IsInBounds(Vector2Int pos)`
Purpose:
- Checks whether a given cell exists in the board.

### `GridManager.IsOccupied(Vector2Int pos)`
Purpose:
- Tells whether a tile already has a ship occupying it.

### `GridManager.PlaceShip(ShipInstance ship, List<Vector2Int> cells)`
Purpose:
- Writes ship ownership into all tile entries used by the ship footprint.

### `GridManager.RemoveShip(ShipInstance ship)`
Purpose:
- Removes a ship from all tiles it previously occupied.

### `GridManager.CanPlaceShip(ShipInstance ship, Vector2Int candidateAnchor, int candidateRotation)`
Purpose:
- Validates a move or placement without immediately mutating the board.

What it checks:
- All ship cells are inside the board.
- None of the occupied cells are overlapped by another ship.
- The ship can legally exist in that footprint.

Why this matters:
- This is the central validation method used by both the AI and the human player.
- It keeps movement and deployment logic consistent.

### `GridManager.IsDeploymentZone(PlayerId player, Vector2Int cell, int deadSpaceColumns)`
Purpose:
- Determines whether a particular cell belongs to a given player's deployment side.

What it does:
- Splits the board into left and right deployment zones.
- Leaves a central dead-space band between them.

### `GridManager.CanDeployShip(ShipInstance ship, PlayerId player, Vector2Int candidateAnchor, int candidateRotation, int deadSpaceColumns)`
Purpose:
- Validates whether a ship can be placed for a specific player in the deployment phase.

What it checks:
- legal footprint placement
- in-bounds cells
- no overlap
- all cells lie inside the correct side of the board

### `GridManager.DeployShip(ShipInstance ship, PlayerId player, Vector2Int candidateAnchor, int candidateRotation, int deadSpaceColumns)`
Purpose:
- Finalizes deployment for a ship.

What it does:
- Rejects invalid placements.
- Updates the ship anchor and rotation.
- Calls `PlaceShip()` so the board reflects the new position.

### `GridManager.MoveShip(ShipInstance ship, Vector2Int newAnchor, int newRotationDegrees)`
Purpose:
- Performs a valid board move.

What it checks:
- movement range
- in-bounds footprint
- free space

What it does:
- Removes the ship from the old cells.
- Updates the anchor and rotation.
- Replaces the ship in the new cells.

This is the core movement command used by both AI and human controls.

---

## 4. Hidden Enemy Placement and Visual Gating

### `GridManager.OnDrawGizmos()`
Purpose:
- Draws the grid and ship placement in the editor during play.

What it does:
- Renders Player A ships in cyan and Player B ships in red.
- Hides Player B ships during the `Deployment` phase to keep their placement hidden.
- Draws the deployment dead-space zone.
- Draws the blue transparent preview when a deployment preview is active.

Why this matters:
- It supports the requested fog-of-war behavior for setup.
- It makes the hidden enemy deployment visible only after both players have confirmed.

### `GridManager.DrawDeploymentDeadSpace()`
Purpose:
- Visualizes the central dead-space band.

What it does:
- Marks the middle board section with a translucent highlight.
- Prevents ships from being placed in that band.

---

## 5. AI System

### `AIController.Update()`
Purpose:
- Main AI loop run every frame.

What it does:
- If current phase is `Deployment`, it calls `DecideDeployment()`.
- If it is not `PlayerB`'s turn, it returns.
- Tracks the last phase and player so the AI only acts once per turn.
- Calls `DecideMove()` during `Phase.Move`.
- Calls `DecideAttack()` during `Phase.Battle`.

Why this matters:
- This script is the AI orchestration layer.
- It is responsible for deciding when AI movement or AI attack should occur.

### `AIController.DecideDeployment()`
Purpose:
- Chooses a valid starting placement for the enemy ship.

What it does:
- Skips if already decided.
- Calls `DeploymentAI.ChooseDeployment()`.
- Uses `GridManager.DeployShip()` to apply the selected anchor and rotation.
- Calls `TurnManager.ConfirmDeployment(PlayerId.PlayerB)` when placement succeeds.

Why this matters:
- It keeps the AI deployment separate from the player deployment flow.
- It allows the enemy to be placed automatically without the player seeing the exact tiles until confirm.

### `AIController.DecideMove(ShipInstance aiShip, ShipInstance enemyShip)`
Purpose:
- Selects the best movement option for the AI ship.

What it does:
- Finds all reachable anchors with `GetReachableAnchors()`.
- Scores each candidate using `BestExpectedDamageAtAnchor()` and risk values.
- Chooses the best move based on attack value, danger, and health state.
- Uses `FindSafestReachableTile()` if the ship is low on health and should retreat.

Why this matters:
- This replaces a simple "move toward the player" behavior with scoring-based choice logic.
- The AI now compares multiple valid options before moving.

### `AIController.GetReachableAnchors(ShipInstance ship)`
Purpose:
- Lists all legal positions within movement range.

What it checks:
- Tiles within movement distance.
- Legal board bounds.
- No overlapping occupation with other ships.

### `AIController.TryMove(ShipInstance ship, Vector2Int destination)`
Purpose:
- Attempts to move the AI ship to a selected destination.

What it does:
- Calls `GridManager.MoveShip()`.
- Logs the move or logs rejection if the move is invalid.

### `AIController.ClosestReachableTileToEnemy(...)`
Purpose:
- Fallback behavior when no strong attack tile exists.

What it does:
- Chooses the reachable tile closest to the enemy ship.

### `AIController.FindSafestReachableTile(...)`
Purpose:
- Finds the best retreat tile when the AI is in low health.

What it uses:
- `DangerAtTile()`
- `NearestEnemyDistance()`

### `AIController.DangerAtTile(Vector2Int tile, ShipInstance aiShip)`
Purpose:
- Estimates how dangerous a target tile is when evaluating movement choices.

What it does:
- Considers whether enemy weapons can strike from the relevant positions.
- Adds risk from legal enemy attacks based on expected damage.

### `AIController.ScoreAttack(...)`
Purpose:
- Evaluates whether a chosen movement position is tactically good.

What it considers:
- opportunity for lethal damage
- expected damage output
- danger to the AI ship
- risk of counterattacks

### `AIController.ClosestReachableTileToEnemy(...)`
Purpose:
- Fallback movement logic while approaching the opponent when no attack option is clearly dominant.

---

## 6. AI Combat Decision Logic

### `AIController.DecideAttack(ShipInstance aiShip, ShipInstance enemyShip)`
Purpose:
- Chooses the best weapon to fire when the AI is in `Battle` phase.

What it does:
- Loops through each weapon on the ship.
- Uses `CanFire()` to validate whether a weapon can legally fire.
- Uses `ExpectedValue()` to score each valid weapon.
- Selects the weapon with the highest score and fires it through `GridManager.ResolveAttack()`.

Why this matters:
- The AI now evaluates weapons by expected output instead of blindly picking the first available one.

### `AIController.CanFire(ShipInstance attacker, ShipInstance target, WeaponProfile weapon)`
Purpose:
- Checks if the weapon can legally fire this turn.

What it verifies:
- weapon charge is ready
- target domain matches the weapon domain or is set to both
- target is within range

### `AIController.ExpectedValue(WeaponProfile weapon)`
Purpose:
- Estimates the average damage output of a weapon from its roll table.

What it does:
- Takes each `RollTier`.
- Computes the chance of landing in that tier.
- Multiplies by the damage value.
- Sums the totals to get an expected damage number.

This is not a single roll outcome. It is a statistical average used to judge how strong a weapon is in scoring.

### `AIController.BestExpectedDamageAtAnchor(...)`
Purpose:
- Returns the best possible expected damage a ship could deal from a given anchor position.

This function is used during movement scoring to determine whether relocating to a tile produces a strong tactical advantage.

---

## 7. Deployment AI

### `DeploymentAI.ChooseDeployment(ShipInstance ship, GridManager grid, int deadSpaceRows)`
Purpose:
- Finds the best legal starting position for a ship during setup.

What it does:
- Iterates through all rotations and all valid grid cells.
- Calls `GridManager.CanDeployShip()` for each candidate.
- Scores the valid candidates with `ScorePosition()`.
- Returns the highest-scoring result.

Why this matters:
- This creates a reusable script for automatically placing AI ships without needing custom per-ship logic.

### `DeploymentAI.ScorePosition(ShipInstance ship, GridManager grid, Vector2Int anchor, int rotation, int deadSpaceRows)`
Purpose:
- Evaluates how strong a candidate deployment position is.

What it considers:
- centrality of the position
- distance from the deployment dead-space band
- surrounding mobility

This helps the AI choose a placement that is both legal and tactically useful.

---

## 8. Combat Resolution Logic

### `GridManager.ResolveAttack(ShipInstance attacker, ShipInstance target, WeaponProfile weapon)`
Purpose:
- Executes a full combat attack once the AI or player chooses a target.

What it checks:
- weapon charge is ready
- domain matches the target
- target is within range

What it does:
- calls `RollWeapon()`
- applies the damage to the target
- destroys the ship if health reaches zero
- removes the target ship from the grid

This is the immediate result side of the turn. It is separate from the AI scoring functions, which estimate value before actually resolving damage.

### `GridManager.RollWeapon(WeaponProfile weapon)`
Purpose:
- Simulates a single d20 attack roll and resolves the correct tier.

It returns a `RollTier` object that includes:
- damage value
- output label
- min/max roll range

---

## 9. What Changed in Practice

### Added Features
- deployment phase before combat
- split deployment board with a dead-space center
- legal deployment validation for player and AI
- hidden enemy setup during deployment
- blue preview highlighting for ship placement
- tactical AI move scoring rather than simple forward movement
- tactical AI attack scoring using expected value
- automatic enemy deployment confirmation

### Improved Stability
- Movement checks are centralized in `GridManager`.
- Deployment checks are centralized in a dedicated validation method.
- Turn progression is controlled in one place.
- The AI depends on the same validation rules as the human player.

---

## 10. Verified State
Build verification was run with:

`dotnet build Assembly-CSharp.csproj --no-restore`

Result:
- Build succeeded.
- Existing warnings remain for nullable serialization fields in weapon / defense profile data, but they do not block the project from compiling.

---

## 11. File Summary
Core implementation files involved in this change set:
- `Assets/Scripts/AI/AiController.cs`
- `Assets/Scripts/AI/DeploymentAI.cs`
- `Assets/Scripts/Grid/GridManager.cs`
- `Assets/Scripts/Grid/TestShipController.cs`
- `Assets/Scripts/Turns/TurnManager.cs`
- `docs/Changelog.md`
- `docs/Change_Log.md`
