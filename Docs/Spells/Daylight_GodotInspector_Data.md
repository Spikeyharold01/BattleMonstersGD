# Daylight — Godot Inspector Data Setup

Yes — **Daylight can be added in Godot inspector data without adding or changing importer scripts and without editing `AbilityMechanicsDatabase`**.

This setup reuses existing generic light/darkness systems so it is usable by:
- **Player-controlled casters**
- **AI-controlled casters**
- **Travel phase**
- **Arena phase**

---

## 1) Ability_SO (`Daylight`)

Create a new `Ability_SO` resource and set these inspector values.

### Core Info
- `AbilityName`: `Daylight`
- `DescriptionForTooltip`: `Touched object radiates bright light in a 60-foot radius and raises lighting by one step for an additional 60 feet. Counters and dispels darkness of equal or lower spell level.`
- `IsImplemented`: `true`
- `SpellLevel`: `3`
- `SpecialAbilityType`: `Sp`

### Classification
- `Category`: `Spell`
- `School`: `Evocation`

### Usage & Targeting
- `ActionCost`: `Standard`
- `TargetType`: `SingleTarget`
- `AttackRollType`: `None`
- `EntityType`: `ObjectsOnly` *(or your project’s closest touch-object target option)*
- `AllowsSpellResistance`: `false`

### Range
- `Range.Type`: `Touch`

### Area / Duration / Save
- `AreaOfEffect.Shape`: `None`
- `AuraEffectDuration.BaseRounds`: `0`
- `AuraEffectDuration.DiceCount`: `0`
- `AuraEffectDuration.DieSides`: `0`
- `SavingThrow.SaveType`: `None`
- `SavingThrow.EffectOnSuccess`: `None`

> Duration is driven by `LightAndDarknessInfo.DurationInMinutes` and caster-level scaling in runtime light-source logic.

### Casting Components
- `Components.HasVerbal`: `true`
- `Components.HasSomatic`: `true`

### Mythic
- `IsMythicCapable`: `true`

### Effect Components (standard)
Add one standard effect component:

1. `CreateLightSourceEffect`
   - `LightData`: `LAD_Daylight_60_120`
   - `SpellLevel`: `3`
   - `IsMythicEffect`: `false`

### Mythic Components
Add one mythic effect component by reusing the same generic effect:

1. `CreateLightSourceEffect` *(mythic instance)*
   - `LightData`: `LAD_Daylight_Mythic_60_120`
   - `SpellLevel`: `3`
   - `IsMythicEffect`: `true`

This keeps the spell entirely data-authored while preserving mythic identity in shared light-resolution logic.

---

## 2) LightAndDarknessInfo resource data

Create these resources in your light/darkness data folder.

## A) `LAD_Daylight_60_120` (standard)
- `Radius`: `60`
- `OuterRadius`: `120`
- `IntensityChange`: `2`
- `OuterIntensityChange`: `1`
- `IsMagical`: `true`
- `IsSupernaturalDarkness`: `false`
- `DurationInMinutes`: `10`

Why this mapping works:
- The inner 60-foot zone is set to bright-light strength.
- The additional 60 feet is one step lower, matching the one-step increase clause.
- Equal-or-lower darkness removal is already handled by spell-level-aware light/darkness conflict and dispel checks in existing runtime behavior.

## B) `LAD_Daylight_Mythic_60_120` (mythic)
- `Radius`: `60`
- `OuterRadius`: `120`
- `IntensityChange`: `3`
- `OuterIntensityChange`: `2`
- `IsMagical`: `true`
- `IsSupernaturalDarkness`: `false`
- `DurationInMinutes`: `10`

Why this mapping works:
- The mythic inner zone remains at full bright-light force.
- The mythic outer zone is pushed to at least normal light pressure under most ambient conditions.
- Mythic identity is preserved by the `IsMythicEffect` flag on the effect component, allowing systems that inspect mythic daylight to react consistently.

---

## 3) Rules coverage notes

This data-only setup covers the central mechanical behavior:
- **Touch an object and make it a light emitter** via `CreateLightSourceEffect` target-object placement.
- **60-foot bright radius plus 60-foot one-step uplift** via `Radius` + `OuterRadius` and inner/outer intensity values.
- **10 min/level** via `DurationInMinutes` and caster-level scaling in light source runtime.
- **Counter/dispel equal or lower darkness** via existing spell-level comparison in light-vs-darkness interactions.
- **Works for AI and player in travel + arena** because it is a normal `Ability_SO` using shared effect execution paths.

Important fidelity note:
- The line about placing the lit item inside a light-proof covering is usually represented by object interaction/visibility handling. No special Daylight-only script is needed.

---

## 4) Mythic add-ons (what is already covered vs optional)

Already covered by existing shared logic:
- **Creatures that suffer bright-light penalties get stronger penalties in mythic daylight** where your existing combat checks use the mythic-daylight query.

Optional data follow-up (only if you want full mythic text parity now):
- Add a generic, reusable proximity-status pipeline that grants:
  - `+2 circumstance` to `Perception`
  - `+2 circumstance` on saves against fear
  for creatures in mythic Daylight bright area.

This should be implemented as a **generic aura/status pattern**, not a Daylight-specific script, so it remains reusable by future spells and environmental effects.

---

## 5) Practical inspector checklist

- [ ] `Ability_SO.Daylight` created and set to `SpellLevel = 3`.
- [ ] Standard `CreateLightSourceEffect` added with `LAD_Daylight_60_120`.
- [ ] Mythic `CreateLightSourceEffect` added with `LAD_Daylight_Mythic_60_120`.
- [ ] Both effect entries use `SpellLevel = 3`.
- [ ] Standard effect has `IsMythicEffect = false`.
- [ ] Mythic effect has `IsMythicEffect = true`.
- [ ] `Range.Type = Touch` and `AllowsSpellResistance = false`.
- [ ] Light resources use `DurationInMinutes = 10`.

---

## 6) Can this be added without new scripts?

**Yes — for core Daylight behavior, absolutely.**

The standard and mythic light-field behavior can be authored with existing inspector data by reusing `CreateLightSourceEffect` + `LightAndDarknessInfo` resources.

If you want full mythic bonus coverage for Perception and fear-resistance in the bright zone, that is best added as a **generic data-centric aura/status extension** rather than a bespoke Daylight script.
