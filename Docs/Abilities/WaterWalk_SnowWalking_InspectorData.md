# Water Walk & Snow Walking — Godot Inspector Data Setup

This setup covers three interconnected abilities using entirely generic systems:
1.  **Snow Walking (Su)** (Passive trait for Akhlut)
2.  **Water Walk** (Spell)
3.  **Water Walk, Communal** (Spell)

No unique scripts are needed; these rely on the updated `SwimController`, `ScentSystem`, and `Pathfinding` logic.

---

## 1) Snow Walking (Su)
The Akhlut's ability lets it traverse snow and ice perfectly without leaving a trail. It doesn't actually allow them to walk on lava or acid, so we don't grant them the full `WaterWalking` condition.

### A. Create `SE_SnowWalking` (StatusEffect_SO)
*   **EffectName:** `Snow Walking`
*   **DurationInRounds:** `0` (Permanent)
*   **IgnoreSnowAndIceMovementPenalty:** `true` (Solves traversing snow/ice freely)
*   **SuppressesScentTrails:** `true` (Solves the "leave no trail" ecology rule)

### B. Create `Ability_SO` for the Creature
*   **AbilityName:** `Snow Walking`
*   **SpecialAbilityType:** `Su`
*   **ActionCost:** `NotAnAction`
*   **Effect Components:** *(Leave empty, as we'll add the status to the creature's passive list)*

### C. Apply to Akhlut
1.  Open the Akhlut's `CreatureTemplate_SO`.
2.  Add `Snow Walking` to the **Special Qualities** array.
3.  Add `SE_SnowWalking` to the **Passive Effects** array (so it applies immediately on spawn in both Arena and Travel).

---

## 2) Water Walk (Spell)

### A. Create `SE_WaterWalk` (StatusEffect_SO)
*   **EffectName:** `Water Walk`
*   **DurationScalesWithLevel:** `true`
*   **DurationPerLevel:** `100` (10 minutes per level = 100 rounds)
*   **ConditionApplied:** `WaterWalking`

### B. Create `Ability_SO`
*   **AbilityName:** `Water Walk`
*   **TargetType:** `SingleAlly`
*   **Range.Type:** `Touch`
*   **SavingThrow.SaveType:** `Will`
*   **SavingThrow.EffectOnSuccess:** `Negates`
*   **Effect Components:** Add `ApplyStatusEffect`
    *   `EffectToApply`: `SE_WaterWalk`
    *   `DurationIsPerLevel`: `false`

---

## 3) Water Walk, Communal (Spell)

To achieve the "divided in 10-minute intervals" rule perfectly, we use the `DistributionStepRounds` field.

### A. Create `Ability_SO`
*   **AbilityName:** `Water Walk, Communal`
*   **TargetType:** `Area_AlliesOnly` (Or your project's multi-touch equivalent)
*   **Range.Type:** `Touch`
*   **SavingThrow.SaveType:** `Will`
*   **SavingThrow.EffectOnSuccess:** `Negates`
*   **Effect Components:** Add `ApplyStatusEffect`
    *   `EffectToApply`: `SE_WaterWalk`
    *   `DurationIsPerLevel`: `true`
    *   `DistributionStepRounds`: `100` (10 minutes = 100 rounds. The duration will now be chunked precisely into 10-minute blocks distributed across targets).

*Note: In the `SE_WaterWalk` asset, ensure `DurationIsDivided` is checked to `true`!*