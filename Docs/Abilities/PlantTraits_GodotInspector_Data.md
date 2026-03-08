# Plant Traits (Ex) - Godot Inspector Setup

This document explains how the **Plant Traits** defensive ability is structured using the data-driven `Trait_SO` system. 

*Note: If you use the Excel Creature Importer, this trait is generated and assigned automatically when "Plant Traits" is detected in the creature's data.*

## Resource Details
* **Type:** `Trait_SO`
* **File Name:** `Trait_Plant.tres` (Generated in `res://Data/Creatures/Traits/`)

## Inspector Configuration

### Core Data
* **Trait Name:** `Plant Traits`
* **Description:** `Plants are immune to all mind-affecting effects (charms, compulsions, morale effects, patterns, and phantasms), paralysis, poison, polymorph, sleep, and stun.`

### Immunities Array
Add the following elements to the `Immunities` array on the `Trait_SO` asset:
1. `MindAffecting`
2. `Paralysis`
3. `Poison`
4. `Polymorph`
5. `Sleep`
6. `Stun`

### Conversions & Passives
* **Conversions:** `[Empty]`
* **Associated Passive Ability:** `[Empty]` (This trait acts purely as a defensive filter).

## How it works at Runtime
When an ability attempts to apply a Status Effect (e.g., *Charm Person*, *Hold Monster*, *Baleful Polymorph*), the `StatusEffectController` checks the incoming `StatusEffect_SO` against the creature's active immunities. 

Because `Trait_Plant.tres` injects these flags into the creature's `HasImmunity` check, any `StatusEffect_SO` with `IsMindControlEffect = true`, `ConditionApplied = Condition.Polymorphed`, or `EffectName` containing "Poison" will automatically be rejected without a saving throw.