# Adaro (CR 3) — Godot Inspector Setup (Importer-Parity, No Guesswork)

This document is a **new setup guide** for Adaro and follows the **actual code paths already implemented** in:
- `Editor/Importers/CreatureImporter.cs`
- `Editor/Importers/ImportUtils.cs`
- travel spawn runtime (`BiomeTravelDefinition` + `TravelEncounterSpawner`)
- runtime sound derivation (`SoundProfileFactory`)

If a setting is not written by importer code, this guide says so explicitly.

---

## 0) Stat block source used

- **Adaro CR 3**, XP 800
- **NE Medium monstrous humanoid (aquatic)**
- Init +3
- Senses: blindsense 30 ft., darkvision 60 ft., low-light vision, keen scent; Perception +8
- AC 15, touch 13, flat-footed 12 (+3 Dex, +2 natural)
- hp 30 (4d10+8)
- Fort +3, Ref +7, Will +5
- Speed 10 ft., swim 50 ft.
- Melee spear +8 (1d8+4/x3 plus poison), bite +2 (1d6+3)
- Ranged spear +8 (1d8+3/x3)
- Special Attacks: rain frenzy
- Str 16, Dex 17, Con 14, Int 10, Wis 13, Cha 13
- BAB +4; CMB +7; CMD 20
- Feats: Deadly Aim, Weapon Focus (spear)
- Skills: Intimidate +8, Perception +8, Stealth +10, Swim +18
- Languages: Aquan, Common; speak with sharks
- SQ: amphibious, poison use
- Environment: warm oceans
- Organization: solitary, hunting party (2-6), tribe (7-12)
- Treasure: standard (3 spears, other treasure)

Special abilities:
- Poison (Ex): Nettlefin toxin (Fort DC 15, 1/min for 4 min, paralyzed 1 min, cure 2 consecutive saves)
- Poison Use (Ex)
- Rain Frenzy (Su)
- Speak with Sharks (Su), telepathy 100 ft simple concepts

---

## 1) Create the creature template asset

Create `CreatureTemplate_SO`:
- `res://Data/Creatures/Adaro.tres`

> Why this path/name: importer uses `res://Data/Creatures/{safeName}.tres` where safeName is creature title (`Adaro`).

---

## 2) Identity and ecology fields (exact importer-parity)

## 2.1 Identity
Set:
- `CreatureName = Adaro`
- `ChallengeRating = 3`
- `XpAward = 800`
- `Alignment = NE`
- `Race = ""` (unless your source row has a race string)
- `Type = MonstrousHumanoid`
- `Size = Medium`
- `SubTypes` includes `aquatic`

## 2.2 Number appearing (`AverageGroupSize`) — exact importer rule
Importer sets:
- `AverageGroupSize = ParseOrganization(ecology/organization)`.

`ParseOrganization` behavior:
- detects `solitary` -> mean 1
- `pair` -> mean 2
- `(min-max)` -> mean `(min+max)/2`
- picks the **largest mean** and rounds.

For Adaro organization:
- solitary => 1
- hunting party (2–6) => 4
- tribe (7–12) => 9.5
- max mean 9.5 => rounded => **10**

Set:
- `AverageGroupSize = 10`

## 2.3 Environment mapping (`NaturalEnvironmentProperties`) — exact importer rule
Importer uses `TranslateEnvironmentToProperties(ecology/environment)`.

For `warm oceans`:
- contains `ocean` => `Aquatic`
- contains `warm` => `Warm`
- does **not** auto-add `Terrestrial` when aquatic matched.

Set:
- `NaturalEnvironmentProperties = [Aquatic, Warm]`

## 2.4 Availability toggles
Importer does **not** overwrite these in `PopulateIdentity`, so keep class defaults unless you intentionally change:
- `CanAppearInTravel = true`
- `CanAppearInArena = true`

---

## 3) Defense

Set:
- `MaxHP = 30`
- `HitDice = 4d10+8`
- `ArmorClass_Total = 15`
- `ArmorClass_Touch = 13`
- `ArmorClass_FlatFooted = 12`
- `AcBreakdown` include natural armor note (`+2 natural`) as desired
- `FortitudeSave_Base = 3`
- `ReflexSave_Base = 7`
- `WillSave_Base = 5`
- `SpellResistance = 0`
- `Resistances = []`
- `DamageReductions = []`
- `Immunities = []`

Definitive code note for Adaro poison handling:
- Do **not** give Adaro global `ImmunityType.Poison` for this stat block.
- In this codebase, `ImmunityType.Poison` blocks poison effects broadly (`StatusEffectController` immunity check + `CreatureStats.HasImmunity`).
- Adaro text grants poison-use safety and immunity to its own nettlefin toxin handling, not universal immunity to all poisons.

---

## 4) Offense and movement

Set:
- `BaseAttackBonus = 4`
- `Speed_Land = 10`
- `Speed_Swim = 50`
- `Speed_Fly = 0`
- `Speed_Burrow = 0`
- `Speed_Climb = 0`
- `Space = 5`
- `Reach = 5`
- `CombatManeuverDefense = 20`
- `CombatManeuverBonus = 7`

Armored land speed (`Speed_Land_Armored`) importer logic:
- if land >= 30 -> 20
- else if land >= 20 -> 15
- else unchanged

For Adaro (land 10):
- `Speed_Land_Armored = 10`

---

## 5) Attacks and starting equipment (how importer treats them)

Importer attack parsing behavior:
- Manufactured weapons (keyword-based, includes `spear`) -> item asset in `res://Data/Items` and added to `StartingEquipment`.
- Non-manufactured attacks (e.g., `bite`) -> added to `MeleeAttacks` (`NaturalAttack`).

For Adaro, configure:

## 5.1 Manufactured weapon item (shared)
Create/reuse shared `Item_SO`:
- `res://Data/Items/Weapon_Spear.tres` (name can vary; use your canonical spear asset)

And ensure:
- melee spear damage profile `1d8`, crit `x3`
- ranged spear profile if you model thrown/projectile usage

## 5.2 Natural attack entry
Add to `MeleeAttacks`:
- `AttackName = Bite`
- damage `1d6+3`

## 5.3 Starting equipment
Set `StartingEquipment` to include at least:
- Spear item
- Extra spears to reflect treasure line (`3 spears`) if your inventory system tracks duplicates/quantity

---

## 6) Ability/spell/SQ wiring (shared assets, importer-parity)

Use shared abilities in `res://Data/Abilities/Special` and `res://Data/Abilities/Spells`.
Importer path conventions:
- generic special ability asset: `Ability_Special_{Name}.tres`
- if mechanics DB has creature-specific key fallback, importer may use `{CreatureNoSpace}_{Name}` key internally.

For Adaro, create/reuse shared abilities for:
1. `Poison` (Ex)
2. `Poison Use` (Ex)
3. `Rain Frenzy` (Su)
4. `Speak with Sharks` (Su)

And include them in `KnownAbilities`.

Also set:
- `SpecialQualities` includes: `amphibious`, `poison use`

> Importer note: `special_qualities` text is copied as strings into `SpecialQualities`; special ability text block creates `Ability_SO` entries and adds them to `KnownAbilities`.

---

## 7) Senses (including keen scent) — exact importer behavior

Set from stat block and importer mappings:
- `HasLowLightVision = true`
- `HasDarkvision = true`
- `DarkvisionRange = 60`
- `HasBlindsight = false`
- `HasTremorsense = false`
- `BlindsenseRange = 30` (Adaro has blindsense 30)

For keen scent:
- importer checks `senses/keen scent`.
- if present but non-numeric, `HasScent = true` and range defaults to 30 if not already set.

Set:
- `HasScent = true`
- `ScentRange = 30` (importer parity default for unspecified keen-scent range)

Other sense flags remain false/default unless your source row explicitly supplies them.

---

## 8) Statistics / feats / skills / languages

Set abilities:
- `Strength = 16`
- `Dexterity = 17`
- `Constitution = 14`
- `Intelligence = 10`
- `Wisdom = 13`
- `Charisma = 13`

Set combat summary:
- `BaseAttackBonus = 4`
- `CombatManeuverBonus = 7`
- `CombatManeuverDefense = 20`
- `TotalInitiativeModifier = 3`

Feats (shared assets):
- `Deadly Aim`
- `Weapon Focus (spear)`

Skills (set values/ranks so final totals match):
- Intimidate +8
- Perception +8
- Stealth +10
- Swim +18

Languages:
- `Aquan`, `Common`

---

## 9) Poison specifics (Nettlefin toxin)

Create/reuse a shared poison ability/effect definition with:
- Delivery: injury via spear hit
- Save: `Fortitude DC 15`
- Frequency: `1/minute for 4 minutes`
- Effect: `paralyzed for 1 minute`
- Cure: `2 consecutive saves`

Attach this poison behavior to spear usage path used by your combat/effect system.

Definitive implementation guidance from current code:
- There is currently **no dedicated global "Poison Use" rules hook** in the importer/runtime that auto-applies self-poison protection.
- Therefore, implement `Poison Use (Ex)` as an explicit shared ability/effect rule for Adaro poison workflows (for example, in the poison application effect path) rather than by setting broad poison immunity.
- Keep `ImmunityType.Poison` off Adaro unless you intentionally want full poison immunity for all poison effects.

---

## 10) Rain Frenzy (Su) and weather hookup

Create/reuse `Rain Frenzy` as shared special ability and ensure its effect condition is tied to rainy/storm weather state.

Behavior requirement from stat block:
- while raining/stormy, Adaro behaves as if under `Rage`
- applies even underwater if within move action of surface (50 ft for most adaros)

In your implementation, encode weather gating and underwater/surface-distance condition in effect components or passive controller used by this ability.

---

## 11) Speak with Sharks (Su)

Create/reuse `Speak with Sharks` ability and set behavior:
- telepathic communication distance: `100 ft`
- concept complexity: simple commands (`come`, `defend`, `attack`)

Add to `KnownAbilities`.

---

## 12) Travel and biome configuration (runtime rules)

Importer does **not** place creatures into biome encounter pools.
You must configure biome resources explicitly.

For each relevant `BiomeTravelDefinition`:
1. Add `Adaro.tres` to `EncounterCreaturePool`.
2. Ensure `RequiredEnvironmentProperties` intersects Adaro ecology (`Aquatic`, `Warm`).
3. Keep `CanAppearInTravel = true`.

Group size in travel runtime:
- final spawn count is rolled from biome `GroupSpawnSettings` (`MinGroupSize`, `MaxGroupSize`, plus difficulty scaling), not directly from `AverageGroupSize`.

To make travel behavior match Adaro organization themes:
- tune `GroupSpawnSettings` in your warm-ocean biomes to support solo/hunting-party/tribe envelopes.

---

## 13) Scent / sound / sensory emission / stimulus settings

## 13.1 Scent (explicit)
Already covered by senses:
- `HasScent = true`
- `ScentRange = 30` (keen scent default when no numeric range supplied)

## 13.2 Sound (explicit runtime-derived settings)
There are no direct per-creature inspector fields for `SoundProfile`; runtime derives from template.
For Adaro values (from formula):
- Size Medium => size factor 0.6
- Strength+Constitution = 30 => mass factor 1.0
- Dexterity 17 => agility factor ~0.433
- Derived `BodyVolume ≈ 0.6`
- Derived `StepNoise ≈ 0.26`
- Derived `WaterNoise = BodyVolume * 0.5 ≈ 0.3` (because `Speed_Swim > 0`)
- MovementType resolves to `Swimming` (from compatibility alias using `Speed_Swim > 0` and no fly speed)

No extra inspector action required beyond accurate core stats/speeds unless you add custom sensory modifiers.

## 13.3 SensoryEmissionModifiers / StimulusComponents
Importer does not auto-populate these arrays.
Default setup:
- `SensoryEmissionModifiers = []`
- `StimulusComponents = []`

Only add entries if your design intentionally gives Adaro extra deception/perception modulation behavior.

---

## 14) Visual setup

Create/assign visual prefab as in your creature pipeline (cylinder or full model scene):
- `CharacterPrefab` points to Adaro visual scene.

If using portrait textures:
- world visual texture and GUI identifier texture are handled by your standard art mapping pipeline.

---

## 15) Full validation checklist

1. Template exists at `res://Data/Creatures/Adaro.tres`.
2. `AverageGroupSize = 10` (importer parity for organization text).
3. `NaturalEnvironmentProperties = [Aquatic, Warm]`.
4. Senses match stat block: low-light true, darkvision 60, blindsense 30, scent true 30.
5. Speeds: land 10, swim 50, armored 10.
6. Combat stats: AC/HP/saves/BAB/CMB/CMD match block.
7. Feats present: Deadly Aim, Weapon Focus (spear).
8. Skills final totals match block.
9. Known abilities include Poison, Poison Use, Rain Frenzy, Speak with Sharks.
10. Poison effect matches DC/frequency/effect/cure text.
11. Biome resources include Adaro in `EncounterCreaturePool` for warm-ocean travel biomes.
12. Biome required properties allow Adaro (`Aquatic`/`Warm` intersection).
13. Travel group-size behavior matches biome `GroupSpawnSettings` expectation.
14. Under rainy/storm conditions, Rain Frenzy behavior activates correctly.
15. Under swim movement, sound detection behavior reflects derived swim/noise profile.
