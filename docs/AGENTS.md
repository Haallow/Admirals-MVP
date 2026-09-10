# AGENTS.md — Admirals

Instructions for any AI coding agent (or human) working in this repository. Read this before writing code.

---

## Project

**Admirals** is a Unity tile-based naval combat game. Turn-based, grid movement with rotatable multi-tile ships, fog of war, per-ship scanning and combat, dice-based resolution.

## Credits

- **Game design, rules, and ship stats**: Tom (Hallow) — original design doc, ship cards, and all game-balance numbers are his.
- **Art (UI, ship sprites, environment)**: in progress, separate track, not yet in this repo.
- **Architecture planning and implementation guidance**: developed collaboratively with Claude (Anthropic), following the milestone plans in `/docs`.
- Whenever this file, the milestone docs, or code comments are edited, **do not remove or rewrite this credits section**. Add to it if new contributors join, don't replace what's here.

---

## How work in this repo is planned

Every feature is broken into milestones, documented before being built. Milestone docs live in `/docs` (or wherever the team places them, check the repo root and `/docs` first) and follow a consistent shape:
- Goal (one sentence, quoted from the master plan)
- What "done" looks like (a concrete, testable checklist)
- Step-by-step build order
- Suggested file list
- What NOT to build yet
- Next milestone preview

**Before writing any code, find and read the current milestone doc.** If one doesn't exist for the work being requested, ask for one to be written first, don't improvise scope.

### Milestone status (update this list as milestones complete)
- [x] Milestone 1 — Grid + hardcoded ship placement
- [x] Milestone 2 — Move/rotate + collision validation
- [x] Milestone 3 — Move/Search/Battle turn-phase loop + player/ownership gating
- [ ] Milestone 4 — Ship data model states (domain, weapon/defense profiles, charges) + one fully-statted ship
- [ ] Milestone 5+ — Fog of War, scanning, combat resolution, remaining ships, abilities (planned, not yet scoped in detail)

---

## KISS/YAGNI — the house rule

This project is built one small, verifiable increment at a time. Every milestone doc says explicitly what NOT to build yet. Follow that list strictly:
- Don't add hooks, interfaces, or "future-proofing" for systems outside the current milestone's scope.
- Don't build a generic framework when the current milestone only needs one or two concrete cases (e.g. hardcode two ships before building a data-driven ship system).
- Don't introduce a new package, dependency, or Unity feature (Tilemap, new Input System, networking, etc.) unless the current milestone's doc calls for it.
- If a milestone doc's "done" checklist is satisfied, stop there. Don't keep polishing or adding adjacent features not on the list.

If something seems obviously missing or wrong in a milestone doc, flag it rather than silently deviating from it.

---

## Established code conventions (do not break these without discussion)

- **Data vs. behavior separation**: `Tile`, `ShipInstance`, and the various profile/state classes (`WeaponProfile`, `DefenseProfile`, `ChargeState`, etc.) are plain data. Logic that acts on them lives in manager classes (`GridManager`, `TurnManager`) or, for abilities, dedicated behavior classes implementing a shared interface. Don't put game logic methods directly on data classes beyond simple derived-value helpers (e.g. `ShipInstance.GetOccupiedCells()` is fine, a `ShipInstance.ResolveAttack()` is not).
- **Plain C# classes vs. MonoBehaviours**: only make something a `MonoBehaviour` if it needs to live on a GameObject and receive Unity lifecycle callbacks (`Awake`, `Update`, etc.) or needs Inspector visibility as a scene component. Data containers (`Tile`, `ShipInstance`, profile classes) are plain `[Serializable]` classes or plain classes, not MonoBehaviours.
- **Single validation path**: grid placement/movement validity always goes through `GridManager.CanPlaceShip`, don't write parallel validation logic elsewhere.
- **Atomic state changes**: never leave the grid (or any state) partially updated if a validation step fails partway through an operation.
- **Ability system**: implemented via a shared interface (`IShipAbility`), one small class per ability, registered by string id. Don't add a new `switch` statement keyed on ability name, extend the registry instead.
- **Folder structure**: scripts are grouped by system, not by type — `Grid/`, `Ships/`, `Turns/`, `Combat/`. Keep new files in the folder matching their system.
- **Naming**: `PascalCase` for classes/methods/public fields, `camelCase` for private/serialized fields. Match existing file naming (`GridManager.cs`, not `grid_manager.cs`).

---

## Quality bar

- Every new script should compile cleanly and be verifiable against the current milestone doc's checklist before being considered done.
- Prefer `Debug.Log`/`Debug.Assert` verification over trusting code "looks right" — every milestone so far has been proven via Console output and/or Gizmos before moving on.
- Comment *why*, not *what*, especially on anything that's intentionally a placeholder/stub for a later milestone (e.g. `// Stub. Real logic arrives in Milestone 2.`).
- Don't rename or restructure existing working code as a side effect of unrelated work, propose it separately if it seems warranted.

---

## Version control

- Standard Unity `.gitignore` (`Library/`, `Temp/`, `Obj/`, `Build/`, `Logs/` excluded).
- Git LFS tracks true binary assets (images, audio, models), not scenes/prefabs/materials (kept as readable YAML via Force Text serialization).
- `.meta` files are always committed.
- One feature branch per milestone, merged via PR even when working solo.
