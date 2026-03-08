# Protection from Energy / Protection from Energy, Communal (Godot Inspector Setup)

This project can support both spells through **existing ability resources + one generic effect component**.
No importer changes and no AbilityMechanicsDatabase changes are required.

## 1) New generic effect component to use

Attach `Effect_ApplyProtectionByEnergyType` in each ability's `EffectComponents` list.

- `MinutesPerCasterLevel`: `10`
- `DivideDurationAcrossTargets`:
  - `false` for **Protection from Energy**
  - `true` for **Protection from Energy, Communal**
- `DurationDistributionStepMinutes`: `10`

Then assign one status resource per energy type:

- `AcidProtectionEffect` -> `ProtectionFromEnergy_Acid`
- `ColdProtectionEffect` -> `ProtectionFromEnergy_Cold`
- `ElectricityProtectionEffect` -> `ProtectionFromEnergy_Electricity`
- `FireProtectionEffect` -> `ProtectionFromEnergy_Fire`
- `SonicProtectionEffect` -> `ProtectionFromEnergy_Sonic`

## 2) Ability resource data (base spell)

Because this implementation intentionally avoids the `CommandWord` enum, create one ability resource per energy type (for example: Protection from Energy [Fire], Protection from Energy [Cold], etc.).

Use this on each `Ability_SO` variant for **Protection from Energy**.

- `AbilityName`: `Protection from Energy`
- `Category`: `Spell`
- `School`: `Abjuration`
- `ActionCost`: `Standard`
- `TargetType`: `SingleAlly`
- `EntityType`: `CreaturesOnly`
- `Range.Type`: `Touch`
- `SavingThrow.Type`: `Fortitude`
- `SavingThrow.EffectOnSave`: `Negates`
- `AllowsSpellResistance`: `true`
- `DescriptionForTooltip`: include immunity to chosen energy until pool depleted
- `EffectComponents`: add `Effect_ApplyProtectionByEnergyType` configured as above with `DivideDurationAcrossTargets=false`
- `Effect_ApplyProtectionByEnergyType.EnergyType`: set one value (`Acid`, `Cold`, `Electricity`, `Fire`, or `Sonic`) per ability resource

## 3) Ability resource data (communal spell)

Use this on each `Ability_SO` variant for **Protection from Energy, Communal**.

- Same as base spell except:
  - `AbilityName`: `Protection from Energy, Communal`
  - `TargetType`: `Area_AlliesOnly` (or your project’s multi-touch equivalent)
  - `DivideDurationAcrossTargets`: `true` on the effect component

## 4) StatusEffect resource data (one per energy type)

Create five `StatusEffect_SO` resources. All use the same values except damage type string.

Common fields:

- `EffectName`: `Protection from Energy (TYPE)`
- `Description`: “Absorbs TYPE damage before HP is lost. Ends when pool is exhausted.”
- `DurationInRounds`: `1` (placeholder; runtime replaces this from caster level)
- `AbsorbDamageTypes`: single entry for that energy type
- `AbsorptionPoolBase`: `0`
- `AbsorptionPoolScalesWithCasterLevel`: `true`
- `AbsorptionPointsPerCasterLevel`: `12`
- `AbsorptionPoolMax`: `120`

Type-specific `AbsorbDamageTypes` values:

- Acid version: `Acid`
- Cold version: `Cold`
- Electricity version: `Electricity`
- Fire version: `Fire`
- Sonic version: `Sonic`

## 5) Why this works for AI/player + travel/arena

- Uses the same `Ability_SO` + `AbilityEffectComponent` pipeline already used by both AI and player.
- Damage absorption resolves in `CreatureStats.TakeDamage`, so it applies anywhere damage is processed (arena and travel).
- Communal behavior is data-driven via a boolean on the same effect component.
