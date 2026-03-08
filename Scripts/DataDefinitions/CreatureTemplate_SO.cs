// =================================================================================================
// FILE: CreatureTemplate_SO.cs (Godot C# Version)
// PURPOSE: Resource defining a creature's base stats and abilities.
// REVISED: Consolidated multiple class definitions into a single, correct script.
// REVISED: Replaced [System.Serializable] with partial classes inheriting GodotObject for inspector support (or just simple C# classes if resource embedding is handled differently).
// REVISED: Replaced List<T> with Godot.Collections.Array<T> or simpler C# Lists (Godot 4 .NET supports standard C# Collections well, but [Export] works best with Godot variants or Resources).
// DO NOT ATTACH - Create assets from this in the FileSystem dock.
// =================================================================================================
using Godot;
using System.Collections.Generic;


// --- HELPER CLASSES ---
// In Godot, for nested data to show in Inspector, it usually needs to be a Resource or GodotObject.
// However, C# Lists of custom classes don't serialize well in Godot inspector unless they are Resources.
// For data-heavy structures, inheriting from Resource is safer.

[GlobalClass]
public partial class ConditionalCMDBonus : Resource
{
    [Export] public int CmdValue;
    [Export] public Godot.Collections.Array<ManeuverType> Maneuvers = new();
}

[GlobalClass]
public partial class ConditionalManeuverBonus : Resource
{
    [Export] public int Bonus;
    [Export] public Godot.Collections.Array<ManeuverType> Maneuvers = new();
    [Export] public ManeuverCondition Condition = ManeuverCondition.None;
}

[GlobalClass]
public partial class NaturalAttack : Resource
{ 
    [Export] public string AttackName; 
    [Export] public bool IsPrimary = true;
    [Export] public Godot.Collections.Array<DamageInfo> DamageInfo = new(); // Assuming DamageInfo is also a Resource
    [Export] public int MiscAttackBonus = 0;
    [Export] public int EnhancementBonus = 0;
    [Export] public int CriticalThreatRange = 20;
    [Export] public int CriticalMultiplier = 2;
    [Export] public bool HasGrab;
    [Export] public Godot.Collections.Array<string> SpecialQualities = new(); 
	 [Export] public Ability_SO OnHitAbility; // Drag "Fate Drain" ability here
	 [Export] public Ability_SO OnCritAbility; // Triggers ONLY on a confirmed critical hit

    [ExportGroup("Charge Interaction")]
    [Export(PropertyHint.Range, "1,10,1")]
    [Tooltip("If this natural attack hits while the attacker is Charging, multiply its final damage by this value.")]
    public int ChargeDamageMultiplier = 1;

    [Export]
    [Tooltip("Optional: requires the creature to have a special attack with this name for ChargeDamageMultiplier to apply (e.g. Impale). Leave empty to always allow.")]
    public string ChargeMultiplierRequiredSpecialAttack = "";
    
    [ExportGroup("Internal Data")]
    [Export] public Godot.Collections.Array<StatusEffect_SO> PassiveEffects = new();
}

// Assuming these helper classes exist elsewhere or need to be Resources too:
// SpellLikeAbility, SpellcastingInfo, DamageResistance, DamageReduction, SkillValue, ChannelEnergyInfo, FeatInstance, AttackReachOverride

[GlobalClass]
public partial class DamageResistance : Resource
{ 
    [Export] public int Amount; 
    [Export] public Godot.Collections.Array<string> DamageTypes = new();
}

[GlobalClass]
public partial class DamageReduction : Resource
{ 
    [Export] public int Amount; 
    [Export] public string Bypass; 
    [Export] public int MaxAbsorbed = 0;
}

[GlobalClass]
public partial class SkillValue : Resource
{ 
    [Export] public SkillType Skill; 
    [Export] public int Ranks; 
}

[GlobalClass]
public partial class ChannelEnergyInfo : Resource
{
    [Export] public int DiceCount = 1;
    [Export] public int SaveDC = 10;
    [Export] public int UsesPerDay = 3;
}

[GlobalClass]
public partial class FeatInstance : Resource
{
    [Export] public Feat_SO Feat;
    [Export] public string TargetName;
}

[GlobalClass]
public partial class AttackReachOverride : Resource
{
    [Export] public float Reach;
    [Export] public Godot.Collections.Array<string> AttackNames = new();
}

// --- THE MAIN RESOURCE DEFINITION ---
[GlobalClass]
public partial class CreatureTemplate_SO : Resource
{
    [ExportGroup("Identity & Info")]
    [Export] public string CreatureName;
    [Export] public int ChallengeRating;
    [Export] public int XpAward;
    [Export] public string Race;
    [Export] public CreatureSize Size;
    [Export] public int AverageGroupSize;
    
    [ExportGroup("Knowledge")]
    [Export] public SkillType AssociatedKnowledgeSkill = SkillType.KnowledgeNature;
    [Export] public CreatureType Type;
    [Export] public bool CanUseEquipment = true;
    [Export] public bool CanBeMounted = false;
    [Export] public Godot.Collections.Array<string> SubTypes = new();
    [Export] public Godot.Collections.Array<Language> RacialLanguages = new();
    [Export] public string Alignment;
    [Export] public Godot.Collections.Array<string> Classes = new();
    [Export] public Godot.Collections.Array<EnvironmentProperty> NaturalEnvironmentProperties = new();

    [ExportGroup("Encounter Availability")]
    [Export]
    [Tooltip("If true, this creature can be selected by Arena encounter systems.")]
    public bool CanAppearInArena = true;

    [Export]
    [Tooltip("If true, this creature can be selected by Travel encounter systems.")]
    public bool CanAppearInTravel = true;

    [ExportGroup("Senses")]
    [Export] public bool HasLowLightVision;
    [Export] public bool HasDarkvision;
    [Export] public int DarkvisionRange;
    [Export] public bool HasBlindsight;
    [Export] public bool HasTremorsense;
    [Export] public bool HasScent;
    [Export] public int BlindsenseRange;
    [Export] public int SpecialSenseRange;
    
    [Export] public bool HasDetectChaos;
    [Export] public int DetectChaosRange;
    
    [Export] public int ScentRange;
    
    [Export] public bool HasLifesense;
    [Export] public int LifesenseRange;
    
    [Export] public bool HasDetectEvil;
    [Export] public int DetectEvilRange;

    [Export] public bool HasDetectGood;
    [Export] public int DetectGoodRange;

    [Export] public bool HasDetectLaw;
    [Export] public int DetectLawRange;
    
    [Export] public bool HasDetectMagic;
    [Export] public int DetectMagicRange;
    
    [Export] public bool HasDragonSenses;
    [Export] public bool HasMistsight;
    [Export] public bool HasSnowsight;
    [Export] public bool HasSmokeVision;
    [Export] public bool HasAppraisingSight;
    [Export] public bool HasDiseaseScent;
    [Export] public bool HasSeeInDarkness;
    [Export] public bool HasSeeInvisibility;
    [Export] public bool HasSpiritsense;
    [Export] public int SpiritsenseRange;
    [Export] public bool HasThoughtsense;
    [Export] public int ThoughtsenseRange;
    [Export] public bool HasTrueSeeing;
    
    [Export] public Godot.Collections.Array<string> VisionQualities = new();

    [ExportGroup("Sensory Emission")]
    [Export] public Godot.Collections.Array<SensoryEmissionModifier_SO> SensoryEmissionModifiers = new();
    [Export] public Godot.Collections.Array<StimulusComponent> StimulusComponents = new();

    [ExportGroup("Physiology")]
    [Export] public int BreathHoldingMultiplier = 2;

    [ExportGroup("Defense")]
    [Export] public int MaxHP;
    [Export] public string HitDice;
    [Export] public int ArmorClass_Total;
    [Export] public int ArmorClass_Touch;
    [Export] public int ArmorClass_FlatFooted;
    [Export(PropertyHint.MultilineText)] public string AcBreakdown;
    [Export] public int FortitudeSave_Base;
    [Export] public int ReflexSave_Base;
    [Export] public int WillSave_Base;
    [Export] public int FastHealing;
    [Export] public string FastHealingCondition;
    [Export] public int Regeneration;
    [Export] public string RegenerationBypass;
    [Export] public Godot.Collections.Array<DamageReduction> DamageReductions = new(); 
    [Export] public Godot.Collections.Array<string> Immunities = new();
    [Export] public Godot.Collections.Array<DamageResistance> Resistances = new();
    [Export] public int AdaptiveResistanceAmount = 0;
    [Export] public Godot.Collections.Array<DamageResistance> Weaknesses = new();
    [Export] public Godot.Collections.Array<Trait_SO> Traits = new();

    [Export] public bool HasLightSensitivity = false;
    [Export] public int SpellResistance;
    [Export] public Godot.Collections.Array<string> DefensiveAbilities = new();

    [ExportGroup("Offense & Movement")]
    [Export] public float Speed_Land;
    [Export] public float Speed_Land_Armored;
    [Export] public float Speed_Fly;
    [Export] public bool HasWings = true;
    [Export] public FlyManeuverability FlyManeuverability = FlyManeuverability.Average;
    [Export] public float Speed_Swim;
    [Export] public float Speed_Burrow;
    [Export] public float Speed_Climb;
    [Export] public float VerticalReach;
    [Export] public float Space;
    [Export] public float Reach;
    [Export] public Godot.Collections.Array<NaturalAttack> MeleeAttacks = new();
	[Export] public Godot.Collections.Array<NaturalAttack> RakeAttacks = new();
    [Export] public Godot.Collections.Array<Ability_SO> SpecialAttacks = new();
    [Export] public bool IsImmuneToTrip = false;
    [Export] public bool IsImmuneToFlanking = false;
    [Export] public bool IsImmuneToDisarm = false;
    [Export] public Godot.Collections.Array<AttackReachOverride> AttackReachOverrides = new();
    [Export] public bool IsImmuneToGrapple = false;
    [Export] public Godot.Collections.Array<ConditionalCMDBonus> ConditionalCmdBonuses = new();
    [Export] public Godot.Collections.Array<ConditionalManeuverBonus> ConditionalManeuverBonuses = new();

    [ExportGroup("Statistics")]
    [Export] public int Strength;
    [Export] public int Dexterity;
    [Export] public int Constitution;
    [Export] public int Intelligence;
    [Export] public int Wisdom;
    [Export] public int Charisma;
    [Export] public AbilityScore PrimaryCastingStat = AbilityScore.None;
    [Export] public int BaseAttackBonus;
    [Export] public int CombatManeuverBonus;
    [Export] public int CombatManeuverDefense;
    [Export] public Godot.Collections.Array<FeatInstance> Feats = new();
    [Export] public int CasterLevel;
    [Export] public Godot.Collections.Array<SkillValue> SkillRanks = new();
    [Export] public int TotalInitiativeModifier;

    [ExportGroup("Special Rules")]
    [Export] public bool HasDualInitiative = false;

    [ExportGroup("Gear")]
    [Export] public Godot.Collections.Array<Item_SO> StartingEquipment = new();

    [ExportGroup("Abilities & Spells")]
    [Export] public ChannelEnergyInfo ChannelPositiveEnergy;
    [Export] public Godot.Collections.Array<Ability_SO> KnownAbilities = new(); 
    [Export] public Godot.Collections.Array<string> SpecialQualities = new();

    [ExportGroup("Mythic")]
    [Export] public int MythicPower = 0;
    [Export] public int MythicRank = 0; 

    [ExportGroup("Psychic")]
    [Export] public int PsychicEnergyPoints = 0;
    
    [ExportGroup("Passive Effects")]
    [Export] public Godot.Collections.Array<StatusEffect_SO> PassiveEffects = new();
	[ExportGroup("Visuals")]
    [Export] public PackedScene CharacterPrefab; // Used for Veil, Project Image, etc.
}

[GlobalClass]
public partial class DamageConversion : Resource
{
    [Export] public string IncomingType = "Bludgeoning";
    [Export] public string ConvertedType = "Nonlethal"; // "Nonlethal" or "Heal" or "Untyped"
}
