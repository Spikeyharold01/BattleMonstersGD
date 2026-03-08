# Frightful Presence (Ex) — Godot Inspector Data Setup

Because this ability is a complex mixture of Hit Dice math and automatic triggering, it requires configuring an `Ability_SO` and attaching a small Controller node to the creature.

## 1) Prepare the Status Effects
You will need your standard Fear status effects ready in your project data:
1.  **SE_Shaken** (`ConditionApplied: Shaken`, `Tag: Fear`, `IsMindControlEffect: true`)
2.  **SE_Panicked** (`ConditionApplied: Panicked`, `Tag: Fear`, `IsMindControlEffect: true`)

## 2) Create the Ability_SO
Create a new `Ability_SO` named `Special_FrightfulPresence`:
*   **AbilityName:** `Frightful Presence`
*   **Category:** `SpecialAttack`
*   **SpecialAbilityType:** `Ex`
*   **ActionCost:** `Free`
*   **TargetType:** `Area_EnemiesOnly`
*   **AreaOfEffect.Shape:** `Burst`
*   **AreaOfEffect.Range:** `30` (Standard range)
*   **SavingThrow.SaveType:** `Will`
*   **SavingThrow.EffectOnSuccess:** `Negates`
*   **SavingThrow.IsSpecialAbilityDC:** `true` (This automatically calculates the DC as `10 + 1/2 HD + Charisma Modifier`!)
*   **SavingThrow.DynamicDCStat:** `Charisma`
*   **Effect Components:** Add `Effect_FrightfulPresence`
    *   `ShakenEffectTemplate`: Assign `SE_Shaken`
    *   `PanickedEffectTemplate`: Assign `SE_Panicked`
    *   `PanicHDThreshold`: `4`
    *   `DurationFormula`: `5d6`

## 3) Attach to the Creature
1. Open the Creature's Scene (e.g., Adult Red Dragon).
2. Add the `Special_FrightfulPresence` to their `CreatureTemplate_SO` **Special Attacks** array so players can read the tooltip.
3. Add a **FrightfulPresenceController** node as a child of the creature root.
4. In the Inspector for the controller:
    *   **Frightful Presence Ability:** Drag and drop `Special_FrightfulPresence`.
    *   **Is Aura:** Check this if the monster's specific rules say it acts as a constant aura.
    *   **Triggers On Attack:** Check this if the rules say it triggers when they attack or charge (standard Pathfinder rule).

### Why this works seamlessly:
Because the Controller listens to the global `OnOffensiveActionRecorded` event, the AI never has to "think" about using Frightful Presence. When the AI decides to perform a standard `AIAction_Attack` or `AIAction_Charge`, the Controller detects the attack and automatically weaves the Frightful Presence burst into the resolution stack.