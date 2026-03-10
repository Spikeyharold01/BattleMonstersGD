# Aasimar (Cleric 1, CR 1/2) — Full From-Scratch Godot Inspector Build Guide

This document is a **from-zero** implementation guide. It assumes **nothing exists yet** for this creature.
You will create every required resource and wire them together:
- visual scene (`CharacterPrefab`),
- items,
- feat,
- all abilities/spells listed in the stat block,
- creature template,
- optional UI portrait mapping for `Aasimar_F.png`.

**Data-centric rule used in this guide:** items, feats, traits, abilities, and spells are authored as **shared canonical resources** and then assigned to creatures. Only creature-specific assets (like the template and art scene) are Aasimar-specific.

---

## 0) Stat block being encoded

- Aasimar CR 1/2, XP 200, Aasimar cleric 1
- NG Medium outsider (native)
- Init +0; Senses darkvision 60 ft.; Perception +5
- AC 15, touch 10, flat-footed 15 (+5 armor)
- hp 11 (1d8+3)
- Fort +4, Ref +0, Will +5
- Resist acid 5, cold 5, electricity 5
- Speed 30 ft. (20 ft. in armor)
- Melee heavy mace –1 (1d8–1)
- Ranged light crossbow +0 (1d8/19–20)
- Special Attacks: channel positive energy (5/day, 1d6, DC 12); rebuke death (1d4+1, 6/day); touch of good (6/day)
- Spell-Like Abilities (CL 1st): 1/day daylight
- Spells Prepared (CL 1st):
  - 1st—bless, command (DC 14), protection from evilD
  - 0th (at will)—detect magic, guidance, stabilize
  - D domain spell; Domains Good, Healing
- Str 8, Dex 10, Con 14, Int 13, Wis 17, Cha 14
- Base Atk +0; CMB –1; CMD 9
- Feats Turn Undead
- Skills Diplomacy +8, Heal +7, Knowledge (religion) +5; Racial Modifiers +2 Diplomacy, +2 Perception
- Languages Celestial, Common, Draconic
- Environment any land
- Organization solitary, pair, or team (3–6)
- Treasure NPC gear (scale mail, heavy mace, light crossbow with 10 bolts, other treasure)

---

## 1) Create folders first (exact structure)

In FileSystem, create:

1. `Assets/Creatures/Aasimar/`
2. `Data/Creatures/Outsiders/Aasimar/`
3. `Data/Items/`
4. `Data/Feats/`
5. `Data/Abilities/`
6. `Data/UI/Portraits/` (for optional GUI portrait mapping)

Copy your images into:
- `Assets/Creatures/Aasimar/Aasimar.png`
- `Assets/Creatures/Aasimar/Aasimar_F.png`

---

## 2) Build the 3D token scene (cylinder + Aasimar.png)

## 2.1 Space-to-size rule (must match your grid)

You specified:
- **1 grid square = 5 feet**.

Use:
- `SpaceInSquares = CreatureSpaceFeet / 5`
- `CylinderDiameterWorld = TileWorldSize * SpaceInSquares`
- `CylinderRadiusWorld = CylinderDiameterWorld / 2`

Where `TileWorldSize` is your world-space width of one tile.

### Size table

| Size | Space ft | Squares | Cylinder Diameter |
|---|---:|---:|---:|
| Fine | 0.5 | 0.1 | `TileWorldSize * 0.1` |
| Diminutive | 1 | 0.2 | `TileWorldSize * 0.2` |
| Tiny | 2.5 | 0.5 | `TileWorldSize * 0.5` |
| Small | 5 | 1 | `TileWorldSize * 1` |
| Medium | 5 | 1 | `TileWorldSize * 1` |
| Large | 10 | 2 | `TileWorldSize * 2` |
| Huge | 15 | 3 | `TileWorldSize * 3` |
| Gargantuan | 20 | 4 | `TileWorldSize * 4` |
| Colossal | 30 | 6 | `TileWorldSize * 6` |

For Aasimar (Medium):
- `SpaceInSquares = 1`
- `CylinderDiameterWorld = TileWorldSize`
- `CylinderRadiusWorld = TileWorldSize / 2`

If `TileWorldSize = 1.0`, radius is `0.5`.

## 2.2 Scene creation steps

1. New scene, root `Node3D`, name: `Aasimar_Cylinder`.
2. Add child `MeshInstance3D`, name: `BodyCylinder`.
3. Set `BodyCylinder.Mesh = CylinderMesh`.
4. In `CylinderMesh`:
   - `Top Radius = CylinderRadiusWorld`
   - `Bottom Radius = CylinderRadiusWorld`
   - `Height = 1.8` (visual; adjust for your art scale)
5. Create `StandardMaterial3D` and assign to `BodyCylinder.Material Override`.
6. In material:
   - `Albedo > Texture = Assets/Creatures/Aasimar/Aasimar.png`
   - Enable transparency mode if PNG requires alpha edge handling.
   - Adjust `UV1 Scale/Offset` to center face if needed.
7. Save scene:
   - `Assets/Creatures/Aasimar/Aasimar_Cylinder.tscn`

---

## 3) Create canonical shared item resources (reuse across creatures)

Create/reuse **shared** `Item_SO` resources in `Data/Items/`.
Do **not** duplicate the same core equipment per creature unless you need a truly unique variant.

> Why: this keeps balance/data maintenance centralized. If you fix `HeavyMace.tres` once,
> every creature using heavy mace is corrected automatically.

## 3.1 `ScaleMail.tres`
- `ItemName`: `Scale Mail`
- `Description`: `Scale mail armor used by the Aasimar cleric.`
- `ItemType`: `Armor`
- `IsEquippable`: `true`
- `EquipSlot`: `Armor`
- `ArmorCategory`: `Medium`
- Weapon-specific fields: leave defaults.

## 3.2 `HeavyMace.tres`
- `ItemName`: `Heavy Mace`
- `Description`: `One-handed heavy mace.`
- `ItemType`: `Weapon`
- `IsEquippable`: `true`
- `EquipSlot`: `MainHand`
- `WeaponType`: `Melee`
- `Handedness`: `OneHanded`
- `CriticalThreatRange`: `20`
- `CriticalMultiplier`: `2`
- `DamageInfo`: add element:
  - `DiceCount = 1`
  - `DieSides = 8`
  - `FlatBonus = 0` (Strength penalty handled on creature stats)
  - `DamageType = Bludgeoning`

## 3.3 `LightCrossbow.tres`
- `ItemName`: `Light Crossbow`
- `Description`: `Ranged light crossbow.`
- `ItemType`: `Weapon`
- `IsEquippable`: `true`
- `EquipSlot`: `MainHand`
- `WeaponType`: `Projectile`
- `Handedness`: `TwoHanded`
- `RangeIncrement`: set to your project standard for light crossbow
- `CriticalThreatRange`: `19`
- `CriticalMultiplier`: `2`
- `DamageInfo`: add element:
  - `DiceCount = 1`
  - `DieSides = 8`
  - `FlatBonus = 0`
  - `DamageType = Piercing`

## 3.4 `CrossbowBolts_10.tres`
- `ItemName`: `Crossbow Bolts (10)`
- `Description`: `Ten bolts for light crossbow.`
- `ItemType`: `Gear` (or your ammo convention)
- If your inventory has quantity stacks, set quantity = 10 there.

---

## 4) Create/reuse shared feat resource

Create shared `Feat_SO` resource `Data/Feats/TurnUndead.tres`.

Set:
- `FeatName`: `Turn Undead`
- `Description`: `Allows channeling divine power against undead (project implementation-specific behavior).`
- `Type`: choose your intended feat behavior category in your system.
- `AssociatedAbility`: leave empty unless you explicitly model this feat as an activated ability.

---

## 5) Create/reuse shared generic ability resources (10 total)

Create these shared `Ability_SO` resources in `Data/Abilities/` (or your shared spell/special-ability subfolders):

1. `ChannelPositiveEnergy.tres`
2. `RebukeDeath.tres`
3. `TouchOfGood.tres`
4. `Daylight_SLA.tres`
5. `Bless.tres`
6. `Command.tres`
7. `ProtectionFromEvil_Domain.tres`
8. `DetectMagic.tres`
9. `Guidance.tres`
10. `Stabilize.tres`

For each ability, create nested resources directly in Inspector when fields are null. Keep these assets generic (no creature name in file names) so any creature can reference them:
- `Range` (`RangeInfo`)
- `SavingThrow` (`SavingThrowInfo`)
- `Usage` (`UsageLimitation`)
- `Components` (`SpellComponentInfo`)
- `AreaOfEffect` only if needed
- `EffectComponents` entries needed by your runtime

---

## 6) Fill ability inspector values (exact targets)

## 6.1 `ChannelPositiveEnergy.tres`
- Core:
  - `AbilityName = Channel Positive Energy`
  - `DescriptionForTooltip = Channel positive energy (5/day, 1d6, DC 12).`
  - `IsImplemented = true`
  - `SpellLevel = 0`
  - `SpecialAbilityType = Su`
- Classification:
  - `Category = SpecialAttack`
  - `School = Universal`
- Usage & Targeting:
  - `ActionCost = Standard`
  - `TargetType = Area_FriendOrFoe` (or your channel mode convention)
  - `AttackRollType = None`
  - `EntityType = CreaturesOnly`
  - `Range.Type = Custom`
  - `Range.CustomRangeInFeet = 30`
- Save:
  - `SavingThrow.SaveType = Will` (if undead-damaging implementation)
  - `SavingThrow.BaseDC = 12`
  - `SavingThrow.IsDynamicDC = false`
- Usage:
  - `Usage.Type = PerDay`
  - `Usage.UsesPerDay = 5`
- Effect:
  - Add effect component that rolls/applies `1d6` healing or undead damage based on your channel implementation.

## 6.2 `RebukeDeath.tres`
- `AbilityName = Rebuke Death`
- `DescriptionForTooltip = Rebuke death (6/day), heals 1d4+1 by touch.`
- `Category = SpecialAttack`
- `SpecialAbilityType = Su`
- `ActionCost = Standard`
- `TargetType = SingleAlly`
- `Range.Type = Touch`
- `SavingThrow.SaveType = None`
- `Usage.Type = PerDay`
- `Usage.UsesPerDay = 6`
- Effect component: heal `1d4+1`.

## 6.3 `TouchOfGood.tres`
- `AbilityName = Touch of Good`
- `DescriptionForTooltip = Touch of Good (6/day).`
- `Category = SpecialAttack`
- `SpecialAbilityType = Su`
- `ActionCost = Standard`
- `TargetType = SingleAlly` (or your self/ally variant)
- `Range.Type = Touch`
- `SavingThrow.SaveType = None`
- `Usage.Type = PerDay`
- `Usage.UsesPerDay = 6`
- Effect component: add your buff implementation for Touch of Good.

## 6.4 `Daylight_SLA.tres`
- `AbilityName = Daylight`
- `DescriptionForTooltip = Spell-like ability, 1/day.`
- `Category = Spell`
- `SpecialAbilityType = Sp`
- `SpellLevel = 3` (spell level entry for daylight if used by your logic)
- `ActionCost = Standard`
- `TargetType` and `Range` according to your Daylight effect implementation
- `Usage.Type = PerDay`
- `Usage.UsesPerDay = 1`
- `SavingThrow.SaveType = None`
- Effect component: your existing daylight/light-area effect.

## 6.5 `Bless.tres`
- `AbilityName = Bless`
- `Category = Spell`
- `SpecialAbilityType = None`
- `SpellLevel = 1`
- `ActionCost = Standard`
- `Usage.Type`: set according to your prepared-caster workflow (typically per encounter/day slot tracking)
- Add bless buff effect component.

## 6.6 `Command.tres`
- `AbilityName = Command`
- `Category = Spell`
- `SpellLevel = 1`
- `ActionCost = Standard`
- `TargetType = SingleEnemy`
- `Range.Type`: your command implementation default
- `SavingThrow.SaveType = Will`
- `SavingThrow.BaseDC = 14`
- `Usage.Type`: prepared spell convention
- Add command/control effect component.

## 6.7 `ProtectionFromEvil_Domain.tres`
- `AbilityName = Protection from Evil (Domain)`
- `Category = Spell`
- `SpellLevel = 1`
- `ActionCost = Standard`
- `TargetType = SingleAlly`
- `Range.Type = Touch`
- `SavingThrow.SaveType = None`
- `Usage.Type`: prepared spell convention
- Add protection-from-evil effect component.

## 6.8 `DetectMagic.tres`
- `AbilityName = Detect Magic`
- `Category = Spell`
- `SpellLevel = 0`
- `ActionCost = Standard`
- `Usage.Type = AtWill`
- Add detect-magic effect component.

## 6.9 `Guidance.tres`
- `AbilityName = Guidance`
- `Category = Spell`
- `SpellLevel = 0`
- `ActionCost = Standard`
- `Usage.Type = AtWill`
- Add guidance buff effect component.

## 6.10 `Stabilize.tres`
- `AbilityName = Stabilize`
- `Category = Spell`
- `SpellLevel = 0`
- `ActionCost = Standard`
- `Usage.Type = AtWill`
- Add stabilize/heal-state effect component.

---

## 7) Create creature template resource from scratch

Create:
- `Data/Creatures/Outsiders/Aasimar/Aasimar_Cleric1_CRHalf.tres` (`CreatureTemplate_SO`)

Fill every relevant inspector group as follows.

## 7.1 Identity & Info
- `CreatureName = Aasimar`
- `ChallengeRating = 1` (store CR 1/2 in notes if int-only)
- `XpAward = 200`
- `Race = Aasimar cleric 1`
- `Size = Medium`
- `AverageGroupSize = 5` (importer parity from `solitary, pair, team (3–6)` parsing).

### Number Appearing (Organization) — exact setup

This project already has importer logic for organization:
- Importer reads `ecology/organization` and writes `CreatureTemplate_SO.AverageGroupSize` via `ImportUtils.ParseOrganization(...)`.
- `ParseOrganization` chooses the **largest mean** among entries like solitary/pair/range and rounds to int.

For `solitary, pair, or team (3–6)`:
- solitary => mean `1`
- pair => mean `2`
- team(3-6) => mean `4.5`
- max mean = `4.5`, rounded => **`AverageGroupSize = 5`**

So if you are following importer parity exactly for this creature, set:
- `AverageGroupSize = 5`

Important runtime note:
- Travel encounter **actual spawned count** is rolled from biome `GroupSpawnSettings` (`MinGroupSize/MaxGroupSize` + difficulty scaling), not from `AverageGroupSize`.
- Therefore, to mirror tabletop organization in travel spawns, also configure biome group envelope accordingly.

## 7.2 Knowledge
- `AssociatedKnowledgeSkill = KnowledgeReligion`
- `Type = Outsider`
- `CanUseEquipment = true`
- `CanBeMounted = false`
- `SubTypes = ["native"]`
- `RacialLanguages = [Celestial, Common, Draconic]`
- `Alignment = NG`
- `Classes = ["Cleric 1"]`
- `NaturalEnvironmentProperties = [Any]` (importer parity for "any land").

### Biomes / where they appear — exact setup

Use importer parity rules for ecology fields:

- Importer maps `ecology/environment` using `ImportUtils.TranslateEnvironmentToProperties(...)`.
- For text containing `any` (including `any land`), importer returns `[EnvironmentProperty.Any]`.

Then apply runtime travel rules:

There are two layers to make biome spawning work:

1. **Creature eligibility layer (on `CreatureTemplate_SO`)**
   - `CanAppearInTravel = true`
   - `CanAppearInArena = true` (if desired in arena pools)
   - `NaturalEnvironmentProperties = [Any]` for importer parity with "any land".

2. **Biome membership layer (on each `BiomeTravelDefinition`)**
   - Open the biome resource used by your phase manager.
   - In `EncounterCreaturePool`, add `Aasimar_Cleric1_CRHalf.tres`.
   - Ensure biome `RequiredEnvironmentProperties` intersects with creature properties. Since Aasimar is `[Any]` from importer parity, required list must include `Any` **or** be left empty.

If you skip layer 2, the creature exists but will not naturally spawn in travel biomes.

## 7.3 Senses
- `HasDarkvision = true`
- `DarkvisionRange = 60`
- leave other senses false/default unless your implementation needs them.

## 7.4 Defense
- `MaxHP = 11`
- `HitDice = 1d8+3`
- `ArmorClass_Total = 15`
- `ArmorClass_Touch = 10`
- `ArmorClass_FlatFooted = 15`
- `AcBreakdown = +5 armor`
- `FortitudeSave_Base = 4`
- `ReflexSave_Base = 0`
- `WillSave_Base = 5`
- `SpellResistance = 0`
- `HasLightSensitivity = false`
- `Resistances`: add 3 `DamageResistance` resources:
  1. `Amount = 5`, `DamageTypes = ["Acid"]`
  2. `Amount = 5`, `DamageTypes = ["Cold"]`
  3. `Amount = 5`, `DamageTypes = ["Electricity"]`
- `Immunities = []`
- `Weaknesses = []`
- `DamageReductions = []`
- `Traits = []` (unless you model aasimar heritage as trait resource)

## 7.5 Offense & Movement
- `Speed_Land = 30`
- `Speed_Land_Armored = 20`
- `Speed_Fly = 0`
- `Speed_Swim = 0`
- `Speed_Burrow = 0`
- `Speed_Climb = 0`
- `Space = 5`
- `Reach = 5`
- `VerticalReach`: set to your character model convention (commonly 5)
- `MeleeAttacks = []` (manufactured weapon damage comes from items)
- `RakeAttacks = []`
- `SpecialAttacks = [ChannelPositiveEnergy, RebukeDeath, TouchOfGood]`
- maneuver immunity booleans: keep defaults (`false`)

## 7.6 Statistics
- `Strength = 8`
- `Dexterity = 10`
- `Constitution = 14`
- `Intelligence = 13`
- `Wisdom = 17`
- `Charisma = 14`
- `PrimaryCastingStat = Wisdom`
- `BaseAttackBonus = 0`
- `CombatManeuverBonus = -1`
- `CombatManeuverDefense = 9`
- `CasterLevel = 1`
- `TotalInitiativeModifier = 0`

## 7.7 Skills
Create `SkillValue` entries so final runtime bonuses match target values:
- Diplomacy final target `+8`
- Heal final target `+7`
- KnowledgeReligion final target `+5`
- Perception final target `+5`

Also preserve racial notes in data comments/doc:
- `+2 Diplomacy`
- `+2 Perception`

## 7.8 Feats
Add one `FeatInstance`:
- `Feat = Data/Feats/TurnUndead.tres`
- `TargetName = ""`

## 7.9 Gear
Set `StartingEquipment` exactly (using shared item assets):
1. `ScaleMail.tres`
2. `HeavyMace.tres`
3. `LightCrossbow.tres`
4. `CrossbowBolts_10.tres`

## 7.10 Abilities & Spells
- `ChannelPositiveEnergy` helper object:
  - `DiceCount = 1`
  - `SaveDC = 12`
  - `UsesPerDay = 5`
- `KnownAbilities` add these shared abilities:
  1. ChannelPositiveEnergy
  2. RebukeDeath
  3. TouchOfGood
  4. Daylight_SLA
  5. Bless
  6. Command
  7. ProtectionFromEvil_Domain
  8. DetectMagic
  9. Guidance
  10. Stabilize
- `SpecialQualities` add:
  - `Aasimar heritage`
  - `Good domain`
  - `Healing domain`

## 7.11 Visuals
- `CharacterPrefab = Assets/Creatures/Aasimar/Aasimar_Cylinder.tscn`

---

## 8) Add GUI identifier art (`Aasimar_F.png`) from scratch

`CreatureTemplate_SO` currently has no direct portrait export field, so create a minimal mapping system now.

### 8.1 Create portrait mapping resource (recommended)
1. Create a resource/script in your UI system that maps `CreatureName -> Texture2D`.
2. Add mapping entry:
   - Key: `Aasimar`
   - Value: `Assets/Creatures/Aasimar/Aasimar_F.png`
3. Update your GUI panels (roster, tooltip, turn order) to resolve portrait by creature name/template id.

### 8.2 Minimum fallback if you do not have mapping code yet
- Keep `Aasimar_F.png` in `Data/UI/Portraits/` and wire it directly in the specific UI node for testing.

---

## 9) Final connection checklist (must all be true)

1. `Aasimar_Cleric1_CRHalf.tres` exists.
2. `CharacterPrefab` on that template points to `Aasimar_Cylinder.tscn`.
3. `Aasimar_Cylinder.tscn` cylinder radius matches Medium size using your tile scale.
4. All 4 item resources exist and are in `StartingEquipment`.
5. Turn Undead feat resource exists and is in `Feats`.
6. Shared ability resources exist and are in `KnownAbilities`.
7. `SpecialAttacks` has exactly Channel Positive Energy, Rebuke Death, Touch of Good.
8. `ChannelPositiveEnergy` helper values are 1d6, DC12, 5/day.
9. Defensive values match AC/HP/saves/resistances.
10. Movement includes `Speed_Land = 30` and `Speed_Land_Armored = 20`.
11. Languages are Celestial/Common/Draconic.
12. UI portrait lookup resolves `Aasimar -> Aasimar_F.png`.
13. `AverageGroupSize` matches importer organization parsing (for this stat block, value `5`).
14. `CanAppearInTravel = true` and Aasimar is added to each target biome's `EncounterCreaturePool`.
15. Biome ecology filters (`RequiredEnvironmentProperties`) allow Aasimar via importer parity (`Any` present or required list empty).

---

## 10) Manual gameplay verification sequence

1. Spawn Aasimar in a test scene.
2. Confirm model appears as cylinder textured with `Aasimar.png`.
3. Confirm footprint equals one tile (Medium).
4. Apply acid/cold/electricity damage and verify `-5` resistance each.
5. Verify heavy mace attack profile uses 1d8 base and crossbow uses 1d8 with 19–20 crit.
6. Use Channel Positive Energy repeatedly and verify:
   - 5/day limit,
   - DC 12 save behavior,
   - 1d6 amount.
7. Use Rebuke Death and verify 6/day and 1d4+1 heal.
8. Use Touch of Good and verify 6/day and buff applies.
9. Use Daylight and verify 1/day SLA.
10. Verify prepared spell list contains Bless, Command, Protection from Evil (Domain), and at-will cantrips Detect Magic/Guidance/Stabilize.
11. Confirm GUI shows `Aasimar_F.png` for this creature.
12. In travel testing, verify Aasimar actually appears in configured biomes (not just via manual spawn).
13. Verify travel group-size behavior matches `GroupSpawnSettings` envelope and difficulty scaling for your configured biomes.

---

## 11) Known data-model constraints (still explicit)

- `ChallengeRating` is int, so CR 1/2 is stored by convention (commonly as `1` + note).
- Exact spell slot accounting depends on your project’s spell-preparation runtime; this guide creates the assets and links all spells explicitly.
- Ammo quantity handling can be stack-based inventory logic or represented as a gear item with quantity metadata.
