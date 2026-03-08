# Incorporeal (Ex) - Godot Inspector Setup

This document explains how the **Incorporeal** rule set works within the combat engine. Because this rule touches almost every system in Pathfinder, its logic is deeply embedded into the core C# files (`CombatAttacks.cs`, `Pathfinding.cs`, `CreatureStats.cs`, etc.).

*Note: If you use the Excel Creature Importer, any creature that possesses the `Incorporeal` SubType in the spreadsheet will automatically have the `Trait_Incorporeal.tres` data assigned to it.*

## How to Make a Creature Incorporeal
You do not need to assign any new scripts. You only need to apply the `Condition.Incorporeal` flag to the creature. This is done via a Passive Trait.

1. Locate or create `Trait_Incorporeal.tres`.
2. Under **Associated Passive Ability**, attach a blank `Ability_SO` named `Ability_IncorporealBaseState.tres`.
3. On that ability, add an `ApplyStatusEffect` component.
4. Set the applied Status Effect's **Condition Applied** to `Incorporeal`, and its **Duration In Rounds** to `0` (Permanent).
5. Drag `Trait_Incorporeal.tres` into the creature's **Traits** array in their `CreatureTemplate_SO`.

## What the Engine Automates
When a creature has `Condition.Incorporeal`, the engine automatically:
* Replaces their Strength modifier with their Dexterity modifier for all Melee Attack rolls and CMB.
* Removes their Strength modifier from all melee damage rolls.
* Replaces their Natural Armor and Armor bonuses with a Deflection bonus equal to their Charisma modifier.
* Allows them to pathfind through `Solid` terrain nodes (walls).
* Halves all incoming damage from corporeal sources, unless the attack is a Force effect (`"Force"`).
* Immune to all non-magical weapon damage.
* Rolls a 50% flat failure chance for any incoming spells that do not deal damage.
* Prevents them from being Grappled, Tripped, Bull Rushed, etc.
* Prevents them from taking Falling damage.
* Reduces their sound emission (`NoiseIntensity`) to 0.
* Erases their scent signature.