# Channel Resistance - Godot Inspector Setup

This document explains how to grant a creature **Channel Resistance** (+2, +4, etc.) using the data-driven Status Effect / Passive Trait system.

## Resource Details
Because Channel Resistance is a permanent defensive buff, it is applied via a passive `StatusEffect_SO` attached to the creature.

1. Create a `StatusEffect_SO` named `Passive_ChannelResistance_Plus4.tres` (or whatever value you need).
2. **Effect Name:** `Channel Resistance +4`
3. **Duration In Rounds:** `0` *(0 means permanent/passive)*

### Conditional Saves Array
We use the conditional saves array to ensure the bonus is *only* applied against Channel Energy.
1. Under **Conditional Saves**, click **Add Element**.
2. **Condition:** `AgainstChanneling`
3. **Modifier Value:** `4` *(Or 2, depending on the creature)*
4. **Bonus Type:** `Untyped` *(In Pathfinder, Channel Resistance stacks with standard Resistance bonuses on cloaks, so Untyped or Racial is appropriate here).*

## Creature Template Integration
To apply this to a creature (like a Vampire or a specific Undead):
1. Open the creature's `CreatureTemplate_SO`.
2. Scroll down to the **Passive Effects** array.
3. Drag and drop your `Passive_ChannelResistance_Plus4.tres` resource into the array.

## How it works at Runtime
1. A Cleric casts *Channel Positive Energy*.
2. `CombatMagic.cs` gathers the Undead targets in the AoE and asks for their Will save.
3. Inside `CreatureStats.cs`, `GetWillSave` looks at the incoming `Ability_SO`. It sees "Channel" and "Energy" in the ability's name.
4. It queries the `StatusEffectController` for any active `AgainstChanneling` bonuses.
5. It finds the +4 bonus from the passive effect and adds it to the Undead's total Will save roll against the damage.
6. **AI Parity:** When the AI Cleric is deciding whether to cast Channel Energy, `AIAction_CastGenericAbility` simulates the targets' Will saves. It will detect the +4 resistance and correctly lower the tactical score of Channeling against this specific Undead creature, potentially opting to attack with a mace instead if the math is poor.