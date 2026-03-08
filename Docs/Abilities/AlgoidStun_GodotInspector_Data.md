# Algoid Stun (Ex) - Godot Inspector Setup

This document outlines how to set up the Algoid's **Stun** ability so it triggers automatically when a slam attack scores a critical hit, calculating its DC based on the Algoid's Strength modifier.

## 1. Ability Resource Setup
Create a new `Ability_SO` named `Ability_AlgoidStun.tres`.

### Core Info
* **Ability Name:** `Algoid Stun`
* **Category:** `SpecialAttack`
* **Special Ability Type:** `Ex` (Extraordinary)
* **Action Cost:** `NotAnAction` *(It rides freely on the critical hit)*
* **Target Type:** `SingleEnemy`
* **Range:** `Touch` 

### Saving Throw
* **Save Type:** `Fortitude`
* **Base DC:** `16` *(Fallback value if dynamic stats fail)*
* **Is Special Ability DC:** `On` (True) *(Calculates using 10 + 1/2 HD + Stat Mod)*
* **Dynamic DC Stat:** `Strength`
* **Effect On Success:** `Negates`

### Effect Components
Add a new element to the **Effect Components** array and assign a `PartialSaveEffect` script to it.

Configure the `PartialSaveEffect` properties:
* **Status Effect:** Drag and drop your generic `Stunned_Effect.tres` here.
* **Fail Duration:** `1d2` (Parsed dynamically at runtime).
* **Success Duration:** `0` (A duration of 0 completely ignores the effect).

## 2. Creature Template Integration
Navigate to the Algoid's `CreatureTemplate_SO`.

1. Scroll down to **Offense & Movement** -> **Melee Attacks**.
2. Expand the `Slam` attack.
3. Locate the new **On Crit Ability** field.
4. Drag and drop `Ability_AlgoidStun.tres` into this field.

## How it works at Runtime
1. The AI (or player) attacks with the Algoid's Slam.
2. If the attack rolls a 20 (or matches the threat range) and is confirmed, `CombatAttacks.cs` flags `isCriticalHit = true`.
3. The Combat Engine detects the `OnCritAbility` attached to the natural attack and instantly triggers `ResolveAbility` against the defender.
4. The target rolls a Fortitude save. `CombatMagic.cs` dynamically calculates the DC using `10 + (Algoid HD/2) + Algoid Strength Modifier`.
5. If the save fails, the target is Stunned for `1d2` rounds.
