# Admirals - Fix: Apply Armor to Combat Damage

You are working on the Unity project **Admirals**.

This is a targeted mechanic addition, not a new phase. `ShipInstance.armor` already exists as a field and is already populated with real values for every ship, but nothing currently reads it. Damage is applied raw. Add armor-based damage reduction and nothing else.

---

# 1. Read first

Before changing anything, read:

```text
docs/AGENTS.md
docs/DEVELOPER_GUIDE.md
```

Then inspect the actual current implementation of:

```text
Assets/Scripts/Combat/CombatResolver.cs
Assets/Scripts/Ships/ShipInstance.cs
Assets/Scripts/Combat/RollTier.cs
```

Confirm `ResolveAttack`'s exact current damage application line before changing it. The source code is authoritative; if the developer guide describes damage resolution differently, follow the source.

Follow the project's KISS/YAGNI rules.

---

# 2. Locked formula, do not deviate

```text
effectiveDamage = round(rawDamage * (1 - armor * 0.0015))
// 0.15% damage reduction per point of armor.
// Current armor tiers top out at 200 (30% reduction). If a future ship or
// upgrade pushes armor past ~667, reduction goes negative (damage increases) —
// not a concern at current values, just don't silently raise the cap without
// revisiting this formula.
```

Equivalently: each point of armor reduces damage by 0.15%. At the game's current armor tiers this gives:

| Armor | Reduction |
|---|---|
| 50  | 7.5%  |
| 100 | 15%   |
| 150 | 22.5% |
| 200 | 30%   |

Rules:

* `rawDamage` is the damage value from the `RollTier` the attack roll landed on.
* If `rawDamage == 0` (a Miss), the result stays `0`. Do not apply the formula to a Miss.
* If `rawDamage > 0`, the result is never less than `1`, even against very high armor. Implement this as an explicit floor/clamp, not as something the arithmetic happens to guarantee — see Test 4's note on why this can't be observed with current data but must still exist in code.
* Round to the nearest integer using standard rounding (`Mathf.RoundToInt` or equivalent), not floor or ceiling.
* Armor never reduces damage to zero or below. There is no maximum-armor invincibility case.

Do not invent a different formula. Do not add flat subtraction, percentage caps, or armor penetration. Those are not part of this task.

---

# 3. Exact scope

Implement only:

1. The damage reduction formula above, applied at the point `ResolveAttack` currently subtracts raw damage from `target.currentHealth`.
2. A small, testable helper function for the formula so it can be verified in isolation, not inlined as a one-off expression if the existing code style in `CombatResolver` favors small named methods. Match whatever the file already does for similar small calculations (see `RollWeapon` for the existing style).
3. Update the existing combat log line to show both the raw roll damage and the armor-reduced damage actually applied, so the reduction is visible in Console during verification.

Do not implement:

* defense saving throws;
* `sideEffectId` execution;
* ammo/charge consumption changes;
* burning/sinking or any other status effect;
* any UI display of armor or damage numbers.

Those are separate, already-known future work. This task is the formula only.

---

# 4. Where this goes

The formula applies inside `CombatResolver.ResolveAttack`, at the exact point where `result.damage` is currently subtracted from `target.currentHealth`. Do not move damage application anywhere else, and do not duplicate the formula in `AIController` or `TestShipController`. Those callers already route through `ResolveAttack` and should not need to know armor exists.

---

# 5. Explicitly DO NOT implement

Do NOT:

```text
change the roll/tier lookup logic
change ammo/charge consumption
change fog, movement, or scanning
implement defense saving throws
implement status effects
add a UI damage display
add armor penetration or armor-piercing weapon flags
change any ship's armor value in ShipData
```

If you notice armor values that look wrong or inconsistent with the ship cards while working in `ShipData.cs`, report it instead of changing it.

---

# 6. KISS/YAGNI enforcement

Do not create:

```text
DamageCalculator
ArmorResolver
CombatMath
IDamageFormula
```

A single small method on `CombatResolver` (or a `static` helper in the same file, matching existing conventions) is sufficient. Do not introduce a new class or interface for one formula.

---

# 7. Verification

Test these directly in Unity Play mode and by inspecting logged values. Report actual results, not code review.

## Test 1: Zero armor is a no-op

Temporarily set (or find) a ship with `armor == 0`. Confirm a Direct Hit applies its full raw damage unchanged (`effectiveDamage == rawDamage`).

## Test 2: Known values match the formula by hand

Using Wolf Class (`armor = 50`) as the target, fire MRK-1 Torpedo's Direct Hit (raw `800`). By hand: `800 * (1 - 50*0.0015) = 800 * 0.925 = 740`. Verify the logged/applied damage is exactly `740`, not `800` and not some other value.

## Test 3: Higher armor reduces more

Using Athena Class (`armor = 150`) as the target, fire the same weapon/tier. By hand: `800 * (1 - 150*0.0015) = 800 * 0.775 = 620`. Verify the applied damage is `620`, visibly less than Test 2's `740` for the identical raw roll.

## Test 4: Minimum 1 damage floor — defensive check, not a live-data test

With armor capped at 200 (30% reduction), no combination of real ship armor and real weapon raw damage can round to `0` — the worst case (`rawDamage * 0.7`) always rounds to at least `1` for any `rawDamage ≥ 1`. So this cannot be demonstrated against actual game data as-is.

Instead: verify by code inspection that the floor is implemented as an explicit clamp (e.g. `Mathf.Max(1, roundedValue)` gated on `rawDamage > 0`), and confirm with a one-off manual call to the helper function (e.g. from a temporary test script or the Unity console) using an artificial value that would round to `0` without the clamp — for example `helper(rawDamage: 1, armor: 2000)`. Report the helper's output for that artificial call, then remove the temporary test call. Do not leave test-only code in the final implementation.

## Test 5: Miss stays zero

Confirm a Miss result (`rawDamage == 0`) still applies `0` damage and does not get pushed to `1` by the floor rule. The floor only applies to non-zero rolls.

## Test 6: Destruction still works correctly

Confirm a ship still reaches `currentHealth <= 0` and gets removed/destroyed correctly using the new reduced damage numbers, not the old raw numbers. Run at least one full kill to verify.

## Test 7: AI and human paths both apply the formula

Fire one attack from the human `TestShipController` path and one from `AIController`. Confirm both show armor-reduced damage in the log, since both route through the same `ResolveAttack`.

---

# 8. Documentation

Update `docs/DEVELOPER_GUIDE.md` only where it currently says armor is not applied to damage. Correct that section to describe the formula (`effectiveDamage = round(rawDamage * (1 - armor * 0.0015))`, i.e. 0.15% reduction per armor point) and where it lives. Do not rewrite unrelated combat documentation.

Update `docs/AGENTS.md` only if this changes an architectural rule documented there. Do not rewrite unrelated instructions. Do not remove or modify project credits.

---

# 9. Before finishing

```text
[ ] docs/AGENTS.md read
[ ] docs/DEVELOPER_GUIDE.md read
[ ] Current ResolveAttack damage line inspected
[ ] Formula implemented exactly as specified: rawDamage * (1 - armor * 0.0015)
[ ] Miss (0 damage) is not affected by the floor
[ ] Non-zero results never round below 1 (explicit clamp, not incidental)
[ ] Combat log shows both raw and reduced damage
[ ] Test 1 (zero armor) passed in Unity
[ ] Test 2 (Wolf, hand-checked value = 740) passed in Unity
[ ] Test 3 (Athena, hand-checked value = 620) passed in Unity
[ ] Test 4 (floor clamp verified by code + artificial-value call, not live data) confirmed
[ ] Test 5 (Miss unaffected) passed in Unity
[ ] Test 6 (destruction still correct) passed in Unity
[ ] Test 7 (human and AI paths both apply it) passed in Unity
[ ] No unrelated code changed
[ ] DEVELOPER_GUIDE.md updated
[ ] AGENTS.md updated only if necessary
```

---

# 10. Final response format

Report:

## Changed files

List every file touched.

## Formula implementation

Show the exact method/expression added and confirm it matches Section 2.

## Verification

Report actual Unity Play mode results and hand-checked numbers for Tests 1 through 7, not code review claims. For Test 4, report the artificial-value output and confirm the temporary test call was removed afterward.

## Unrelated issues found

List anything noticed but not fixed, per Section 5.

## Documentation

Report what changed in `DEVELOPER_GUIDE.md` and `AGENTS.md`, if anything.

Stop after this task is complete. Do not implement defenses, status effects, or any other combat feature beyond this formula.