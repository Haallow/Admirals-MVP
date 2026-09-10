# Milestone 4 — Ship Data Model States + One Fully-Statted Ship

Goal: build the data shapes identified in the Combat/Turn/Fog planning doc (domain state, weapon/defense profiles, charge tracking, vision layers), then prove them by fully defining **one real ship** with its actual card numbers. No combat resolution, no rolling dice yet, this milestone is purely "the data exists and is correct," verified by reading it back out.

Recommended ship: **[Wolf Class] Fast Attack Submarine**. It's the best test case because it exercises every new concept at once: domain toggling (Submerge), three different weapons, three different defenses (two with saving throws, one that's a pure movement counter), and a vision layer that's conditional on domain state ("Absolute Vision, Only When Surfaced").

---

## What "done" looks like

- All new types from the planning doc compile: `DomainType`, `RollTier`, `WeaponProfile`, `DefenseProfile`, `VisionLayer`, `ChargeState`.
- `ShipInstance` has a `currentDomain` field, plus weapon/defense/vision lists and matching charge-tracking lists.
- One ship instance exists with the Wolf Class's real numbers: 500 HP, 50 Armor, 1x2 footprint, 3 weapons, 3 defenses, 2 vision layers.
- At `Start()`, the ship's full stat block prints to Console, health, armor, domain, and every weapon/defense with its remaining ammo/uses, so you can visually confirm it matches the card.
- Pressing a test key toggles `currentDomain` between Surface and Sub-Surface, and the logged output changes accordingly (in particular, whether the "Only When Surfaced" vision layer would currently apply).

Nothing fires a weapon, nothing rolls dice, nothing resolves an attack yet. That's later milestones. This one is entirely about correctly holding and reporting data.

---

## Step-by-step build order

### Step 1 — `DomainType` enum
`Assets/Scripts/Combat/DomainType.cs`
- Values: `Surface`, `SubSurface`, `Both`
- `Both` is used on weapon/vision definitions that apply either way (e.g. Spear Anti-Ship Missile is Surface-only, but some sensors detect `[SF,SS]`, both at once).

### Step 2 — `RollTier` struct
`Assets/Scripts/Combat/RollTier.cs`
- Fields: `int minRoll`, `int maxRoll`, `string outcomeLabel`, `int damage`
- One instance per row on the card, e.g. Spear becomes 3 `RollTier`s: Miss (1-3, 0 dmg), Direct Hit (4-17, 500 dmg), Catastrophic Hit (18-20, 750 dmg).

### Step 3 — `WeaponProfile` class
`Assets/Scripts/Combat/WeaponProfile.cs`
- Fields: `string id`, `int? ammo` (null = infinite), `DomainType targetDomain`, `List<RollTier> rollTiers`
- One instance per weapon on the card. `ammo` is the *starting* value, actual remaining tracked separately (Step 6).

### Step 4 — `DefenseProfile` class
`Assets/Scripts/Combat/DefenseProfile.cs`
- Fields: `string id`, `int? uses`, `DomainType validAgainst`, `List<RollTier> savingThrowTiers`, `string sideEffectId` (nullable)
- Saving throw tiers only need `outcomeLabel` (Fail/Hit Avoided), damage field unused, leave it 0.
- Crash Dive gets a `sideEffectId` like `"BecomeSubSurfaceAndSkipNextMove"`, a string id for now, not real logic. The logic that reads this id doesn't need to exist yet.

### Step 5 — `VisionLayer` class + its two small enums
`Assets/Scripts/Combat/VisionLayer.cs`
- Enums: `ShapeType { Halo, Cone }`, `VisionType { Sensor, Absolute }`
- Fields: `ShapeType shape`, `int range`, `DomainType detects`, `VisionType visionType`, `bool isPassive`, `bool onlyWhileSurfaced` (new field, specifically for cases like the Wolf's Default Absolute Vision layer)

### Step 6 — `ChargeState` class
`Assets/Scripts/Combat/ChargeState.cs`
- Fields: `string profileId`, `int remaining`, `int turnsUntilRecharge`
- This is what actually changes during a game; `WeaponProfile`/`DefenseProfile` never change once defined.

### Step 7 — Update `ShipInstance`
Add:
- `DomainType currentDomain` (default `Surface`)
- `List<WeaponProfile> weapons`
- `List<DefenseProfile> defenses`
- `List<VisionLayer> visionLayers`
- `List<ChargeState> weaponCharges`
- `List<ChargeState> defenseCharges`

Add a helper method, `InitializeCharges()`, called once when the ship is set up: loops `weapons`/`defenses`, creates one `ChargeState` per profile using its starting `ammo`/`uses` value (or a sentinel like `-1` for infinite, since `ChargeState.remaining` is a plain `int`, not nullable).

### Step 8 — Build the actual Wolf Class ship
This can live directly in `GridManager.Start()` for now (same hardcoded-test-data approach as every milestone so far), or in a small dedicated method like `BuildWolfClassTestShip()`. Populate every field using the real card numbers:
- Health 500, Armor 50, footprint 1x2
- Weapons: `MRK-1 Torpedo` (ammo 4, SubSurface, tiers D1-10 Miss / D11-20 Direct Hit 800 dmg), `Spear Anti-Ship Missile` (ammo 2, Surface, tiers as above), `Hippocampus Torpedo` (ammo 3, tiers D1-6 Miss / D7-16 Direct Hit 1000 / D17-20 Catastrophic 1500)
- Defenses: `Crash Dive` (infinite, Surface, D1-10 Fail / D11-20 Hit Avoided, sideEffect set), `Acoustic Decoys` (ammo 2, SubSurface, D1-10 Fail / D11-20 Hit Avoided), `Deep Dive` (infinite, SubSurface, D1-15 Fail / D16-20 Hit Avoided)
- Vision: Passive Sonar (Halo, range 2, Both, Sensor, passive=true), Default (Halo, range 1, Both, Absolute, passive=true, onlyWhileSurfaced=true), Active Search Sonar (Cone, range 4, Both, Sensor, passive=false)

### Step 9 — Log everything, and add a domain-toggle test key
- On `Start()`, log health/armor/domain, then loop weapons and defenses logging each one's id and current `ChargeState.remaining`.
- Add one keybind (reuse `TestShipController` or a new small script) that flips `currentDomain` between Surface/SubSurface and re-logs the stat block, so you can visually confirm the "Only When Surfaced" vision layer's relevance changes with it (you don't need real Fog logic yet, just log something like `"Default Absolute Vision active: {currentDomain == Surface}"`).

---

## Suggested file list

```
Assets/Scripts/Combat/DomainType.cs
Assets/Scripts/Combat/RollTier.cs
Assets/Scripts/Combat/WeaponProfile.cs
Assets/Scripts/Combat/DefenseProfile.cs
Assets/Scripts/Combat/VisionLayer.cs
Assets/Scripts/Combat/ChargeState.cs
```
Plus edits to:
```
Assets/Scripts/Ships/ShipInstance.cs
Assets/Scripts/Grid/GridManager.cs
```

## Do NOT build yet
- No dice rolling, no `ResolveAttack()`, no Fog of War grid, no mines. Those are separate milestones, this one is data-only.
- No `ShipDefinition` ScriptableObject still, same reasoning as before, hardcode the Wolf Class stats directly for now. Converting to data-driven assets is a later, mechanical step once you have more than one ship's worth of hardcoded data to justify it.
- No UI, this is Console-log verification only.

## Next milestone preview
Once one ship's full data is verified correct, Milestone 5 would be the first real Fog of War pass: a per-player visibility grid, resolving one `VisionLayer` (the Wolf's Passive Sonar Halo) against the existing obstruction ship, marking its tiles based on distance + domain match.

---

## Implementation Reference — What Was Actually Built

This section is appended after completion. It maps every file, class, field, method, and test key added during implementation so future milestones have a precise starting-point inventory.

---

### File structure added

```
Assets/
  Scripts/
    Combat/                          ← new folder
      DomainType.cs
      RollTier.cs
      WeaponProfile.cs
      DefenseProfile.cs
      VisionLayer.cs
      ChargeState.cs
    Ships/
      ShipInstance.cs                ← updated
    Grid/
      GridManager.cs                 ← updated
      TestShipController.cs          ← updated
```

---

### New types — `Assets/Scripts/Combat/`

#### `DomainType` — enum
| Value | Meaning |
|---|---|
| `Surface` | Ship / weapon / sensor operates on the surface |
| `SubSurface` | Ship / weapon / sensor operates below surface |
| `Both` | Domain-agnostic — valid in either state. **Not a valid runtime ship state** (only used on profile definitions) |

---

#### `RollTier` — struct (`[Serializable]`)
One row in a weapon or saving-throw roll table. Shared between `WeaponProfile` and `DefenseProfile`.

| Field | Type | Notes |
|---|---|---|
| `minRoll` | `int` | Inclusive lower bound on the d20 roll |
| `maxRoll` | `int` | Inclusive upper bound on the d20 roll |
| `outcomeLabel` | `string` | e.g. `"Miss"`, `"Direct Hit"`, `"Catastrophic Hit"`, `"Hit Avoided"`, `"Fail"` |
| `damage` | `int` | Damage dealt on this outcome. `0` for misses and all saving-throw rows |

Constructor: `RollTier(int minRoll, int maxRoll, string outcomeLabel, int damage = 0)`

---

#### `WeaponProfile` — class (`[Serializable]`)
Immutable definition of one weapon slot. Never mutated at runtime — ammo is tracked in `ChargeState`.

| Field | Type | Notes |
|---|---|---|
| `id` | `string` | Unique name matching the ship card |
| `ammo` | `int?` | Starting ammo count. `null` = infinite |
| `targetDomain` | `DomainType` | Which domain targets this weapon can engage |
| `rollTiers` | `List<RollTier>` | Ordered roll table, looked up by 1d20 result |

Constructor: `WeaponProfile(string id, int? ammo, DomainType targetDomain, List<RollTier> rollTiers)`

---

#### `DefenseProfile` — class (`[Serializable]`)
Immutable definition of one defensive scheme slot. Never mutated at runtime — uses tracked in `ChargeState`.

| Field | Type | Notes |
|---|---|---|
| `id` | `string` | Unique name matching the ship card |
| `uses` | `int?` | Starting use count. `null` = infinite |
| `validAgainst` | `DomainType` | Which incoming weapon domain this counters |
| `savingThrowTiers` | `List<RollTier>` | Roll table — `damage` field unused (left `0`) |
| `sideEffectId` | `string` | Nullable. String id for a side-effect e.g. `"BecomeSubSurfaceAndSkipNextMove"`. Logic to consume this id deferred to Milestone 5+ |

Constructor: `DefenseProfile(string id, int? uses, DomainType validAgainst, List<RollTier> savingThrowTiers, string sideEffectId = null)`

---

#### `VisionLayer` — class (`[Serializable]`)
Definition of one scan/vision layer. A ship can hold multiple. Activation logic deferred to the Fog of War milestone.

**Companion enums (defined in the same file):**

| Enum | Values |
|---|---|
| `ShapeType` | `Halo` (radial ring), `Cone` (directional forward arc) |
| `VisionType` | `Sensor` (marks tile — something is there), `Absolute` (full reveal — tile contents known) |

| Field | Type | Notes |
|---|---|---|
| `id` | `string` | Unique name |
| `shape` | `ShapeType` | Geometric pattern |
| `range` | `int` | Radius for Halo; forward distance for Cone |
| `detects` | `DomainType` | Which domain targets this layer sees |
| `visionType` | `VisionType` | How much info is revealed |
| `isPassive` | `bool` | `true` = always on; `false` = consumes the Search phase action |
| `onlyWhileSurfaced` | `bool` | `true` = layer is irrelevant while submerged. Real enforcement deferred to Milestone 5 |

Constructor: `VisionLayer(string id, ShapeType shape, int range, DomainType detects, VisionType visionType, bool isPassive, bool onlyWhileSurfaced = false)`

---

#### `ChargeState` — class (`[Serializable]`)
Mutable runtime state for one weapon or defense slot. One instance per profile, created by `ShipInstance.InitializeCharges()`.

| Field | Type | Notes |
|---|---|---|
| `profileId` | `string` | Matches the `id` of the parent `WeaponProfile` or `DefenseProfile` |
| `remaining` | `int` | Current ammo/uses left. `-1` = infinite (sentinel for a `null` starting value) |
| `turnsUntilRecharge` | `int` | `0` = ready now. Recharge logic not yet implemented — field stored for Milestone 6+ |

Constructor: `ChargeState(string profileId, int remaining)`

Property: `bool IsReady` → `turnsUntilRecharge == 0 && remaining != 0`

---

### Updated — `ShipInstance` (`Assets/Scripts/Ships/ShipInstance.cs`)

New fields added this milestone:

| Field | Type | Default | Notes |
|---|---|---|---|
| `maxHealth` | `int` | `100` | Set at ship creation |
| `currentHealth` | `int` | — | Set by `InitializeCharges()` to equal `maxHealth` |
| `armor` | `int` | `0` | Flat damage reduction, applied during combat resolution (future milestone) |
| `currentDomain` | `DomainType` | `Surface` | Mutable runtime state. Toggle between `Surface` / `SubSurface` for subs |
| `weapons` | `List<WeaponProfile>` | empty | Immutable definitions assigned at setup |
| `defenses` | `List<DefenseProfile>` | empty | Immutable definitions assigned at setup |
| `visionLayers` | `List<VisionLayer>` | empty | Immutable definitions assigned at setup |
| `weaponCharges` | `List<ChargeState>` | empty | Mutable runtime tracking, one per weapon |
| `defenseCharges` | `List<ChargeState>` | empty | Mutable runtime tracking, one per defense |

Also: `footprintOffsets` default changed from `1x3` to `1x2` to match the Wolf Class card.

New methods added this milestone:

**`InitializeCharges()`**
- Sets `currentHealth = maxHealth`.
- Clears and repopulates `weaponCharges` and `defenseCharges` — one `ChargeState` per profile.
- Maps `null` ammo/uses → `remaining = -1` (infinite sentinel).
- Call once after all profiles are assigned, before placing the ship.

**`LogStatBlock(string label)`**
- Prints the ship's full stat block to Console.
- Logs: owner, domain, HP, armor, footprint size, move range.
- Per weapon: id, target domain, ammo remaining, each roll tier.
- Per defense: id, valid-against domain, uses remaining, side effect id, each saving throw tier.
- Per vision layer: id, shape, range, detects, vision type, passive flag, and if `onlyWhileSurfaced`, the current active/inactive status based on `currentDomain`.

---

### Updated — `GridManager` (`Assets/Scripts/Grid/GridManager.cs`)

New private method added this milestone:

**`BuildWolfClassTestShip(ShipInstance ship)`**
Populates the passed `ShipInstance` with every Wolf Class card number. Called in `Start()` before placing.

| Stat | Value |
|---|---|
| HP | 500 |
| Armor | 50 |
| Movement range | 3 |
| Footprint | 1×2 (`(0,0)`, `(1,0)`) |
| Starting domain | `Surface` |

Weapons populated:

| id | Ammo | Domain | Roll table |
|---|---|---|---|
| MRK-1 Torpedo | 4 | SubSurface | D1-10 Miss / D11-20 Direct Hit (800 dmg) |
| Spear Anti-Ship Missile | 2 | Surface | D1-3 Miss / D4-17 Direct Hit (500) / D18-20 Catastrophic Hit (750) |
| Hippocampus Torpedo | 3 | Both | D1-6 Miss / D7-16 Direct Hit (1000) / D17-20 Catastrophic Hit (1500) |

Defenses populated:

| id | Uses | Against | Saving throw | Side effect |
|---|---|---|---|---|
| Crash Dive | ∞ | Surface | D1-10 Fail / D11-20 Hit Avoided | `BecomeSubSurfaceAndSkipNextMove` |
| Acoustic Decoys | 2 | SubSurface | D1-10 Fail / D11-20 Hit Avoided | — |
| Deep Dive | ∞ | SubSurface | D1-15 Fail / D16-20 Hit Avoided | — |

Vision layers populated:

| id | Shape | Range | Detects | Type | Passive | onlyWhileSurfaced |
|---|---|---|---|---|---|---|
| Passive Sonar | Halo | 2 | Both | Sensor | true | false |
| Default Absolute Vision | Halo | 1 | Both | Absolute | true | **true** |
| Active Search Sonar | Cone | 4 | Both | Sensor | false | false |

---

### Updated — `TestShipController` (`Assets/Scripts/Grid/TestShipController.cs`)

#### Keyboard shortcuts — full reference

All shortcuts active during Play Mode.

| Key | Phase required | Ship required | Action |
|---|---|---|---|
| `Space` | Any | — | Advance turn phase (Move → Search → Battle → Move, switches player on Battle→Move) |
| `D` | Any | `TestShip` (PlayerA) | Toggle `currentDomain` Surface ↔ SubSurface on the Wolf Class, then re-log full stat block to Console |
| `↑` Arrow | Move | PlayerA's turn | Move ship one tile north |
| `↓` Arrow | Move | PlayerA's turn | Move ship one tile south |
| `←` Arrow | Move | PlayerA's turn | Move ship one tile west |
| `→` Arrow | Move | PlayerA's turn | Move ship one tile east |
| `Q` | Move | PlayerA's turn | Rotate ship 90° counter-clockwise |
| `E` | Move | PlayerA's turn | Rotate ship 90° clockwise |

Movement and rotation are gated: they silently do nothing if the current phase is not `Move` or if it is not PlayerA's turn. `D` and `Space` work regardless of phase.
