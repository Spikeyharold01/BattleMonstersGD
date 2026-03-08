# Endure Elements - Godot Inspector Setup

This spell can be authored entirely through inspector data using the existing `ApplyStatusEffect` component.
No importer changes are required.
No ability-mechanics database changes are required.

---

## 1) Status Effect Resource

Create a new `StatusEffect_SO` resource:

- **Resource path suggestion:** `res://Data/StatusEffects/EndureElements_Protection.tres`
- **EffectName:** `Endure Elements`
- **Description:** `This ward keeps the creature comfortable in harsh heat and cold. It protects the creature and worn gear from climate harm, but not direct energy attacks or other hazards like smoke.`
- **DurationInRounds:** `14400` *(24 hours at 10 rounds per minute)*
- **ConditionApplied:** `None`
- **Tag:** `None`

### Environmental Protection section
- **ProtectsFromEnvironmentalHeat:** `true`
- **ProtectsFromEnvironmentalCold:** `true`
- **IgnoreSnowAndIceMovementPenalty:** `false` *(base spell does not grant this)*
- **IgnorePrecipitationCombatPenalties:** `false` *(base spell does not grant this)*
- **WindSeverityReductionSteps:** `0` *(base spell does not weaken wind)*

### Optional mythic override values
If you create a mythic version of this status, use these values:

- **EffectName:** `Endure Elements (Mythic)`
- **IgnoreSnowAndIceMovementPenalty:** `true`
- **IgnorePrecipitationCombatPenalties:** `true`
- **WindSeverityReductionSteps:** `1`

*(If your game has temporary elemental resistance status resources, pair mythic with those. If not, leave resistances out to avoid adding non-generic logic.)*

---

## 2) Base Ability Resource (`Endure Elements`)

Create a new `Ability_SO` resource:

- **Resource path suggestion:** `res://Data/Abilities/Spells/EndureElements.tres`
- **AbilityName:** `Endure Elements`
- **Category:** `Spell`
- **School:** `Abjuration`
- **SpellLevel:** `1`
- **ActionCost:** `Standard`
- **TargetType:** `SingleAlly`
- **EntityType:** `CreaturesOnly`
- **Range.Type:** `Touch`
- **AllowsSpellResistance:** `true`
- **SavingThrow.SaveType:** `Will`
- **SavingThrow.EffectOnSuccess:** `Negates`
- **Components.HasVerbal:** `true`
- **Components.HasSomatic:** `true`
- **DescriptionForTooltip:**
  `Target creature is protected from environmental heat and cold for 24 hours. This does not grant fire or cold damage resistance and does not protect from smoke, suffocation, or similar hazards.`

### EffectComponents
Add one component:

1. `ApplyStatusEffect`
   - **EffectToApply:** `EndureElements_Protection.tres`
   - **DurationIsPerLevel:** `false`
   - **LimitTargetsByLevel:** `false`
   - **TargetFilter:** *(optional friendly/living filter if your project uses one)*

---

## 3) Communal Ability Resource (`Endure Elements, Communal`)

Create a second `Ability_SO` resource:

- **AbilityName:** `Endure Elements, Communal`
- **School:** `Abjuration`
- **SpellLevel:** `2` *(or class-specific data outside this resource if your pipeline handles per-list levels elsewhere)*
- **ActionCost:** `Standard`
- **TargetType:** `Area_AlliesOnly` *(or your multi-touch equivalent)*
- **Range.Type:** `Touch`
- **SavingThrow.SaveType:** `Will`
- **SavingThrow.EffectOnSuccess:** `Negates`

### EffectComponents
Add one component:

1. `ApplyStatusEffect`
   - **EffectToApply:** `EndureElements_Protection.tres`
   - **DurationIsPerLevel:** `true`
   - **LimitTargetsByLevel:** `false`

Then set on the status resource:
- **DurationIsDivided:** `true`

This gives shared duration behavior where total time is split among touched allies.

---

## 4) AI and Phase Usage Notes

This setup is compatible with both AI and player because it uses the same standard ability execution path and standard status application component.
It also works in both travel and arena phases because the environmental hazard checks now read generic status protection flags rather than spell-specific code.
