# AI Stance Testing - Debug Controls

**Location:** Active when PlayerB (AI) is the current player during Play mode

## Stance Override Hotkeys

Press these keys to force the AI into specific stances for testing:

| Key | Stance | Expected Behavior |
|-----|--------|-------------------|
| **1** | Hunt | Search for enemies, advance toward predicted enemy zone, loose formation |
| **2** | Advance | Push toward contacts aggressively, scouts lead, loose formation |
| **3** | Hold | Maintain position, defensive, medium-tight formation |
| **4** | DefensiveFallback | Pull back from contacts, tight formation, conservative |
| **5** | Retreat | Flee toward home edge (with spread), very tight formation, still takes guaranteed kills |
| **0** | Auto (release) | Return to normal stance selection based on material score/HP |

## Fleet HP Manipulation

| Key | Effect |
|-----|--------|
| **- (Minus)** | Damage all AI ships to 33% HP (triggers Retreat conditions) |
| **= (Equals)** | Heal all AI ships to full HP |

## Testing Workflows

### Test Retreat Behavior
1. Start Play mode
2. Press **-** (minus) to damage AI fleet to 33% HP
3. Press **5** to force Retreat stance
4. Advance PlayerA ships to create contacts
5. **Watch for:**
   - AI ships move away from your ships
   - AI ships spread out near their starting edge (not clustering on one pixel)
   - AI still shoots if a guaranteed kill exists

### Test Advance Behavior
1. Start Play mode  
2. Press **=** (equals) to ensure AI is full HP
3. Press **2** to force Advance stance
4. Place some PlayerA ships in scan range
5. **Watch for:**
   - AI ships push toward your ships
   - Wolf (Scout) leads the charge
   - Fragile ships (SwordFish if present) stay slightly back

### Test Hold Behavior
1. Start Play mode
2. Press **3** to force Hold stance
3. Move your ships toward AI
4. **Watch for:**
   - AI maintains position (doesn't advance aggressively)
   - AI engages when you come into range
   - Defensive, medium-tight formation

### Test DefensiveFallback
1. Start Play mode
2. Press **4** to force DefensiveFallback
3. Create contacts by scanning or moving near AI
4. **Watch for:**
   - AI slowly pulls back from contacts
   - Tighter formation than Hold
   - Less urgent than Retreat

### Compare Natural Stance Selection
1. Start Play mode
2. Press **0** to ensure auto mode
3. Watch console logs: `[AI][Fleet] stance=...`
4. Kill enemy ships or damage AI ships to see stance changes
5. **Material score thresholds:**
   - < 0.35: Retreat
   - < 0.60: DefensiveFallback  
   - 0.60-0.89: Hold
   - = 0.90: Advance
   - No contacts: Hunt

## Console Log Tags

- `[AI][Debug]` - Debug hotkey actions (stance forced, HP changed)
- `[AI][Fleet]` - Stance changes, material score, roster, contacts
- `[AI]` - Individual ship movement/attack decisions

## Tips

- Use **0** (zero) frequently to reset to auto mode between tests
- The **-** (minus) key is useful for quickly setting up Retreat scenarios
- Combine hotkeys: press **-** then **5** for "wounded fleet in Retreat"
- Watch the `[AI][Fleet]` logs to confirm the stance actually changed
- Stance overrides persist until you press **0** or restart Play mode


## Enhanced Move Logging (Stage 3)

The AI now logs stance-aware movement intent with clear labels:

### Log Format
```
[AI][STANCE_LABEL] ShipType moved to (x, y) (intent description)
```

### Stance Labels

| Label | Stance | Example |
|-------|--------|---------|
| `[HUNTING]` | Hunt | `[AI][HUNTING] WolfClass moved to (20, 5) (hunting (no contacts))` |
| `[ADVANCING]` | Advance | `[AI][ADVANCING] WolfClass moved to (18, 7) (lining up on AthenaClass)` |
| `[HOLDING]` | Hold | `[AI][HOLDING] AthenaClass moved to (25, 3) (holding position)` |
| `[FALLING BACK]` | DefensiveFallback | `[AI][FALLING BACK] WolfClass moved to (26, 2) (falling back)` |
| `[RETREATING]` | Retreat | `[AI][RETREATING] SwordFishClass moved to (28, 1) (retreating)` |
| `[FLEE]` | Any (low HP) | `[AI][FLEE] WolfClass moved to (29, 0) (fleeing to safety)` |

### Detailed Scoring Debug

For deep debugging, open `Assets/Scripts/AI/AImovementplanner1.cs` and change:
```csharp
private const bool DebugScoring = false;  // Change to true
```

This will log tile-by-tile scoring breakdown:
```
[AI][Scoring] WolfClass tile=(18,7) enemy=AthenaClass: base=35.2 formation=2.1 directional=5.0 total=42.3
```

**Components:**
- `base` = Attack value + danger penalty + target weakness
- `formation` = Cohesion + fragility pushback + scout forward
- `directional` = Stance-driven movement bias (Advance/Retreat/Fallback)

**Warning:** This produces ~50-100 log lines per ship per turn. Only enable when debugging specific movement decisions.


---

*Debug system added in Stage 3 for stance-driven movement testing.*

