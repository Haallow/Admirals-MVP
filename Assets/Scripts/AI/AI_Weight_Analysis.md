# AI Weight Analysis & Tuning Guide

**Stage 5 - Complete System Weight Review**

## Weight Hierarchy (by magnitude)

All weights are scaled as fractions of ship maxHealth, so they work consistently across 400 HP (SwordFish) and 2000 HP (Athena) ships.

### Core Attack Scoring (Base Layer)
| Weight | Value | Purpose | Scale |
|--------|-------|---------|-------|
| **KillBonus** | 50 | Guaranteed kill | Dominates everything |
| **MaxDamageScore** | 40 | High-damage non-lethal hit | Second priority |
| **DangerWeight** | 35 | Penalty for exposed tiles | Balances with damage |
| **AlreadyClaimedPenalty** | 20 | Discourage piling on | Less than KillBonus |
| **SelfRiskWeight** | 15 | Caution when wounded | Subtle |
| **TargetWeaknessWeight** | 10 | Prefer wounded enemies | Tiebreaker |

**Total range:** Attack scores typically 0-90 per tile

### Formation Terms (Stage 2)
| Weight | Value | Purpose | Multiplier |
|--------|-------|---------|------------|
| **FragilityPushbackWeight** | 12 | Fragile ships drop back | × distance delta |
| **CohesionBaseWeight** | 8 | Pull toward centroid | × stance mult (0.0-1.0) |
| **ScoutForwardWeight** | 6 | Scouts advance | × distance delta |

**Total range:** Formation terms add -15 to +15 per tile (small nudges, not overrides)

### Directional Bias (Stage 3)
| Weight | Value | Purpose | When Active |
|--------|-------|---------|-------------|
| **RetreatDirectionalWeight** | 18 | Strong pull toward home | Retreat stance |
| **FallbackDirectionalWeight** | 10 | Medium pull toward home | DefensiveFallback |
| **AdvanceDirectionalWeight** | 10 | Medium push toward contacts | Advance stance |

**Total range:** Directional bias adds -20 to +20 per tile

### Combined Scoring Example

**Scenario:** Wolf (500 HP) scoring a tile in Advance stance with a contact visible

| Component | Contribution | Notes |
|-----------|--------------|-------|
| Base attack | +35 | Good shot, moderate danger |
| Formation | +3 | Scout forward bonus, slight cohesion pull |
| Directional | +8 | Advance bias toward contact (2 tiles closer × 10 weight) |
| **Total** | **46** | |

**Another tile:**
| Component | Contribution | Notes |
|-----------|--------------|-------|
| Base attack | +30 | Weaker shot |
| Formation | -2 | Moving away from centroid |
| Directional | +15 | Much closer to contact (3 tiles × 10 weight) |
| **Total** | **43** | First tile still wins despite stronger directional bias |

## Weight Relationships

### Critical Balance Points

1. **KillBonus (50) >> All Formation + Directional (max ~35)**
   - Guaranteed kills always win, even if the tile has bad positioning
   - ? Correct: Kills are always worth taking

2. **MaxDamageScore (40) ˜ DangerWeight (35)**
   - High damage balanced against exposure
   - ? Correct: Risk-reward is balanced

3. **DirectionalWeight (18 max) > CohesionWeight (8 max)**
   - Stance-driven movement overrides tight formation in Retreat
   - ? Correct: Retreat should pull ships home even if it breaks formation

4. **FragilityPushback (12) > ScoutForward (6)**
   - Fragile ships dropping back overrides scout bonus
   - ? Correct: Fragile scouts shouldn't charge recklessly

5. **AlreadyClaimedPenalty (20) < MaxDamageScore (40)**
   - Piling on is discouraged but not blocked if it's the best shot
   - ? Correct: Spreads fire without preventing good opportunities

## Tuning Recommendations

### If Fleet is Too Aggressive
- **Increase** DangerWeight (35 ? 45)
- **Decrease** MaxDamageScore (40 ? 30)
- **Increase** FragilityPushbackWeight (12 ? 16)

### If Fleet is Too Passive
- **Decrease** DangerWeight (35 ? 25)
- **Increase** AdvanceDirectionalWeight (10 ? 15)
- **Decrease** CohesionBaseWeight in Hold stance multiplier (0.7 ? 0.5)

### If Formation is Too Loose
- **Increase** CohesionBaseWeight (8 ? 12)
- **Increase** Hold stance multiplier (0.7 ? 0.9)
- **Decrease** Hunt stance multiplier (already 0.0, cannot go lower)

### If Formation is Too Tight
- **Decrease** CohesionBaseWeight (8 ? 5)
- **Increase** RetreatVariance (4 ? 6) for more spread

### If Focus Fire is Insufficient (Advance stance)
- In AIAttackPlanner, decrease Advance penalty (75 ? 50 or even 0)
- Current: 75 (light discouragement)
- More aggressive: 0 (no penalty, full pile-on)

### If Focus Fire is Too Much
- Increase Advance penalty (75 ? 150)
- Ships will spread fire more even in Advance

## Stage 4: Battle Coordination Weights

| Constant | Value | Purpose |
|----------|-------|---------|
| **AIAttackPlanner.AlreadyClaimedPenalty** | 300 | Base spread-fire penalty |
| **Advance stance override** | 75 | Reduced penalty for focus fire |

**Ratio:** 300:75 = 4:1 (Advance encourages piling on 4× more than normal)

### Target Prioritization Formula
```
Priority = Threat × (1 + Fragility)

Threat = S(weapon_expectedValue × rangeMult)
  rangeMult = 1.5 if weapon in range of fleet centroid
  rangeMult = 1.0 otherwise

Fragility = 1 - (currentHP / maxHP)
```

**Example:**
- Athena (1800/2000 HP, 3 weapons @ 300 dmg each, in range)
  - Threat = (300×1.5) + (300×1.5) + (300×1.5) = 1350
  - Fragility = 1 - 0.9 = 0.1
  - Priority = 1350 × 1.1 = **1485**

- Wolf (100/500 HP, 2 weapons @ 150 dmg each, out of range)
  - Threat = (150×1.0) + (150×1.0) = 300
  - Fragility = 1 - 0.2 = 0.8
  - Priority = 300 × 1.8 = **540**

**Result:** Athena targeted first (higher threat despite being healthier)

## Known Good Configurations

### Current Default (Balanced)
- Works well for mixed fleets (2-3 ship types)
- Advance is aggressive but not suicidal
- Retreat actually retreats
- Hold maintains position
- Focus fire in Advance without total pile-on

### Aggressive Variant
```
MaxDamageScore = 50
DangerWeight = 25
AdvanceDirectionalWeight = 15
Advance claimed penalty = 50
```

### Defensive Variant
```
DangerWeight = 45
FragilityPushbackWeight = 16
RetreatDirectionalWeight = 25
CohesionBaseWeight = 12
```

---

*Weight analysis completed Stage 5. All values are on compatible scales and properly balanced.*
