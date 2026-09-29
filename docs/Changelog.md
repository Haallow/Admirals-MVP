# Changelog

## 2026-09-19

### Added

- Added a `Deployment` phase before the normal `Move`, `Search`, and `Battle` phases.
- Added simultaneous deployment confirmation tracking for both players.
- Added left/right deployment zones separated by a configurable central dead-space band.
- Added deployment validation for ship bounds, footprints, rotations, occupancy, ownership, and deployment-zone limits.
- Added keyboard-controlled Player A deployment:
  - Arrow keys move the deployment position.
  - `Q` and `E` rotate the ship.
  - `Enter` confirms deployment.
- Added `DeploymentAI` as a separate AI file in `Assets/Scripts/AI`.
- Added AI deployment scoring based on:
  - Valid placement.
  - Central row access.
  - Distance from the central dead-space band.
  - Available movement options after deployment.
- Added a translucent blue preview for Player A's current deployment footprint.
- Added a translucent central-band visualization in the Scene view.
- Added hidden deployment behavior for Player B's ship. The ship remains registered for collision and game logic but is not rendered until deployment ends.
- Added automatic transition to the normal combat loop after both players confirm.
- Added Unity VS Code extension recommendations and an attach-to-Unity debug launch configuration.
- Added the Unity AI Assistant gateway project settings file.

### Changed

- Changed deployment zones from horizontal top/bottom regions to vertical left/right regions.
- Changed the deployment dead-space setting from a hardcoded local value to the configurable `GridManager` field `deploymentDeadSpaceColumns`, defaulting to `2` columns.
- Removed the old hardcoded starting placement for both ships. Ships now wait for deployment decisions.
- Updated `GridManager` to expose grid dimensions and deployment-zone APIs.
- Updated Player B AI to deploy automatically during the Deployment phase.
- Updated the existing AI movement logic to score legal movement positions instead of always moving directly toward the enemy.
- Added AI movement scoring for expected damage, lethal attacks, target health, danger, self health, counterattack risk, retreating, and fallback approach movement.
- Added low-health retreat behavior for the combat AI, using a default threshold of `25%` health.
- Updated the Wolf Class maximum health from `500` to `1000`.

### Fixed

- Prevented normal movement, attack, domain-toggle, and phase-advance controls from running during deployment.
- Prevented Player B's deployment position from being visually revealed before both players confirm.
- Replaced the deprecated Unity object lookup API with `FindAnyObjectByType<TurnManager>()`.

### Validation

- `dotnet build Assembly-CSharp.csproj --no-restore` completed successfully.
- The build still reports the two existing Unity serialization warnings for nullable `ammo` and `uses` fields.

### Notes

- The current deployment dead space is `2` columns and can be changed through the `GridManager` Inspector.
- The staged changes also include machine-specific Unity AI Assistant gateway settings under `ProjectSettings/AI.Assistant/GatewaySettings.asset`.
