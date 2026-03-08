# Mind Blast (Sp) - Godot Inspector Setup

This document explains how to configure the **Mind Blast** ability using the fully data-driven pipeline. By utilizing `PartialSaveEffect`, the ability automatically scales its duration via dice strings and applies the precise DC logic based on the creature's Charisma and Hit Dice.

## Ability Resource Setup
Create a new `Ability_SO` named `Ability_MindBlast.tres`.

### Core Info
* **Ability Name:** `Mind Blast`
* **Category:** `SpecialAttack`
* **Special Ability Type:** `Sp` (Spell-Like)
* **Action Cost:** `Standard`
* **Target Type:** `Area_FriendOrFoe` (Affects anyone caught in the cone)

### Area of Effect
* **Shape:** `Cone`
* **Range:** `60`

### Usage Limitation
* **Type:** `PerDay`
* **Uses Per Day:** `1`

### Saving Throw
* **Save Type:** `Will`
* **Base DC:** `14` *(Fallback value if dynamic stats fail)*
* **Is Special Ability DC:** `On` (True) *(Calculates using 10 + 1/2 HD + Stat Mod)*
* **Dynamic DC Stat:** `Charisma`
* **Dynamic DC Bonus:** `2` *(Applies the +2 Racial Bonus)*

### Effect Components
Add a new element to the **Effect Components** array and assign a `PartialSaveEffect` script to it.

Configure the `PartialSaveEffect` properties:
* **Status Effect:** Drag and drop your generic `Stunned_Effect.tres` here.
* **Fail Duration:** `3d4` (The string will be parsed dynamically at runtime).
* **Success Duration:** `0` (A duration of 0 completely negates the effect).

## How it works at Runtime
1. The AI (or player) aims the 60-foot cone at an area containing enemies.
2. `CombatMagic.cs` calculates the exact DC by looking up the creature's HD, retrieving its Charisma Modifier, and adding the +2 `DynamicDCBonus`.
3. Every creature in the cone (ally or enemy) rolls a Will save.
4. `PartialSaveEffect.cs` is executed. If a creature failed the save, it rolls `3d4` to determine how many rounds the Stun lasts. If they succeeded, the duration becomes `0` and no status is applied.