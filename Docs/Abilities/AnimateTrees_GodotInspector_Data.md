# Animate Trees (Su) - Godot Inspector Setup

This document outlines how to set up the Algoid's **Animate Trees** ability, allowing it to transform static environmental objects into combat-ready allies, restricted exclusively to Trees.

## 1. Preparing the Scene Objects
Ensure that the static trees placed in your world scenes (the ones the player or AI can click on) are assigned to the Godot Group `"Tree"`.
1. Select the static tree prefab.
2. Go to the **Node** panel -> **Groups**.
3. Add the group `"Tree"`.
4. Ensure the prefab has an `ObjectDurability` component so the combat engine recognizes it as a valid environmental object.

## 2. Preparing the Animated Tree Template
You must first have a valid `CreatureTemplate_SO` for the Animated Tree that will replace the static object.
1. Create a `CreatureTemplate_SO` named `Animated_Tree_Template.tres`.
2. Assign its Type as **Plant**.
3. Set its **Speed_Land** to `10`.
4. Ensure it has a valid **CharacterPrefab** assigned (the visual 3D node that will spawn).

## 3. Ability Resource Setup
Create a new `Ability_SO` named `Ability_AnimateTrees.tres`.

### Core Info
* **Ability Name:** `Animate Trees`
* **Category:** `SpecialAttack`
* **Action Cost:** `Standard`
* **Target Type:** `SingleEnemy` *(Note: Because we configure EntityType below, this allows targeting objects instead of creatures).*
* **Entity Type:** `ObjectsOnly`
* **Range:** `Custom` -> `90`

### Effect Components
Add a new element to the **Effect Components** array and assign an `Effect_AnimateObject` script to it.

Configure the `Effect_AnimateObject` properties:
* **Animated Template:** Drag and drop your `Animated_Tree_Template.tres` here.
* **Max Controlled:** `2`
* **Control Range:** `90`
* **Requires Uproot Delay:** `On` (True)
* **Required Object Groups:** Add `1` element -> `"Tree"`

## How it works at Runtime
1. The AI (or player) targets a valid node in the environment possessing an `ObjectDurability` component.
2. The `Effect_AnimateObject` verifies the target belongs to the Godot Group `"Tree"`. If not, it fails immediately.
3. The static environmental object is deleted via `QueueFree()`.
4. The Animated Tree prefab is spawned in its place and injected into the Turn Manager's initiative order.
5. An `Uprooting` (Stunned) effect is automatically placed on the tree for 1 round.
6. The `AnimatedObjectController` ticks every turn. If the Algoid dies, falls Unconscious, or moves more than 90ft away, the tree immediately gains the **Inert** debuff (speed drops to 0).