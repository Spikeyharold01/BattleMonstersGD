# Whirlwind & Lightning's Kiss — Godot Inspector Data Setup

This is a two-part setup. We will create the optional payload ability (Lightning's Kiss) first, then attach it to the main Whirlwind ability.

## 1) Create the Payload (Lightning's Kiss)
Create a new `Ability_SO` named `Special_LightningsKiss`:
*   **AbilityName:** `Lightning's Kiss`
*   **Category:** `SpecialAttack`
*   **SavingThrow.SaveType:** `Reflex`
*   **SavingThrow.EffectOnSuccess:** `HalfDamage`
*   **Effect Components:** Add `Effect_DynamicSynergyDamage`
    *   `BaseDamage`: `2d6 Electricity`
    *   `RequiredAllyCondition`: `WhirlwindForm`
    *   `ScalingDamagePerAlly`: `1d6 Electricity`
    *   `SynergyRadius`: `50`

## 2) Create the Main Ability (Whirlwind)
Create a new `Ability_SO` named `Special_Whirlwind`:
*   **AbilityName:** `Whirlwind`
*   **DescriptionForTooltip:** `Transform into a whirlwind. Does not provoke AoOs. Sweeps up creatures 1 size smaller...`
*   **Category:** `SpecialAttack`
*   **SpecialAbilityType:** `Su`
*   **ActionCost:** `Standard`
*   **TargetType:** `Self`
*   **Effect Components:** Add `Effect_Whirlwind`
    *   `Height`: `30` (Adjust based on monster limits, e.g., Ala is 10-30ft).
    *   `BaseDamage`: `2d8 Bludgeoning` (This is the slam damage explicitly dealt by the Ala's whirlwind).
    *   `PayloadAbilities`: Add `Special_LightningsKiss` to this array.

## 3) Apply to Creature
1. Open the Creature's `CreatureTemplate_SO`.
2. Add `Special_Whirlwind` to the **Special Attacks** array.

### How it works at Runtime:
1. The AI decides to use **Whirlwind** based on its massive control/AoE value.
2. The `Effect_Whirlwind` turns the caster into a storm. It attaches a `WhirlwindController` and sets the `WhirlwindForm` condition.
3. The combat engines instantly see `WhirlwindForm` and disable standard attacks and AoO provocations.
4. As the creature moves (`_PhysicsProcess`), the controller checks for smaller enemies overlapping its cylinder. 
5. When an enemy is hit, it takes the `BaseDamage` (Slam) and the controller loops through the `PayloadAbilities`. It executes **Lightning's Kiss**, which scans the grid for other allies in `WhirlwindForm` and adds massive electricity damage dynamically!
6. If grounded, the controller automatically handles the Debris Cloud concealment logic.