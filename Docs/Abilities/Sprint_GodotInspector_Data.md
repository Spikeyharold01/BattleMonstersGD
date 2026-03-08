# Sprint (Ex) - Godot Inspector Setup

This document outlines how to configure the **Sprint** ability purely through data in the Inspector. Thanks to engine enhancements, the AI will automatically recognize when it needs this ability to close the gap on a distant target during a Charge and will fire it seamlessly.

## 1. Create the Speed Buff Effect
First, create the temporary status effect that grants the speed boost.
1. Create a `StatusEffect_SO` named `Sprint_Buff.tres`.
2. **Effect Name:** `Sprinting`
3. **Duration In Rounds:** `1`
4. Under **Modifications**, add a new element:
   * **Stat To Modify:** `Speed`
   * **Modifier Value:** `[Enter Bonus Here]` *(Note: If a creature has a base speed of 30 and Sprint increases it to 300, set this modifier to `270`)*
   * **Bonus Type:** `Enhancement` *(Or Untyped)*

## 2. Ability Resource Setup
Create a new `Ability_SO` named `Ability_Sprint.tres`.

### Core Info
* **Ability Name:** `Sprint`
* **Category:** `SpecialAttack`
* **Special Ability Type:** `Ex`
* **Action Cost:** `Free`
* **Target Type:** `Self`

### Usage Limitation (The "Once Per Minute" Rule)
* **Type:** `CooldownDuration`
* **Cooldown Duration Dice:** `10` *(In Pathfinder, 1 minute is exactly 10 combat rounds. The `AbilityCooldownController` will tick this down automatically).*

### Effect Components
Add a new element to the **Effect Components** array and assign an `ApplyStatusEffect` script to it.
* **Effect To Apply:** Drag and drop `Sprint_Buff.tres` here.

## 3. Creature Template Integration
Navigate to the creature's `CreatureTemplate_SO`.
1. Scroll down to **Abilities & Spells** -> **Known Abilities**.
2. Add a new element to the array and drop `Ability_Sprint.tres` into it.

## How it works at Runtime
1. **Player Use:** The player clicks "Sprint" (Free Action). The buff applies instantly. The player then selects a Move or Charge action, and the pathfinding range is massively expanded for that turn.
2. **AI Use:** The AI calculates the distance to its highest threat. If it is 200 feet away (normally unreachable), `AIAction_Charge` peeks at the Sprint ability. Seeing it's off cooldown, it adds the speed bonus to its simulated charge distance and determines the charge is viable. As the charge begins, the AI automatically activates Sprint, puts it on a 10-round cooldown, and clears the massive distance.