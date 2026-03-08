# Control Water - Godot Inspector Setup

This document outlines how to set up the two variants of the **Control Water** spell. Because the spell can either Raise or Lower water, we create two separate `Ability_SO` assets.

## 1. Create the Slow Status Effect
First, create the Slow debuff applied to water elementals.
1. Create a `StatusEffect_SO` named `Slow_Aquatic_Effect.tres`.
2. **Condition Applied:** `Impeded` *(This mimics the speed and action restrictions of Slow).*
3. **Modifications:** Add a penalty to `ArmorClass`, `AttackRoll`, and `ReflexSave` (-1 each, as per standard PF1e *Slow*).

## 2. Ability Resource: Control Water (Lower)
Create a new `Ability_SO` named `Ability_ControlWater_Lower.tres`.

### Core Info
* **Ability Name:** `Control Water (Lower)`
* **Category:** `Spell`
* **Action Cost:** `Standard`
* **Target Type:** `Area_FriendOrFoe`
* **Range:** `Long`

### Saving Throw
* **Save Type:** `Will`
* **Base DC:** `14` *(Fallback)*
* **Is Dynamic DC:** `On` (True)
* **Dynamic DC Stat:** `Wisdom` *(Or Intelligence/Charisma depending on the primary class using it)*

### Effect Components
Add a new element and assign an `Effect_ControlWater` script.
* **Is Lower Water:** `On` (True)
* **Aquatic Slow Effect:** Drag and drop `Slow_Aquatic_Effect.tres` here.

---

## 3. Ability Resource: Control Water (Raise)
Create a new `Ability_SO` named `Ability_ControlWater_Raise.tres`.

### Core Info
* **Ability Name:** `Control Water (Raise)`
* **Category:** `Spell`
* **Action Cost:** `Standard`
* **Target Type:** `Area_FriendOrFoe`
* **Range:** `Long`

### Saving Throw
* **Save Type:** `None`

### Effect Components
Add a new element and assign an `Effect_ControlWater` script.
* **Is Lower Water:** `Off` (False)
* **Aquatic Slow Effect:** Leave empty.

## How it works at Runtime
1. The AI or Player selects the ground to target the spell.
2. The `Effect_ControlWater` script calculates the exact dimensions of the spell (10ft * CL width, 2ft * CL height) and duration (10 min * CL).
3. It spawns an invisible `PersistentEffect_ControlWater` 3D node at the target location.
4. The controller modifies the underlying `GridManager` nodes.
    * If **Lowering**, it turns `Water` nodes into `Air`, dropping swimming creatures to the ground. It also forces any creatures with the "Aquatic" or "Water" subtypes to make a Will save or be slowed.
    * If **Raising**, it turns `Ground` and `Air` nodes into `Water` nodes, forcing land creatures to begin making Swim checks.
5. When the 10 min/level duration expires, the controller perfectly restores the Grid nodes to their original pre-spell states.