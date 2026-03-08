# Freedom of Movement — Godot Inspector Data Setup

Short answer: **yes**.  
This spell can be authored as inspector data using existing generic systems, with **no importer edits** and **no `AbilityMechanicsDatabase` edits**.

This setup is compatible with:
- **player and AI** (standard `Ability_SO` + effect-component pipeline),
- **arena and travel** (shared status/condition checks are used across both),
- existing generic movement/combat handling for `Condition.FreedomOfMovement`.

---

## Why no new script is required

Existing core logic already checks `FreedomOfMovement` in reusable places:
- new grapple attempts against the protected target fail,
- attempts to break a grapple auto-succeed,
- movement-cost penalties from magical movement impedance are ignored,
- underwater slashing/bludgeoning attack penalties are ignored,
- incoming paralyze/entangle/impeded/staggered-style control effects are ignored while active.

Because those checks already exist and are keyed off `Condition.FreedomOfMovement`, this spell is data-authorable.

---

## 1) Ability_SO (`Freedom of Movement`)

Create a new `Ability_SO` and set these values in the inspector.

### Core Identity
- `AbilityName`: `Freedom of Movement`
- `DescriptionForTooltip`: `The target moves and attacks as if unbound by magical restraints. Grapples fail against the target, escape from grapple or pin is effortless, and underwater movement/attacks function normally. This does not grant water breathing.`
- `IsImplemented`: `true`
- `SpellLevel`: `4`
- `SpecialAbilityType`: `Sp`

### Classification
- `Category`: `Spell`
- `School`: `Abjuration`

### Usage & Targeting
- `ActionCost`: `Standard`
- `TargetType`: `SingleAlly`
- `AttackRollType`: `None`
- `EntityType`: `CreaturesOnly`
- `AllowsSpellResistance`: `true`

> If your current project conventions need strict self-cast support from the same entry, keep this as `SingleAlly` and rely on self-target allowance in your targeting rules. If your local config requires an explicit self mode, use your existing equivalent that supports "you or creature touched".

### Range / Save / Duration container
- `Range.Type`: `Touch`
- `SavingThrow.SaveType`: `Will`
- `SavingThrow.EffectOnSuccess`: `Negates`
- `AreaOfEffect.Shape`: project default no-op (or `Burst`, `Range = 0` if your validator expects a value)

> Duration is provided by the status resource (10 min/level => 100 rounds/level).

### Components
- `Components.HasVerbal`: `true`
- `Components.HasSomatic`: `true`
- `Components.HasMaterial`: `true`
- `Components.HasDivineFocus`: `true`

### Effect Components
Add one generic component:
1. `ApplyStatusEffect`
   - `EffectToApply`: `SE_FreedomOfMovement`
   - `DurationIsPerLevel`: `false`
   - `LimitTargetsByLevel`: `false`

---

## 2) StatusEffect_SO (`SE_FreedomOfMovement`)

Create a `StatusEffect_SO` and link it from `ApplyStatusEffect.EffectToApply`.

### Identity
- `EffectName`: `Freedom of Movement`
- `Description`: `A ward of flowing liberty surrounds the subject. Magical restraints lose their hold, grapples cannot keep purchase, and water no longer hinders weapon rhythm or motion. The ward does not provide air beneath the waves.`

### Duration (10 minutes/level)
- `DurationScalesWithLevel`: `true`
- `DurationPerLevel`: `100`
- `DurationInRounds`: `100`

### Core behavior flags
- `ConditionApplied`: `FreedomOfMovement`
- `Tag`: `None`
- `IsMindControlEffect`: `false`

### On-apply cleanup (important for casting while already restrained)
Populate `RemoveConditionsOnApply` with:
- `Grappled`
- `Pinned`
- `Entangled`
- `Impeded`
- `Paralyzed`
- `Staggered`

This keeps the behavior intuitive when the spell is applied after a restraint is already present.

### Everything else
Leave additional fields at defaults unless your project’s balance layer deliberately adds extra interactions.

---

## 3) Pathfinder text mapping to current systems

- **"Move and attack normally despite magical impediments"**  
  Mapped through `ConditionApplied = FreedomOfMovement` plus existing pathfinding/combat checks.

- **"Combat maneuver checks to grapple automatically fail"**  
  Handled by existing grapple resolution checks for `FreedomOfMovement`.

- **"Automatically succeeds to escape grapple or pin"**  
  Handled by existing break-grapple resolution checks for `FreedomOfMovement`.

- **"Move and attack normally underwater"**  
  Handled by existing movement/attack logic that lifts underwater weapon penalties under `FreedomOfMovement`.

- **"Does not grant water breathing"**  
  No water-breathing effect is added in data, so this remains true.

---

## 4) Practical inspector checklist

- [ ] `Ability_SO.Freedom of Movement` created.
- [ ] `TargetType = SingleAlly`, `Range = Touch`.
- [ ] `SavingThrow = Will`, `EffectOnSuccess = Negates`.
- [ ] `ApplyStatusEffect` points to `SE_FreedomOfMovement`.
- [ ] `SE_FreedomOfMovement.ConditionApplied = FreedomOfMovement`.
- [ ] `SE_FreedomOfMovement` set to 100 rounds/level.
- [ ] `SE_FreedomOfMovement.RemoveConditionsOnApply` contains grapple/pin and related restraint conditions.
- [ ] No Water Breathing effect is attached.

---

## Final answer to your question

**Can this be added in the Godot inspector without additional scripts?**  
**Yes**—with the data above, it is fully authorable through existing generic effect/status systems and remains reusable, AI-compatible, player-compatible, and phase-compatible (travel + arena).
