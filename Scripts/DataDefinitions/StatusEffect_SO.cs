using Godot;
using System;

[GlobalClass]
public partial class StatusEffect_SO : Resource
{
    [Export] public string EffectName = "New Effect";
    [Export(PropertyHint.MultilineText)] public string Description;

    [ExportGroup("Duration")]
    [Export] public int DurationInRounds = 1;
    [Export] public bool DurationScalesWithLevel = false;
    [Export] public int DurationPerLevel = 1;
    [Export] public bool DurationIsDivided = false;

    [Export] public Godot.Collections.Array<StatModification> Modifications = new();
    [Export] public Godot.Collections.Array<ConditionalSaveBonus> ConditionalSaves = new(); 
    [Export] public Godot.Collections.Array<SpecialDefense> SpecialDefenses = new();
    
    [Export] public Condition ConditionApplied = Condition.None;
    [Export] public int DamagePerRound = 0;
    [Export] public string DamageType = "Untyped";
	 [ExportGroup("Temporary HP")]
    [Export] public int TemporaryHPBase = 0;
    [Export] public int TemporaryHPDiceCount = 0;
    [Export] public int TemporaryHPDiceSides = 0;
    [Export] public bool TempHPScalesWithLevel = false;
    [Export] public int TempHPMaxBonus = 0; // Cap (e.g. +10)

    [ExportGroup("Mechanics")]
    [Export] public bool IsMindControlEffect = false;
    [Export] public bool IsDischargeable = false;
    [Export] public int Charges = 1;
	
	 [ExportGroup("Defensive Properties")]
    [Export] public int ConcealmentMissChance = 0; // e.g. 20 for Blur, 50 for Displacement
	
	 [Export] 
    [Tooltip("If true, the victim can choose to roll twice (worse) to end this effect.")]
    public bool OffersDisadvantageCure = false; 

    [ExportGroup("Recurring Saves")]
    [Export] public bool AllowsRecurringSave = false;
    [Export] public SaveType RecurringSaveType = SaveType.None;
    [Export] public bool RecurringSaveRequiresIntelligence = false;
	[Export] public bool RecurringSaveConsumesTurn = false; // For Hold Person

    [ExportGroup("Ongoing Sound Emission")]
    [Export] public bool EmitsSoundEachTurn = false;
    [Export(PropertyHint.Range, "0,20,0.1")] public float OngoingSoundIntensity = 2.5f;
    [Export(PropertyHint.Range, "0.1,10,0.1")] public float OngoingSoundDurationSeconds = 1.0f;
    [Export] public SoundEventType OngoingSoundType = SoundEventType.Laughter;
    [Export] public bool OngoingSoundIsIllusion = false;
    
    [Export] public TacticalTag_SO AiTacticalTag;

    [ExportGroup("Invisibility Rules")]
    [Export] public bool BreaksOnAttack = true; 
    [Export] public bool ImmuneToPurge = false;
	[Export] public bool IsUndispellable = false;
	[ExportGroup("Break Conditions")]
    [Export] public bool BreaksOnAllyDamage = false; // For Enthrall
    [Export] public bool BreaksOnDamageTaken = false; // Generic: ends this effect when the owner takes damage
	[Export] public EffectTag Tag = EffectTag.None;

    [ExportGroup("Granted Immunities")]
    [Export]
    [Tooltip("Temporary immunities this effect grants while it remains active and not suppressed.")]
    public Godot.Collections.Array<ImmunityType> GrantedImmunities = new();

    [Export]
    [Tooltip("When true, corruption resistance penalties are ignored for this effect's duration.")]
    public bool SuppressCorruptionResistancePenalty = false;

    [ExportGroup("Environmental Protection")]
    [Export]
    [Tooltip("When enabled, this effect shields the creature from climate-driven heat hazard checks.")]
    public bool ProtectsFromEnvironmentalHeat = false;

    [Export]
    [Tooltip("When enabled, this effect shields the creature from climate-driven cold hazard checks.")]
    public bool ProtectsFromEnvironmentalCold = false;

    [Export]
    [Tooltip("When enabled, ice and snow terrain no longer force balance checks from movement.")]
    public bool IgnoreSnowAndIceMovementPenalty = false;

  [Export]
    [Tooltip("When enabled, weather-driven penalties to perception and ranged attacks are ignored.")]
    public bool IgnorePrecipitationCombatPenalties = false;

    [Export]
    [Tooltip("When enabled, this creature does not leave a scent trail for predators to follow.")]
    public bool SuppressesScentTrails = false;

    [Export(PropertyHint.Range, "0,5,1")]
    [Tooltip("Reduces effective wind severity for this creature by this many categories while active.")]
    public int WindSeverityReductionSteps = 0;

    [ExportGroup("Damage Absorption")]
    [Export]
    [Tooltip("When one or more damage types are listed here, this effect can absorb incoming damage of those types before hit points are reduced.")]
    public Godot.Collections.Array<string> AbsorbDamageTypes = new();

    [Export]
    [Tooltip("Flat amount of damage this effect can absorb, before optional caster-level scaling is added.")]
    public int AbsorptionPoolBase = 0;

    [Export]
    [Tooltip("If enabled, the absorption pool gains additional points from caster level using the value below.")]
    public bool AbsorptionPoolScalesWithCasterLevel = false;

    [Export]
    [Tooltip("Extra absorption points granted per caster level when scaling is enabled.")]
    public int AbsorptionPointsPerCasterLevel = 0;

    [Export]
    [Tooltip("Optional cap on the total absorption pool after scaling. Set to 0 for no cap.")]
    public int AbsorptionPoolMax = 0;

    [ExportGroup("Damage Resistance")]
    [Export]
    [Tooltip("Damage types this effect resists while active. Use entries such as Acid, Cold, Electricity, Fire, or Sonic.")]
    public Godot.Collections.Array<string> ResistDamageTypes = new();

    [Export]
    [Tooltip("Flat amount of damage prevented each time one of the listed damage types is taken.")]
    public int DamageResistanceAmount = 0;
	
    [ExportGroup("Suppression")]
    [Export] public bool BlocksViolentActions = false; // Prevents offensive standard/full-round actions
    [Export] public Godot.Collections.Array<Condition> SuppressConditions = new();
    [Export] public Godot.Collections.Array<EffectTag> SuppressEffectTags = new();
    [Export] public Godot.Collections.Array<BonusType> SuppressBonusTypes = new();

    [ExportGroup("On Apply")]
    [Export] public Godot.Collections.Array<Condition> RemoveConditionsOnApply = new();
	
	[ExportGroup("Removal Conditions")]
    [Export] public bool RemoveWhenHealed = false; 
    [Export] public AbilityScore StatToWatchForHealing = AbilityScore.None;
	
	[ExportGroup("Affliction Mechanics (Poison/Disease)")]
    [Export] public bool IsAffliction = false;
    [Export] public int ConsecutiveSavesToCure = 1;
    [Export(PropertyHint.None, "Seconds. (e.g. 1 min = 60, 1 hour = 3600)")] 
    public float OnsetDelaySeconds = 0f; 
    [Export(PropertyHint.None, "Seconds. (e.g. 1/day = 86400)")] 
    public float FrequencySeconds = 86400f; 
    [Export] public Godot.Collections.Array<AbilityDamageInfo> AfflictionAbilityDamage = new();
    
    [Export]
    [Tooltip("The condition applied when the creature fails an affliction tick (e.g., Sickened).")]
    public StatusEffect_SO AfflictionCondition;
    
    [Export]
    [Tooltip("If the creature ALREADY has the condition above, apply this escalated condition instead (e.g., Nauseated).")]
    public StatusEffect_SO AfflictionEscalatedCondition;

    [ExportGroup("Death Reanimation")]
    [Export] public CreatureTemplate_SO SpawnTemplateOnDeath;
    [Export] public CreatureTemplateModifier_SO ApplyTemplateOnDeath;
    [Export] public string SpawnDelayHoursDice = "";
	
    [ExportGroup("Attack Bonus Damage")]
    [Export] public bool GrantsExtraDamageOnRangedAttacks = false;
    [Export] public bool RestrictExtraRangedDamageToProjectileWeapons = true;
    [Export] public bool RestrictExtraRangedDamageToThrownWeapons = false;
    [Export] public bool ExtraRangedDamageMultipliesOnCritical = false;
    [Export] public DamageInfo ExtraRangedDamage = new();
}

[GlobalClass]
public partial class StatModification : Resource
{
    [Export] public StatToModify StatToModify = StatToModify.None;
    [Export] public int ModifierValue = 0;
    [Export] public BonusType BonusType = BonusType.Untyped;
	[ExportGroup("Dynamic Dice Value")]
    [Export] public bool UseDiceForValue = false; // If true, ignore ModifierValue
    [Export] public int DiceCount = 1;
    [Export] public int DieSides = 6;
    [Export] public bool IsPenalty = false; // If true, result is negative
    [Export] public bool CannotReduceBelowOne = false; // Logic for Touch of Idiocy
    
    [Export] public bool IsDynamicValue = false;
    [Export] public int DynamicValueDivisor = 0;

    [Export] public TargetFilter_SO SourceFilter;
    [Export] public WeaponNameFilter_SO WeaponFilter; 
}

[GlobalClass]
public partial class ConditionalSaveBonus : Resource
{
    [Export] public SaveCondition Condition = SaveCondition.None;
    [Export] public int ModifierValue = 0;
    [Export] public BonusType BonusType = BonusType.Untyped;
}
