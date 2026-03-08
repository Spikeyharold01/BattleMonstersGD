# Darkness — Godot Inspector Data Setup

Yes — **Darkness can be added through inspector-authored data using existing effect components**, without touching importer scripts and without changing `AbilityMechanicsDatabase`.

This setup uses the already generic light/darkness pipeline so the spell remains usable by:
- **Player-controlled casters**
- **AI-controlled casters**
- **Travel phase**
- **Arena phase**

---

## 1) Ability_SO (`Darkness`)

Create a new `Ability_SO` resource and set the following fields.

### Core Info
- `AbilityName`: `Darkness`
- `DescriptionForTooltip`: `Touched object radiates darkness in a 20-foot radius, lowering illumination by one step. Nonmagical light cannot brighten this area. Equal-or-lower-level light is countered or dispelled.`
- `IsImplemented`: `true`
- `SpellLevel`: `2`
- `SpecialAbilityType`: `Sp`

### Classification
- `Category`: `Spell`
- `School`: `Evocation`

### Usage & Targeting
- `ActionCost`: `Standard`
- `TargetType`: `Single`
- `AttackRollType`: `None`
- `EntityType`: `Any`
- `AllowsSpellResistance`: `false`

### Range
- `Range.Type`: `Touch`
- `Range.CustomRangeInFeet`: `0`

### Area of Effect
- `AreaOfEffect.Shape`: `Burst`
- `AreaOfEffect.Range`: `20`

> The gameplay light radius is driven by the linked `LightAndDarknessInfo` resource below. Keep this area value aligned so AI and tooltip systems evaluate the spell correctly.

### Duration
- `AuraEffectDuration.BaseRounds`: `0`
- `AuraEffectDuration.DiceCount`: `0`
- `AuraEffectDuration.DieSides`: `0`

> Duration is handled by `LightAndDarknessInfo.DurationInMinutes` and scales with caster level via existing light-source runtime logic.

### Saving Throw
- `SavingThrow.SaveType`: `None`
- `SavingThrow.EffectOnSuccess`: `None`

### Casting Components
- `Components.HasVerbal`: `true`
- `Components.HasSomatic`: `false` *(or true if your table conventions still represent touch spells as somatic; rules text is V, M/DF)*
- `Components.HasMaterial`: `true`
- `Components.HasDivineFocus`: `true`

### Mythic
- `IsMythicCapable`: `true`

### Effect Components (non-mythic)
Add one effect component:

1. `CreateLightSourceEffect`
   - `LightData`: `LAD_Darkness_20ft`
   - `SpellLevel`: `2`
   - `IsMythicEffect`: `false`
   - `AiTacticalTag`: *(optional, assign your darkness/vision-denial tactical tag if available)*

### Mythic Components
Add one mythic light component by reusing the same effect type with mythic flag enabled in your mythic effect slot:

1. `CreateLightSourceEffect` (mythic instance)
   - `LightData`: `LAD_Darkness_Mythic_20ft`
   - `SpellLevel`: `2`
   - `IsMythicEffect`: `true`

---

## 2) LightAndDarknessInfo resource data

Create the following resources under your light/darkness data folder.

## A) `LAD_Darkness_20ft` (standard)
- `Radius`: `20`
- `OuterRadius`: `0`
- `OuterIntensityChange`: `0`
- `IsMagical`: `true`
- `IntensityChange`: `-1`
- `IsSupernaturalDarkness`: `false`
- `DurationInMinutes`: `1`

**Why this matches spell text:**
- `-1` lowers illumination by one step.
- 20-foot radius matches the spell area.
- 1 minute per level is represented by duration minutes + caster-level scaling in the runtime light source controller.

## B) `LAD_Darkness_Mythic_20ft` (mythic)
- `Radius`: `20`
- `OuterRadius`: `0`
- `OuterIntensityChange`: `0`
- `IsMagical`: `true`
- `IntensityChange`: `-3`
- `IsSupernaturalDarkness`: `true`
- `DurationInMinutes`: `1`

**Why this maps well for mythic behavior:**
- Using a deeper negative shift forces effective light toward full darkness regardless of most ambient conditions.
- `IsSupernaturalDarkness: true` allows systems that check supernatural darkness to treat vision more strictly.
- Mythic flag on the effect instance keeps this source identifiable as mythic in shared light resolution.

---

## 3) Rule mapping notes (what this setup already covers)

- **Object touched emits darkness:** handled by target-object placement in `CreateLightSourceEffect`.
- **20-foot radius:** handled by `LightAndDarknessInfo.Radius`.
- **1 step light reduction (standard):** handled by `IntensityChange = -1`.
- **No effect in already-dark area:** naturally no lower state exists beneath darkness.
- **Does not stack with itself:** light/darkness conflict resolution uses highest relevant spell influence, not additive stacking.
- **Counter/dispel equal-or-lower light:** existing dispel comparison logic in light source effect handles spell-level-based removal.
- **Blocked by lightproof covering:** already representable in encounter logic by removing line influence from covered object locations as those object interactions are modeled.

---

## 4) Mythic rider coverage and current data-only boundary

This data-only setup supports the **core mythic darkness lighting behavior**.

For the additional mythic line **“creatures in the area take –2 on saves against fear”**, you have two options:

1. **Ship now with no new scripts** (recommended for strict no-code requirement).
   - You get full darkness gameplay behavior immediately.

2. **Add later as a generic, data-centric aura status mechanic** if you want exact ongoing area-locked fear-save penalty.
   - Keep it generic (for any zone-based conditional save modifier), not darkness-specific.
   - Do **not** add darkness-special-case scripting.

---

## 5) Optional StatusEffect data (for future generic aura pipeline)

If/when your generic aura-status system supports persistent in-area application/removal, prepare this status now:

### `SE_MythicDarkness_FearPenalty`
- `EffectName`: `Mythic Darkness Fear Penalty`
- `Description`: `Saves against fear are harder while inside mythic darkness.`
- `DurationScalesWithLevel`: `false`
- `DurationInRounds`: `1` *(or your aura refresh tick duration)*
- `Tag`: `Fear`
- `ConditionalSaveBonus` entry:
  - `Condition`: `AgainstFear`
  - `ModifierValue`: `-2`
  - `BonusType`: `Untyped`

This keeps the content fully data-authored and reusable by other fear-aura designs.

---

## 6) Practical inspector checklist

- [ ] `Ability_SO.Darkness` created.
- [ ] `Range.Type` is `Touch`.
- [ ] `CreateLightSourceEffect` added to standard effects with `SpellLevel = 2`.
- [ ] `LightData` on standard effect points to `LAD_Darkness_20ft`.
- [ ] Mythic effect instance added with `IsMythicEffect = true`.
- [ ] Mythic `LightData` points to `LAD_Darkness_Mythic_20ft`.
- [ ] `IsMythicCapable` enabled.
- [ ] Tooltip text reflects concealment expectations for dim/dark areas.

---

## 7) Final answer to your question

**Yes.** Darkness can be implemented in Godot inspector data with existing generic systems, without importer changes and without `AbilityMechanicsDatabase` edits.

For strict RAW mythic fear-save penalty as a moving in-area rider, treat that as a **future generic aura-status enhancement**, not a darkness-specific script.
