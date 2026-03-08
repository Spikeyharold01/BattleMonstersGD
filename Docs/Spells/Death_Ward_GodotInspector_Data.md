# Death Ward — Godot Inspector Data Setup

Short answer: **almost**.  
Most of *Death Ward* can be authored as inspector data immediately, but **full parity** (energy-drain immunity + suppressing corruption resistance penalties) needs a small, generic, data-centric extension to status-effect handling.

This implementation:
- does **not** touch importer scripts,
- does **not** edit `AbilityMechanicsDatabase`,
- remains reusable for future protective effects,
- works for **player and AI**, and in **travel and arena** because it uses standard `Ability_SO` + status pipelines.

---

## Why a tiny generic extension is needed

The spell requires all of the following at once:
1. `+4 morale` on saves against death effects,
2. protection against negative energy,
3. immunity to energy drain,
4. temporary removal of penalties from already-present negative levels (represented here by corruption resistance penalties).

Items (1) and part of (2) are already data-authored through conditional saves.  
Items (3) and (4) required generic status fields so data can grant temporary immunities and temporarily suppress corruption resistance penalty application.

---

## 1) Ability_SO (`Death Ward`)

Create a new `Ability_SO` resource and set these inspector values.

### Core Info
- `AbilityName`: `Death Ward`
- `DescriptionForTooltip`: `A living creature touched is protected from death magic and negative energy. It gains a +4 morale bonus on saves against death effects, can attempt a save even when one is normally not allowed, and is immune to energy drain and negative energy effects for the duration.`
- `IsImplemented`: `true`
- `SpellLevel`: `4`
- `SpecialAbilityType`: `Sp`

### Classification
- `Category`: `Spell`
- `School`: `Necromancy`

### Usage & Targeting
- `ActionCost`: `Standard`
- `TargetType`: `SingleAlly` *(for “living creature touched” as a protective touch spell)*
- `AttackRollType`: `None`
- `EntityType`: `CreaturesOnly`
- `AllowsSpellResistance`: `true`

### Range
- `Range.Type`: `Touch`

### Area / Duration / Save
- `AreaOfEffect.Shape`: `Burst` *(or project default no-op shape if required by your resource validator)*
- `AreaOfEffect.Range`: `0`
- `AuraEffectDuration.BaseRounds`: `0`
- `AuraEffectDuration.DiceCount`: `0`
- `AuraEffectDuration.DieSides`: `0`
- `SavingThrow.SaveType`: `Will`
- `SavingThrow.EffectOnSuccess`: `Negates`

> Duration is driven by status data (1 minute/level).

### Casting Components
- `Components.HasVerbal`: `true`
- `Components.HasSomatic`: `true`
- `Components.HasDivineFocus`: `true`

### Mythic
- `IsMythicCapable`: `false` *(unless you have a mythic variant authored separately)*

### Effect Components
Add one standard effect component:

1. `ApplyStatusEffect`
   - `EffectToApply`: `SE_DeathWard`
   - `DurationIsPerLevel`: `false`
   - `LimitTargetsByLevel`: `false`

---

## 2) StatusEffect_SO (`SE_DeathWard`)

Create a status resource and reference it from `ApplyStatusEffect.EffectToApply`.

### Identity
- `EffectName`: `Death Ward`
- `Description`: `A sacred barrier steadies the target against life-ending magic. The ward grants +4 morale on saves against death effects and negative energy, prevents energy drain, and temporarily silences resistance penalties from existing corruption-like drain burdens.`

### Duration
- `DurationScalesWithLevel`: `true`
- `DurationPerLevel`: `10`
- `DurationInRounds`: `10`

### Tags and behavior
- `Tag`: `None`
- `IsMindControlEffect`: `false`
- `ConditionApplied`: `None`

### Conditional save adjustments
Add two `ConditionalSaveBonus` entries:
1. `Condition`: `AgainstDeathEffects`, `ModifierValue`: `4`, `BonusType`: `Morale`
2. `Condition`: `AgainstNegativeEnergy`, `ModifierValue`: `4`, `BonusType`: `Morale`

### Granted immunities (new generic status data)
Set `GrantedImmunities` to include:
- `EnergyDrain`

### Corruption/negative-level penalty suppression (new generic status data)
- `SuppressCorruptionResistancePenalty`: `true`

This mirrors the tabletop line that existing negative levels remain but their penalties are suspended while the ward lasts.

---

## 3) What the generic code extension now enables

The supporting code is generic and reusable (not Death-Ward specific):
- Any status can now grant temporary immunities through `GrantedImmunities`.
- Any status can temporarily suppress corruption resistance penalty through `SuppressCorruptionResistancePenalty`.
- Conditional save routing now also recognizes negative-energy style sources when applicable.

So future effects like anti-poison wards, anti-fatigue blessings, or sanctified anti-drain auras can use the same pattern with only data changes.

---

## 4) Practical inspector checklist

- [ ] `Ability_SO.Death Ward` created.
- [ ] `TargetType = SingleAlly`, `Range.Type = Touch`.
- [ ] `SavingThrow = Will`, `EffectOnSuccess = Negates`.
- [ ] `ApplyStatusEffect` points to `SE_DeathWard`.
- [ ] `SE_DeathWard` duration scales at 10 rounds/level.
- [ ] `SE_DeathWard` has `+4 morale` vs `AgainstDeathEffects`.
- [ ] `SE_DeathWard` has `+4 morale` vs `AgainstNegativeEnergy`.
- [ ] `SE_DeathWard.GrantedImmunities` includes `EnergyDrain`.
- [ ] `SE_DeathWard.SuppressCorruptionResistancePenalty = true`.

---

## 5) Final answer to your question

**Can this be added in the inspector without additional scripts?**
- **Partially yes** for save bonuses.
- **Full rules intent** needed a minimal generic extension, now provided in reusable status/effect core.

So the spell is now practical to author in Godot inspector data while keeping implementation data-centric, AI-compatible, player-compatible, and phase-agnostic (travel + arena).
