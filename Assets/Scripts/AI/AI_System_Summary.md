# AI Fleet Commander System - Complete Implementation

**Project:** Admirals Fleet Commander  
**Status:** All 5 Stages Complete  
**Last Updated:** Stage 5 Completion

---

## System Overview

The AI controls PlayerB's fleet through a hierarchical decision-making architecture with 5 integrated stages:

1. **Fleet Commander** (strategy) ? picks stance, builds contacts
2. **Formation Terms** (coordination) ? cohesion, fragility, scout roles
3. **Directional Bias** (movement) ? stance-driven push/pull
4. **Target Prioritization** (battle) ? focus fire, threat ranking
5. **Tuning & Polish** (refinement) ? weight balance, documentation

---

## Quick Reference

### Files Modified/Created

**Core AI (Assets/Scripts/AI/):**
- `AiController.cs` - Orchestrator with FleetCommander integration + debug hotkeys
- `AIMovementPlanner1.cs` - Formation + directional bias + stance-aware fallback
- `AIAttackPlanner.cs` - Stance-aware focus fire
- `AIActiveScanPlanner.cs` - Scout-priority scanner selection
- `AiContact.cs` - Fog-honest contact clustering
- `AIEnemyMemory.cs`, `AITurnContext.cs`, `AIScoring.cs` - (unchanged support)

**Fleet Strategy (Assets/Scripts/Fleet/):**
- `FleetCommander.cs` - Stance chooser + target prioritization
- `FleetSnapshot.cs` - Per-turn fleet data holder
- `FleetStance.cs` - Enum (Hunt, Advance, Hold, DefensiveFallback, Retreat)
- `FleetRoles.cs` - Class-to-role assignment (Scout/Anchor/Striker)

**Documentation (Assets/Scripts/AI/):**
- `AI_Developer_Guide.md` - Complete system reference (24 KB)
- `AI_Debug_Controls.md` - Debug hotkeys & testing workflows
- `AI_Weight_Analysis.md` - Weight hierarchy & tuning guide
- `AI_Testing_Checklist.md` - Comprehensive test scenarios

### Key Features

**Stance System:**
- Hunt ? Search for enemies, advance toward predicted zone
- Advance ? Aggressive push, focus fire, loose formation
- Hold ? Defensive position, spread fire, medium formation
- DefensiveFallback ? Pull back, tight formation, conservative
- Retreat ? Flee toward home with spread, still takes guaranteed kills

**Formation Behavior:**
- Scouts (Wolf) lead, Anchors (Athena) hold center, Strikers drop back
- Fragile/wounded ships automatically pull to rear
- Formation tightness varies by stance (0× in Hunt, 1.0× in Retreat)

**Battle Coordination:**
- Targets prioritized by Threat × Fragility
- Advance stance: multiple ships pile on priority target
- Other stances: spread fire across multiple targets

**Scanner Selection:**
- Scout-priority (Wolf first)
- Surface-capable cones only (excludes Athena's sub-surface sonar)
- One scan per Search phase (GridManager enforced)

---

## Debug Hotkeys

Active during PlayerB's turn in Play mode:

| Key | Action |
|-----|--------|
| **1** | Force Hunt stance |
| **2** | Force Advance stance |
| **3** | Force Hold stance |
| **4** | Force DefensiveFallback stance |
| **5** | Force Retreat stance |
| **0** | Release override (auto stance selection) |
| **-** | Damage AI fleet to 33% HP |
| **=** | Heal AI fleet to full HP |

---

## Weight Summary (Quick Tuning)

**Attack/Danger Balance:**
- KillBonus = 50 (dominates)
- MaxDamageScore = 40
- DangerWeight = 35

**Formation:**
- FragilityPushbackWeight = 12
- CohesionBaseWeight = 8
- ScoutForwardWeight = 6

**Directional:**
- RetreatDirectionalWeight = 18
- FallbackDirectionalWeight = 10
- AdvanceDirectionalWeight = 10

**Focus Fire:**
- Advance claimed penalty = 75
- Other stances penalty = 300

**Tuning Rule:** All weights scaled as fractions of maxHealth, so they work across all ship classes.

---

## Testing Quick Start

1. **Enter Play mode**
2. **Press 5** (force Retreat) ? Watch ships flee toward home edge with spread
3. **Press 2** (force Advance) ? Watch ships push toward contacts, focus fire
4. **Press 3** (force Hold) ? Watch ships maintain position defensively
5. **Press 0** (release) ? Let AI pick stance based on material score
6. **Watch console** for `[AI][Fleet]` stance logs and `[AI][STANCE_LABEL]` move intent

See `AI_Testing_Checklist.md` for full test scenarios.

---

## Known Good Configurations

**Current (Balanced):**
- Works for mixed 2-3 ship fleets
- Advance aggressive but not suicidal
- Retreat actually retreats
- Focus fire without total pile-on

**More Aggressive:**
```
MaxDamageScore = 50
DangerWeight = 25
AdvanceDirectionalWeight = 15
```

**More Defensive:**
```
DangerWeight = 45
FragilityPushbackWeight = 16
CohesionBaseWeight = 12
```

---

## Next Steps (Optional Future Work)

### Terrain Awareness
- Use cover, avoid costly tiles, exploit chokepoints
- Add terrain scoring term (~5-10 weight)

### Staging Phase Actions
- Mines, planes, repair ships
- Requires new planner class

### Parallel Movement Planning
- All ships decide simultaneously instead of sequentially
- Requires movement conflict resolution

### Full Fog Honesty
- Make per-ship planners work on AIContacts instead of true enemy ShipInstances
- Currently only FleetCommander is fully fog-honest

### Learning/Adaptation
- Track which tactics work against human players
- Adjust weights dynamically mid-match

---

## Maintenance Notes

**Before Modifying AI Code:**
1. Read the affected section in `AI_Developer_Guide.md`
2. Check `AI_Weight_Analysis.md` for weight interactions
3. Run `AI_Testing_Checklist.md` after changes
4. Update documentation if adding features

**Weight Changes:**
- Always test with at least 3 different fleet compositions
- Verify stance transitions still make sense (check hysteresis logs)
- Confirm formation terms don't overwhelm attack scoring

**Adding New Ship Classes:**
1. Add vision layers in `ShipData.cs`
2. Add role entry in `FleetRoles.cs` (Scout/Anchor/Striker + fragility)
3. Test scanner selection (should prioritize Scouts, exclude sub-surface-only cones)
4. Test formation (fragile ships drop back, scouts advance)

---

## Contact & Support

**Implementation:** Kiro AI Assistant (2024)  
**Maintained By:** [Your Team]  
**AI Folder Owner:** Only edit AI/ and Fleet/ folders if you own them

Questions? See `AI_Developer_Guide.md` first, then ping the AI folder owner.

---

**All 5 Stages Complete ?**

