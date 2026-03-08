# Undead Traits (Ex) - Godot Inspector Setup

This document explains how the **Undead Traits** defensive package is structured using the data-driven `Trait_SO` system. 

*Note: If you use the Excel Creature Importer, this trait is generated and assigned automatically when "Undead Traits" is detected in the creature's data fields.*

## Resource Details
* **Type:** `Trait_SO`
* **File Name:** `Trait_Undead.tres` (Generated in `res://Data/Creatures/Traits/`)

## Inspector Configuration

### Core Data
* **Trait Name:** `Undead Traits`
* **Description:** `Undead are immune to death effects, disease, mind-affecting effects... (etc).`

### Immunities Array
Add the following exactly `14` elements to the `Immunities` array on the `Trait_SO` asset:
1. `MindAffecting` *(Blocks Charm, Pattern, Phantasm, Morale)*
2. `DeathEffects`
3. `Disease`
4. `Paralysis`
5. `Poison`
6. `Sleep`
7. `Stun`
8. `FortitudeSaves_NoDamage` *(Blocks any effect requiring a Fort save unless it also deals damage, e.g., Disintegrate)*
9. `AbilityDrain`
10. `EnergyDrain` *(Blocks standard negative levels)*
11. `NonlethalDamage`
12. `PhysicalAbilityDamage` *(Specifically blocks STR, DEX, and CON damage and stat penalties, while still allowing mental stat damage like INT, WIS, CHA)*
13. `Fatigue`
14. `Exhaustion`

## How it works at Runtime
1. By having the `PhysicalAbilityDamage` flag, `CreatureStats.cs` checks the `StatToModify` whenever a Debuff is applied or damage is dealt. If the stat is physical (STR, DEX, CON), the engine ignores the penalty automatically.
2. If a caster uses a Spell like *Flesh to Stone* (which requires a Fortitude save but does no HP damage), the `FortitudeSaves_NoDamage` immunity causes `CombatMagic.cs` to quietly reject the spell without requiring a roll.