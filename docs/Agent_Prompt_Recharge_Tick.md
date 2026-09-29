# Admirals - Feature: Recharge Tick Infrastructure

You are working on the Unity project **Admirals**.

This is a small, self-contained infrastructure addition, not a new phase. It adds the passive recharge tick that `ChargeState.turnsUntilRecharge` was always designed for, but it does **not** make anything actually start recharging. Nothing currently sets `turnsUntilRecharge` above `0` anywhere in the codebase, so after this task, observable gameplay behavior should be unchanged. That is the correct, intended result, not a sign the feature did nothing.

---

# 1. Read first

Before changing anything, read:

```text
docs/AGENTS.md
docs/DEVELOPER_GUIDE.md
```

Then inspect the actual current implementation of:

```text
Assets/Scripts/Combat/ChargeState.cs
Assets/Scripts/Ships/ShipInstance.cs
Assets/Scripts/Grid/GridManager.cs (HandlePhaseChanged)
Assets/Scripts/Turns/TurnManager.cs
```

Confirm `ChargeState.turnsUntilRecharge` and `IsReady` exist exactly as described below before writing anything. The source code is authoritative.

Follow the project's KISS/YAGNI rules.

---

# 2. What already exists, do not rebuild it

```text
ChargeState.remaining            — current ammo/uses, -1 means infinite
ChargeState.turnsUntilRecharge   — turns until this slot is usable again, 0 = ready now
ChargeState.IsReady              — true when turnsUntilRecharge == 0 AND remaining != 0
ShipInstance.weaponCharges       — one ChargeState per weapon
ShipInstance.defenseCharges      — one ChargeState per defense
```

Every field this task needs already exists. This task adds exactly one thing: a method that decrements `turnsUntilRecharge` once per relevant turn, and the call that triggers it.

---

# 3. Exact scope

Implement only:

1. A method that ticks down `turnsUntilRecharge` by 1 for every `ChargeState` on one ship, for both `weaponCharges` and `defenseCharges`, never going below `0`.
2. A call to that method for every living ship belonging to the **current acting player only**, triggered once when `Phase.End` is entered.

Do not implement anything that sets `turnsUntilRecharge` above `0`. Do not add recharge values to `ShipData`. Do not change `ResolveAttack`, ammo consumption, or defense use. Do not touch mines, planes, or repair, those don't exist yet and are out of scope here.

---

# 4. Design decisions, locked

## 4.1 The tick method belongs on `ShipInstance`

`ShipInstance` already owns `weaponCharges` and `defenseCharges` and already has a small setup method in the same spirit (`InitializeCharges`). Add a matching method there:

```text
ShipInstance.TickRecharge()
  for each ChargeState in weaponCharges:
    if turnsUntilRecharge > 0:
      turnsUntilRecharge -= 1
  for each ChargeState in defenseCharges:
    if turnsUntilRecharge > 0:
      turnsUntilRecharge -= 1
```

Do not put this loop in `GridManager`, `CombatResolver`, or a new class. The ship owns its own charge state; ticking it is the ship's own responsibility, same pattern as `InitializeCharges`.

## 4.2 Only the acting player's ships tick, not both sides

`Phase.End` occurs once per player, at the end of that player's own Move-through-Battle sequence. Tick only `turnManager.CurrentPlayer`'s living ships at that moment, not the opposing player's. This matches the existing pattern already used for Search (`Fog.RunActiveSearch(turnManager.CurrentPlayer, match)`), current-player-only, not both sides, at a per-player phase event.

Do not tick both players' ships on every `End`. A ship should recharge based on its own side's turn ending, not the opponent's.

## 4.3 Where the call goes

In `GridManager.HandlePhaseChanged`, inside the existing `else if (newPhase == Phase.End)` branch, alongside `Fog.ClearAllActiveMarks()` and the active scan preview clear already there. Add one loop over the current player's living ships calling `TickRecharge()` on each. Do not create a new phase-handling branch or a new event subscriber.

## 4.4 Dead ships need no special handling

Destroyed ships are already removed from `PlayerState.ships` elsewhere in the codebase. Looping the live `ships` list for the current player already excludes them naturally. Do not add a `currentHealth <= 0` guard inside the tick loop, it would be redundant.

---

# 5. Explicitly DO NOT implement

Do NOT:

```text
set turnsUntilRecharge above 0 anywhere, for any weapon or defense
add recharge values to ShipData for any ship
change ResolveAttack, ammo consumption, or defense saving throws
implement mines, planes, or repair
implement any Staging action
change AI logic
add a new manager class for this
```

If, while reading `ChargeState`/`ShipInstance`, you notice something that looks like it was meant to support recharge-on-spend but doesn't yet, report it. Do not implement it as part of this task.

---

# 6. KISS/YAGNI enforcement

Do not create:

```text
RechargeManager
RechargeSystem
ITickable
TurnTickService
```

One method on `ShipInstance`, one loop in an existing `HandlePhaseChanged` branch. Nothing else is needed for this task.

---

# 7. Verification

Test these directly in Unity Play mode. Report actual results, not code review.

## Test 1: No observable behavior change (this is the expected, correct result)

Run a normal full match sequence: movement, scanning, an attack, several full turn cycles. Confirm every weapon and defense behaves exactly as before this task, since nothing currently sets `turnsUntilRecharge` above `0`, `IsReady` should be unaffected for every existing `ChargeState` in the game.

## Test 2: The tick actually decrements when a value is present

Using a temporary debug hook (a `[ContextMenu]` method, a direct field edit in the Inspector during Play mode, or an equivalent already-established debug pattern from this project), set one `ChargeState.turnsUntilRecharge` on a living ship to `2`, leaving `remaining` at any non-zero value.

Advance through that ship's owning player's phases until `End` is reached once. Verify `turnsUntilRecharge` is now `1`, and `IsReady` is still `false`.

Advance through a full turn cycle again to reach that player's `End` a second time. Verify `turnsUntilRecharge` is now `0`, and `IsReady` is now `true` (assuming `remaining != 0`).

## Test 3: Never goes below zero

Using the same debug approach, confirm a `ChargeState` already at `turnsUntilRecharge == 0` stays at `0` after `End`, it does not become `-1`.

## Test 4: Only the acting player's ships tick

Set `turnsUntilRecharge = 2` on one ship for each player. Advance only through Player A's full cycle to `End`. Verify Player A's ship ticked down to `1` and Player B's ship is still at `2`, unchanged.

## Test 5: Existing systems unaffected

Confirm movement, fog, scanning, and combat all behave identically to before this task across a full multi-turn playthrough.

---

# 8. Documentation

Update `docs/DEVELOPER_GUIDE.md` only where it currently describes `ChargeState`/recharge as unimplemented. Correct that section to state the tick infrastructure exists and runs at `End`, but that no weapon, defense, mine, or plane currently sets a recharge value, so it is presently inert. Do not rewrite unrelated combat documentation.

Update `docs/AGENTS.md` only if this changes an architectural rule documented there. Do not rewrite unrelated instructions. Do not remove or modify project credits.

---

# 9. Before finishing

```text
[ ] docs/AGENTS.md read
[ ] docs/DEVELOPER_GUIDE.md read
[ ] ChargeState.turnsUntilRecharge and IsReady confirmed as already existing
[ ] ShipInstance.TickRecharge() added, ticks both weaponCharges and defenseCharges
[ ] Tick never decrements below 0
[ ] Call added in GridManager.HandlePhaseChanged, Phase.End branch only
[ ] Only turnManager.CurrentPlayer's living ships are ticked
[ ] No code sets turnsUntilRecharge above 0 anywhere
[ ] No ShipData changes
[ ] No new manager class created
[ ] Test 1 (no behavior change) passed in Unity
[ ] Test 2 (manual decrement over two End phases) passed in Unity
[ ] Test 3 (never goes negative) passed in Unity
[ ] Test 4 (only acting player ticks) passed in Unity
[ ] Test 5 (existing systems unaffected) passed in Unity
[ ] DEVELOPER_GUIDE.md updated
[ ] AGENTS.md updated only if necessary
```

---

# 10. Final response format

Report:

## Changed files

List every file touched.

## What changed

Explain the `TickRecharge` method and where it's called from, in plain terms.

## Verification

Report actual Unity Play mode results for Tests 1 through 5, including the exact before/after `turnsUntilRecharge` values observed in Test 2, not code review claims.

## Unrelated issues found

List anything noticed but not fixed, per Section 5.

## Documentation

Report what changed in `DEVELOPER_GUIDE.md` and `AGENTS.md`, if anything.

Stop after this task is complete. Do not implement recharge-on-spend for weapons, defenses, mines, or planes. That is separate, future work once this infrastructure is verified.
