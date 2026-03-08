# Touch of Insanity (Su) - Godot Inspector Setup

This document outlines how to set up the **Touch of Insanity** ability. Because a critical hit does 1d4 damage + 1 drain (instead of doubling the 1d4), we will split this into two payloads. The base `OnHitAbility` does the 1d4 damage and the Temp HP. The `OnCritAbility` does the 1 point of Drain. 

When a critical hit occurs, the engine automatically fires the Hit payload, and then immediately fires the Crit payload.

## 1. Create the Base Hit Payload
Create an `Ability_SO` named `Ability_InsanityTouch_Hit.tres`.

### Core Info
* **Ability Name:** `Touch of Insanity`
* **Category:** `SpecialAttack`
* **Action Cost:** `NotAnAction`
* **Target Type:** `SingleEnemy`

### Saving Throw
* **Save Type:** `Will`
* **Base DC:** `15` *(Fallback)*
* **Is Special Ability DC:** `On` (True) *(Calculates using 10 + 1/2 HD + Cha Mod)*
* **Dynamic DC Stat:** `Charisma`

### Effect Components
We need **two** components here:

1. **Effect_AbilityDamage**
   * Expand `AbilityDamages`, add `1` element.
   * **Stat To Damage:** `Wisdom`
   * **Dice Count:** `1`
   * **Die Sides:** `4`
   * **Is Drain:** `Off` (False)
2. **Effect_GrantTempHpToCaster**
   * **Flat Amount:** `5`
   * **Use Dice:** `Off` (False)

---

## 2. Create the Critical Hit Payload
Create an `Ability_SO` named `Ability_InsanityTouch_Crit.tres`.

### Core Info
* **Ability Name:** `Touch of Insanity (Crit Drain)`
* **Category:** `SpecialAttack`
* **Action Cost:** `NotAnAction`
* **Target Type:** `SingleEnemy`

### Saving Throw
* **Save Type:** `Will`
* **Is Special Ability DC:** `On` (True)
* **Dynamic DC Stat:** `Charisma`

### Effect Components
We only need **one** component here:

1. **Effect_AbilityDamage**
   * Expand `AbilityDamages`, add `1` element.
   * **Stat To Damage:** `Wisdom`
   * **Dice Count:** `0`
   * **Die Sides:** `0`
   * **Is Drain:** `On` (True)
   * *(Note: Because dice count is 0, Godot will look for a Flat Bonus, but we don't have one in `AbilityDamageInfo` yet. Wait! We need to make sure the drain applies `1`. So change `DiceCount: 1` and `DieSides: 1` to force a flat `1` damage roll from the dice parser).*

---

## 3. Creature Template Integration
Open the Creature's `CreatureTemplate_SO` (e.g., Allip).

1. Scroll down to **Offense & Movement** -> **Melee Attacks**.
2. Add or open the `Incorporeal Touch` attack.
3. In **Damage Info**, set `DamageType` to `None` so it deals 0 physical HP damage.
4. **On Hit Ability:** Drag and drop `Ability_InsanityTouch_Hit.tres`.
5. **On Crit Ability:** Drag and drop `Ability_InsanityTouch_Crit.tres`.