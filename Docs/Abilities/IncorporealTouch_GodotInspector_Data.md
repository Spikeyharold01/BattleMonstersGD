# Incorporeal Touch (Wisdom Damage) - Godot Inspector Setup

This document outlines how to set up an attack that deals *no* physical Hit Point damage, but instead applies Ability Damage upon a successful hit (e.g., a Shadow or an Allip).

## 1. Create the Ability Damage Payload
First, we create the payload that actually drains the Wisdom.
1. Create an `Ability_SO` named `Ability_WisdomDrainTouch.tres`.
2. **Ability Name:** `Wisdom Drain`
3. **Category:** `SpecialAttack`
4. **Special Ability Type:** `Su`
5. **Action Cost:** `NotAnAction` *(It rides freely on the melee hit)*
6. **Target Type:** `SingleEnemy`

### Effect Components
Add an `Effect_AbilityDamage` script to the **Effect Components** array.
1. In the `AbilityDamages` array, add a new element (`AbilityDamageInfo` resource).
2. **Stat To Damage:** `Wisdom`
3. **Dice Count:** `1`
4. **Die Sides:** `4`

## 2. Configure the Natural Attack
Now, we attach the payload to the creature's attack profile.
Open the Creature's `CreatureTemplate_SO` (e.g., the Allip).

1. Scroll down to **Offense & Movement** -> **Melee Attacks**.
2. Add a new `NaturalAttack` resource.
3. **Attack Name:** `Incorporeal Touch`
4. **Is Primary:** `On` (True)
5. **On Hit Ability:** Drag and drop `Ability_WisdomDrainTouch.tres` here.

### Muting the HP Damage
To ensure the attack doesn't accidentally deal `1d4 + Str` bludgeoning damage:
1. In the **Damage Info** array of the `NaturalAttack`, add `1` element.
2. **Dice Count:** `0`
3. **Flat Bonus:** `0`
4. **Damage Type:** `None` *(This keyword tells the patched combat engine to completely skip HP reduction for this strike).*

## How it works at Runtime
1. The AI calculates that the touch attack is highly valuable (`Effect_AbilityDamage` scores 80 points) and moves to attack.
2. `CombatAttacks.cs` calculates the hit. Because the creature possesses the `Incorporeal` trait, it substitutes Dexterity for Strength and ignores the target's Armor/Natural Armor.
3. The attack hits. The engine sees `DamageType = "None"` and skips HP reduction entirely.
4. The engine checks `OnHitAbility`, finds the Wisdom Drain payload, and executes it.
5. The `Effect_AbilityDamage` script rolls `1d4` and subtracts it directly from the target's Wisdom score.