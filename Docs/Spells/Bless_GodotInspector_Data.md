# Bless — Godot Inspector Data Setup

This setup allows **Bless** to be authored directly in Godot inspector data with **no importer changes** and **no AbilityMechanicsDatabase edits**.

It uses existing generic effect components so the spell is usable by:
- **Player-controlled casters**
- **AI-controlled casters**
- **Travel phase**
- **Arena phase**

---

## 1) Ability_SO (`Bless`)

Create a new `Ability_SO` resource and set the following inspector values.

### Core Info
- `AbilityName`: `Bless`
- `DescriptionForTooltip`: `Bless fills allies with courage, granting a morale bonus on attack rolls and saving throws against fear effects. It counters and dispels bane.`
- `IsImplemented`: `true`
- `SpellLevel`: `1`
- `SpecialAbilityType`: `Sp`

### Classification
- `Category`: `Spell`
- `School`: `Enchantment`

### Usage & Targeting
- `ActionCost`: `Standard`
- `TargetType`: `Area_AlliesOnly`
- `AttackRollType`: `None`
- `EntityType`: `CreaturesOnly`
- `AllowsSpellResistance`: `true`

### Range
- `Range.Type`: `Custom`
- `Range.CustomRangeInFeet`: `50`

### Area of Effect
- `AreaOfEffect.Shape`: `Burst`
- `AreaOfEffect.Range`: `50`

### Duration
- `AuraEffectDuration.BaseRounds`: `0`
- `AuraEffectDuration.DiceCount`: `0`
- `AuraEffectDuration.DieSides`: `0`

> Duration is handled by the status effect so it scales naturally to 1 minute per caster level.

### Saving Throw
- `SavingThrow.SaveType`: `None`
- `SavingThrow.EffectOnSuccess`: `None`

### Casting Components
- `Components.HasVerbal`: `true`
- `Components.HasSomatic`: `true`
- `Components.HasDivineFocus`: `true`

### Mythic
- `IsMythicCapable`: `true`

### Effect Components
Add two standard (non-mythic) effect components in this order:

1. `ApplyStatusEffect`
   - `EffectToApply`: `SE_Bless`
   - `DurationIsPerLevel`: `false`
   - `LimitTargetsByLevel`: `false`

2. `Effect_Dispel`
   - `AffectAllTargetsInAoE`: `true`
   - `IncludeCaster`: `false`
   - `EffectNameContains`: `Bane`
   - `AutoSuccess`: `true`

> This handles the “Bless counters and dispels bane” clause using generic data-driven dispel logic.

### Mythic Components
Add one mythic effect component:

1. `RollTwiceEffect`
   - No extra fields required.

> This provides the once-per-duration roll-twice benefit in existing mythic systems.

---

## 2) StatusEffect_SO (`SE_Bless`)

Create a status resource and assign it in `ApplyStatusEffect.EffectToApply`.

### Identity
- `EffectName`: `Bless`
- `Description`: `The creature is inspired with courage, gaining a morale bonus on attacks and saving throws against fear.`

### Duration
- `DurationScalesWithLevel`: `true`
- `DurationPerLevel`: `10`
- `DurationInRounds`: `10`

### Tags and behavior
- `Tag`: `MindAffecting`
- `IsMindControlEffect`: `false`

### Modifications
Add one `StatModification` entry:
- `StatToModify`: `AttackRoll`
- `ModifierValue`: `1`
- `BonusType`: `Morale`

### Conditional save adjustment
Add one `ConditionalSaveBonus` entry:
- `Condition`: `AgainstFear`
- `ModifierValue`: `1`
- `BonusType`: `Morale`

---

## 3) Mythic variant data (`SE_Bless_Mythic`, optional but recommended)

To match the mythic text that broadens the morale bonus to weapon damage and all saving throws, create a second status effect.

### Identity
- `EffectName`: `Bless (Mythic)`
- `Description`: `The morale bonus now applies to attack rolls, weapon damage rolls, and all saving throws.`

### Duration
- `DurationScalesWithLevel`: `true`
- `DurationPerLevel`: `10`
- `DurationInRounds`: `10`

### Tags
- `Tag`: `MindAffecting`

### Modifications
Add these `StatModification` entries (all `BonusType: Morale`):
1. `AttackRoll`, `+1`
2. `MeleeDamage`, `+1`
3. `RangedDamage`, `+1`
4. `FortitudeSave`, `+1`
5. `ReflexSave`, `+1`
6. `WillSave`, `+1`

### How to apply mythic status
If your current cast flow uses the same status for normal and mythic casts, keep `SE_Bless` for baseline rules and add a mythic-only status application component if your pipeline supports it.

If your build does not yet expose mythic-only status swap in data, you can still ship baseline Bless immediately and keep mythic status expansion as a follow-up data pass.

---

## 4) Practical inspector checklist

Use this checklist to reduce setup mistakes:

- [ ] `Ability_SO.Bless` created.
- [ ] `TargetType` set to `Area_AlliesOnly`.
- [ ] Range and burst both set to `50` feet.
- [ ] `ApplyStatusEffect` points to `SE_Bless`.
- [ ] `Effect_Dispel` filters by `EffectNameContains = Bane`.
- [ ] `RollTwiceEffect` added under `MythicComponents`.
- [ ] `SE_Bless` includes +1 morale attack and +1 morale save vs fear.
- [ ] Optional: `SE_Bless_Mythic` created for full mythic bonus coverage.

---

## 5) Can this be added without new scripts?

**Yes.** Bless can be implemented entirely through existing inspector-authored data using:
- `ApplyStatusEffect` for the ally buff,
- `Effect_Dispel` for bane counter/dispel behavior,
- `RollTwiceEffect` for the mythic once-per-duration roll advantage.

No importer edits and no `AbilityMechanicsDatabase` changes are required.
