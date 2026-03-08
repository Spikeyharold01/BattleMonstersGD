# Bane — Godot Inspector Data Setup

This setup lets **Bane** be authored directly in Godot inspector data, with no importer script edits and no AbilityMechanicsDatabase changes.

## 1) Ability_SO (`Bane`)

Set these fields on a new `Ability_SO` resource:

- **Core Info**
  - `AbilityName`: `Bane`
  - `DescriptionForTooltip`: `Bane fills enemies with fear and doubt, imposing penalties on attacks and fear-related saves.`
  - `IsImplemented`: `true`
  - `SpellLevel`: `1`
  - `SpecialAbilityType`: `Sp`

- **Classification**
  - `Category`: `Spell`
  - `School`: `Enchantment`

- **Usage & Targeting**
  - `ActionCost`: `Standard`
  - `TargetType`: `Area_EnemiesOnly`
  - `AttackRollType`: `None`
  - `EntityType`: `CreaturesOnly`
  - `AllowsSpellResistance`: `true`

- **Range**
  - `Range.Type`: `Custom`
  - `Range.CustomRangeInFeet`: `50`

- **Area of Effect**
  - `AreaOfEffect.Shape`: `Burst`
  - `AreaOfEffect.Range`: `50`

- **Duration**
  - `AuraEffectDuration.BaseRounds`: `0`
  - `AuraEffectDuration.DiceCount`: `0`
  - `AuraEffectDuration.DieSides`: `0`
  - *(Duration is driven by status data so it can cleanly scale 1 minute per caster level.)*

- **Saving Throw**
  - `SavingThrow.SaveType`: `Will`
  - `SavingThrow.BaseDC`: `10` *(placeholder if dynamic DC is used)*
  - `SavingThrow.IsDynamicDC`: `true`
  - `SavingThrow.DynamicDCStat`: `Wisdom`
  - `SavingThrow.EffectOnSuccess`: `Negates`

- **Casting Components**
  - `Components.HasVerbal`: `true`
  - `Components.HasSomatic`: `true`
  - `Components.HasDivineFocus`: `true`

- **Mythic**
  - `IsMythicCapable`: `true`

- **Effect Components**
  - Add one `ApplyStatusEffect` component
    - `EffectToApply`: `SE_Bane` (the status resource below)
    - `DurationIsPerLevel`: `false`
    - `LimitTargetsByLevel`: `false`

- **Mythic Components**
  - Add one `ForceLowerRollOnceEffect`
    - `OnlyTargetsThatFailedSave`: `true`
    - `AdditionalTargetFilter`: *(optional, usually none)*

## 2) StatusEffect_SO (`SE_Bane`)

Create a status resource and assign it to `ApplyStatusEffect`:

- `EffectName`: `Bane`
- `Description`: `The creature is overcome with fear and doubt.`

### Duration
- `DurationScalesWithLevel`: `true`
- `DurationPerLevel`: `10`
- `DurationInRounds`: `10` *(fallback value when no caster is present)*

### Tags and rules
- `Tag`: `Fear`
- `IsMindControlEffect`: `true`

### Modifications
Add one `StatModification` entry:
- `StatToModify`: `AttackRoll`
- `ModifierValue`: `-1`
- `BonusType`: `Morale`

### Conditional save adjustment
Add one `ConditionalSaveBonus` entry:
- `Condition`: `AgainstFear`
- `ModifierValue`: `-1`
- `BonusType`: `Morale`

## 3) Mythic text mapping

For mythic tooltip/support text, describe:

- `The –1 penalty applies on attack rolls, weapon damage rolls, and all saving throws.`
- `Each affected creature must roll its next attack roll or saving throw twice and take the lower result.`

If you want to fully enforce the mythic expanded penalties in data, duplicate `SE_Bane` as `SE_Bane_Mythic` and add:

- `StatModification`: `MeleeDamage`, `-1`, `Morale`
- `StatModification`: `RangedDamage`, `-1`, `Morale`
- `StatModification`: `FortitudeSave`, `-1`, `Morale`
- `StatModification`: `ReflexSave`, `-1`, `Morale`
- `StatModification`: `WillSave`, `-1`, `Morale`

Then apply `SE_Bane_Mythic` using an additional mythic status component if desired.

## 4) Bless counter/dispel note

Bane's "counters and dispels bless" can be authored with existing generic dispel data:

- Add `Effect_Dispel` component (or mythic component variant if desired).
- Configure:
  - `AffectAllTargetsInAoE`: `true`
  - `EffectNameContains`: `Bless`

This avoids special-case scripting and keeps behavior data-driven.

## 5) AI + Player + Arena + Travel usability

- **AI** can score and use the ability because it is a standard `Ability_SO` with standard area targeting and status application.
- **Player** can use it through normal spell selection (same path as other area spells).
- **Arena and Travel** both read from shared creature ability data, so this spell works in both phases without special phase scripting.
