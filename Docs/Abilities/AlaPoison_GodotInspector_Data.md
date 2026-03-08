# Ala's Poison (Ex) — Godot Inspector Data Setup

This ability leverages the `IsAffliction` engine and the new `PassiveRetaliationController` to ensure the poison is delivered both on the Ala's bite *and* defensively when an enemy bites the Ala.

## 1) Create the Conditions
You will need two basic status effects that last for exactly 1 round (because the poison ticks every round, refreshing the condition if they fail).
1. Create `SE_Sickened_1Rnd` (`ConditionApplied: Sickened`, `DurationInRounds: 1`).
2. Create `SE_Nauseated_1Rnd` (`ConditionApplied: Nauseated`, `DurationInRounds: 1`).

## 2) Create the Poison Status Effect (The Affliction)
Create a new `StatusEffect_SO` named `SE_AlaPoison_Affliction`:
*   **EffectName:** `Ala's Poison`
*   **DurationInRounds:** `6` (Max duration of the poison).
*   **IsAffliction:** `true`
*   **ConsecutiveSavesToCure:** `1`
*   **FrequencySeconds:** `6` (This means it forces a save every 1 round).
*   **AfflictionCondition:** Assign `SE_Sickened_1Rnd`.
*   **AfflictionEscalatedCondition:** Assign `SE_Nauseated_1Rnd`.

## 3) Create the Delivery Ability
Create a new `Ability_SO` named `Ability_AlaPoison_Delivery`:
*   **AbilityName:** `Ala's Poison`
*   **Category:** `SpecialAttack`
*   **SavingThrow.SaveType:** `Fortitude`
*   **SavingThrow.BaseDC:** `17` (Or use `IsSpecialAbilityDC` and `Constitution` to scale dynamically).
*   **Effect Components:** Add `ApplyStatusEffect`
    *   `EffectToApply`: `SE_AlaPoison_Affliction`

## 4) Apply to the Ala Creature
We need to apply this in **two places** so it works offensively and defensively.

**Offensive (On Bite):**
1. Open the Ala's `CreatureTemplate_SO`.
2. Expand the **Melee Attacks** array and find the **Bite** attack.
3. In the `OnHitAbility` field for the Bite attack, drag and drop `Ability_AlaPoison_Delivery`. 

**Defensive (On getting Bitten):**
1. Open the Ala's 3D Scene / Prefab.
2. Add a `PassiveRetaliationController` node as a child.
3. In the inspector for the controller:
    *   **RetaliationAbility:** Drag and drop `Ability_AlaPoison_Delivery`.
    *   **TriggerOnNaturalAttacks:** `True`
    *   **SpecificNaturalAttackKeyword:** `bite` (This ensures it only poisons wolves/dragons that bite it, not a bear that swats it with a claw).
    *   **TriggerOnManufacturedMeleeWeapons:** `False`