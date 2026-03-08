# Death Roll (Ex) - Godot Inspector Setup

This document explains how to set up the **Death Roll** special ability using the generic `Effect_OnGrappleMaintain` component. This system automatically overrides standard grapple damage, applies the bite damage, and knocks the target prone.

## 1. Ability Resource Setup
Create a new `Ability_SO` named `Ability_DeathRoll.tres`.

### Core Info
* **Ability Name:** `Death Roll`
* **Category:** `SpecialAttack`
* **Special Ability Type:** `Ex`
* **Action Cost:** `NotAnAction` *(It triggers automatically as part of a Standard Action grapple maintain check).*
* **Target Type:** `SingleEnemy`

### Effect Components
Add a new element to the **Effect Components** array and assign an `Effect_OnGrappleMaintain` script to it.

Configure the `Effect_OnGrappleMaintain` properties:
* **Size Difference Allowed:** `0` *(0 means the target must be the exact same size or smaller. If it was 1, it would mean one size category larger).*
* **Use Natural Attack Damage:** `On` (True)
* **Natural Attack Name:** `Bite`
* **Extra Damage:** `[Empty]` *(No bonus damage on top of the bite).*
* **Effect To Apply:** Drag and drop your generic `Prone_Effect.tres` here.

## 2. Creature Template Integration
Navigate to the creature's `CreatureTemplate_SO` (e.g., Crocodile, Alligator).
1. Scroll down to **Abilities & Spells** -> **Known Abilities**.
2. Add a new element to the array and drop `Ability_DeathRoll.tres` into it.
3. *Ensure the creature actually has a Melee Attack named "Bite" in its Melee Attacks array.*

## How it works at Runtime
1. The creature successfully initiates a Grapple.
2. On its next turn, the AI selects `AIAction_MaintainGrapple` and chooses the `Damage` sub-action.
3. `CombatManeuvers.cs` rolls the CMB check to maintain. If successful, the grapple is maintained.
4. `CombatManeuvers.cs` scans the creature's known abilities and finds `Death Roll` (because it contains `Effect_OnGrappleMaintain`).
5. The script verifies the target is not larger than the attacker.
6. The script finds the creature's "Bite" attack, rolls its damage (including Strength modifiers), and deals it to the target.
7. Finally, it applies the `Prone_Effect.tres` to the victim.