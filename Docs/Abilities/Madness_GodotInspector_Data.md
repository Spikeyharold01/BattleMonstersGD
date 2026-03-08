# Madness (Su) - Godot Inspector Setup

This document outlines how to set up the **Madness** defensive ability (commonly found on the Allip). It uses the `PassiveSpellRetaliationController` to instantly counter-attack anyone who attempts to read or control the creature's mind.

## 1. Create the Wisdom Damage Payload
First, we create the payload that deals the 1d4 Wisdom damage.
1. Create an `Ability_SO` named `Ability_MadnessFeedback.tres`.
2. **Ability Name:** `Madness Feedback`
3. **Category:** `SpecialAttack`
4. **Special Ability Type:** `Su`
5. **Action Cost:** `NotAnAction`
6. **Target Type:** `SingleEnemy`
7. **Saving Throw -> Save Type:** `None` *(The ability states "takes 1d4 points of Wisdom damage", with no save allowed).*

### Effect Components
Add an `Effect_AbilityDamage` script to the **Effect Components** array.
1. In the `AbilityDamages` array, add a new element.
2. **Stat To Damage:** `Wisdom`
3. **Dice Count:** `1`
4. **Die Sides:** `4`

## 2. Configure the Creature Prefab
Now, we attach the listener to the creature so it can react to incoming spells.
1. Open the Creature's 3D Character Prefab in the Godot Editor (e.g., `Allip_Prefab.tscn`).
2. Add a new child Node to the root.
3. Attach the `PassiveSpellRetaliationController.cs` script to this new Node.
4. Configure the Inspector properties:
   * **Retaliation Ability:** Drag and drop `Ability_MadnessFeedback.tres` here.
   * **Trigger On Mind Affecting:** `On` (True)
   * **Trigger On Alignment Detection:** `Off` (False)

## How it works at Runtime
1. An enemy casts *Charm Person*, *Dominate Monster*, or *Detect Thoughts* with the Allip in the Area of Effect.
2. `CombatMagic.cs` processes the targets and calls `target.NotifyTargetedBySpell()`.
3. The `PassiveSpellRetaliationController` on the Allip hears the event. It checks the incoming spell's School, Name, and Description for "mind-affecting" or "thoughts" keywords.
4. It detects a match and instantly triggers `CombatManager.ResolveAbility`, aiming the `Madness Feedback` ability directly back at the caster.
5. The `Effect_AbilityDamage` rolls 1d4 and subtracts it from the original caster's Wisdom score.