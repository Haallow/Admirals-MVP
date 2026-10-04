# AI System Testing Checklist

**Complete System Test - All 5 Stages**

Use this checklist to verify all AI features are working after implementation or after making changes.

## Prerequisites
- Unity Play mode
- PlayerB set as AI-controlled
- Debug hotkeys active (see AI_Debug_Controls.md)

---

## Stage 1: Fleet Commander & Stance Selection

### Test: Hunt Stance
- [ ] Start game, AI has no contacts
- [ ] Console shows `[AI][Fleet] stance=Hunt`
- [ ] Ships move toward predicted enemy zone (opposite edge from spawn)
- [ ] Console label: `[AI][HUNTING]`

### Test: Advance Stance
- [ ] Press **=** (heal AI), **2** (force Advance), or let AI win decisively
- [ ] Console shows `[AI][Fleet] stance=Advance`
- [ ] Ships push toward known contacts aggressively
- [ ] Console label: `[AI][ADVANCING]`

### Test: Hold Stance
- [ ] Press **3** (force Hold)
- [ ] Console shows `[AI][Fleet] stance=Hold`
- [ ] Ships maintain position, minimal forward movement
- [ ] Console label: `[AI][HOLDING]`

### Test: DefensiveFallback
- [ ] Press **4** (force DefensiveFallback)
- [ ] Console shows `[AI][Fleet] stance=DefensiveFallback`
- [ ] Ships pull back toward starting edge
- [ ] Tighter formation than Hold
- [ ] Console label: `[AI][FALLING BACK]`

### Test: Retreat
- [ ] Press **-** (damage fleet) then **5** (force Retreat)
- [ ] Console shows `[AI][Fleet] stance=Retreat`
- [ ] Ships flee toward home edge with spread (not clustering on one pixel)
- [ ] Still shoots if guaranteed kill exists
- [ ] Console label: `[AI][RETREATING]`

### Test: Stance Transitions
- [ ] Start in Hunt, create contact ? stance changes to Hold or Advance
- [ ] Console shows hysteresis: `(wanted X, held by dwell N/2)` if switching too soon
- [ ] Dwell time: stance holds for minimum 2 turns before switching (except Retreat/Hunt exit)

---

## Stage 2: Formation & Protection

### Test: Cohesion Pull
- [ ] Force **Hunt** stance (0× cohesion) ? ships spread out freely
- [ ] Force **Hold** stance (0.7× cohesion) ? ships loosely cluster
- [ ] Force **Retreat** stance (1.0× cohesion) ? ships tightly cluster
- [ ] Console roster shows role assignments (Scout/Anchor/Striker)

### Test: Fragility Pushback
- [ ] Press **-** to damage Wolf below 50% HP
- [ ] Create contact ahead of fleet
- [ ] Wolf should drop back behind Athena (fragility > 0.6)
- [ ] Undamaged ships advance normally

### Test: Scout Forward
- [ ] Wolf (Scout) should lead the formation when contacts exist
- [ ] Wolf advances ahead of Athena/SwordFish in Advance stance
- [ ] Console roster shows `WolfClass=Scout frag=0.70(hp X.XX)`

### Test: Scanner Selection
- [ ] During Search phase, console shows `[AI][Scan] Selected scanner: WolfClass (role=Scout...)`
- [ ] Wolf scans first if available (Scout priority)
- [ ] If Wolf dead, next surface-capable ship scans (not Athena's sub-surface cone)
- [ ] Only one scan per Search phase

---

## Stage 3: Stance-Driven Movement

### Test: Directional Bias - Advance
- [ ] Force **Advance** stance, create contact
- [ ] Ships move toward contact even without guaranteed shots lined up
- [ ] Console shows `advancing position` when repositioning

### Test: Directional Bias - Retreat
- [ ] Force **Retreat** stance with contact visible
- [ ] Ships move away from contact toward home edge
- [ ] Ships spread ±4 tiles around home centroid (not clustering)
- [ ] Still takes guaranteed kills if safe

### Test: Directional Bias - Hold
- [ ] Force **Hold** stance
- [ ] Ships stay near fleet centroid
- [ ] Minimal forward movement even with contacts visible
- [ ] Console shows `holding position`

### Test: Fallback Behavior (No Attacks Available)
- [ ] **Hunt** + no contacts ? advance toward enemy zone
- [ ] **Advance** + no attacks ? close on known enemies or push to center
- [ ] **Hold** + no attacks ? stay near fleet centroid
- [ ] **DefensiveFallback** + no attacks ? pull back toward home

---

## Stage 4: Coordinated Battle

### Test: Target Prioritization
- [ ] Two enemies visible: high-threat low-HP and low-threat high-HP
- [ ] Console shows ships targeting high-threat enemy first
- [ ] Priority = Threat × (1 + Fragility)
- [ ] In-range enemies prioritized over out-of-range (1.5× threat multiplier)

### Test: Focus Fire - Advance Stance
- [ ] Force **Advance** stance
- [ ] Multiple AI ships have same enemy in range
- [ ] All ships attack the same priority target (claimed penalty only 75)
- [ ] Console shows multiple ships firing at same target

### Test: Spread Fire - Other Stances
- [ ] Force **Hold** or **DefensiveFallback**
- [ ] Multiple AI ships have multiple enemies in range
- [ ] Ships spread fire across different targets (claimed penalty 300)
- [ ] Each ship picks a different target if options are comparable

### Test: Guaranteed Kill Priority
- [ ] One enemy at 50 HP, another at 2000 HP
- [ ] AI ship has weapon dealing 60 expected damage
- [ ] Ship targets the 50 HP enemy (guaranteed kill overrides prioritization)
- [ ] Console shows `expected value X.X`: hit should be lethal

---

## Stage 5: System Integration

### Test: Complete Turn Sequence
Watch one full PlayerB turn (Move ? Search ? Battle):

**Move Phase:**
- [ ] Console: `[AI][Fleet] stance=X ... score=Y.YY ownAlive=N/M ...`
- [ ] Console: `[AI][Fleet] roster: WolfClass=Scout ...`
- [ ] Console: `[AI][STANCE_LABEL] ShipType moved to (x,y) (intent)`
- [ ] Ships move based on stance + formation + directional bias

**Search Phase:**
- [ ] Console: `[AI][Scan] Selected scanner: ShipType (role=X ...)`
- [ ] Console: `[AI][Scan] ShipType scanned facing (dx,dy)`
- [ ] Only one ship scans, Scout priority

**Battle Phase:**
- [ ] Console: `[AI][Fleet] Battle refresh: stance=X contacts=N ...`
- [ ] Console: `[AI] ShipType fired WeaponID at TargetType ...`
- [ ] Ships attack prioritized targets based on stance

### Test: Debug Hotkeys
- [ ] **1-5**: Force stances, console confirms with `[AI][Debug] Forced stance: X`
- [ ] **0**: Release override, console confirms `Released stance override (auto)`
- [ ] **-**: Damage fleet to 33% HP
- [ ] **=**: Heal fleet to full HP
- [ ] Stance labels appear correctly: `[HUNTING]`, `[ADVANCING]`, `[HOLDING]`, `[FALLING BACK]`, `[RETREATING]`, `[FLEE]`

---

## Edge Cases & Known Limitations

### Expected Behavior (Not Bugs)

- [ ] Hunt stance with zero contacts ? ships may cluster at map center if no enemies ever scanned (non-issue in real play)
- [ ] Retreat with guaranteed kill ? ship still shoots despite retreating (correct: kills are valuable)
- [ ] Hold stance repositions slightly ? Hold maintains position relative to centroid, which moves as ships move
- [ ] Focus fire in Advance ? all ships may pile on one target (intended behavior)

### Known Limitations

- [ ] Sequential movement only (ships move one at a time, later ships see earlier positions)
- [ ] Per-ship planners read true enemy stats via context.KnownEnemies (FleetCommander is fog-honest, planners are not yet)
- [ ] No terrain awareness (cover, chokepoints, Costly tiles treated equally)
- [ ] No staging-phase actions (mines, planes, repair excluded by design)
- [ ] Contact class deduction from footprint not guaranteed (partial scans may misidentify size)

---

## Performance Check

- [ ] No excessive log spam (< 20 lines per ship per turn)
- [ ] No frame drops during AI turn
- [ ] Stance changes happen smoothly (hysteresis prevents rapid flip-flopping)
- [ ] Ships don't freeze or oscillate (move to same tile repeatedly)

---

## Regression Tests (After Changes)

If you modify AI weights or logic, re-run these key scenarios:

1. **Balanced Fight (score ~0.8-1.2)** ? Should Hold or Advance
2. **AI Winning (score > 1.3)** ? Should Advance aggressively
3. **AI Losing (score < 0.5)** ? Should DefensiveFallback or Retreat
4. **AI Critical (HP < 30%)** ? Should Retreat regardless of score
5. **No Contacts** ? Should Hunt or advance toward enemy zone

---

## Sign-Off

Test completed by: _________________  
Date: _________________  
Build/Commit: _________________  

All tests passed: ? Yes ? No (see notes below)

Notes:
_________________________________________________________________
_________________________________________________________________
_________________________________________________________________

