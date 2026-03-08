// =================================================================================================
// FILE: Ability_SO.cs (Godot C# Version)
// PURPOSE: Resource defining a single Ability or Spell.
// REVISED: Enums moved to CombatEnums.cs
// =================================================================================================
using Godot;
using System.Collections.Generic;

// [GlobalClass] allows us to create this resource in the editor
[GlobalClass]
public partial class RangeInfo : Resource
{
    // Uses the global enum from CombatEnums.cs
    [Export] public RangeType Type = RangeType.Custom;
    
    [Export(PropertyHint.Range, "0,2000,1")] 
    public float CustomRangeInFeet = 5f;

    public float GetRange(CreatureStats caster)
    {
        if (caster == null || caster.Template == null) return CustomRangeInFeet;
        return GetRange(caster.Template.CasterLevel);
    }

    public float GetRange(int casterLevel)
    {
        switch (Type)
        {
            case RangeType.Self: return 0f;
            case RangeType.Touch: return 5f;
            case RangeType.Close: return 25f + (Mathf.Floor(casterLevel / 2f) * 5f);
            case RangeType.Medium: return 100f + (casterLevel * 10f);
            case RangeType.Long: return 400f + (casterLevel * 40f);
            case RangeType.Custom:
            default:
                return CustomRangeInFeet;
        }
    }
}

[GlobalClass]
public partial class Ability_SO : Resource
{
    [ExportGroup("Core Info")]
    [Export] public string AbilityName;
    [Export(PropertyHint.MultilineText)] public string DescriptionForTooltip;
    
    [Export] public bool IsImplemented = true;
    [Export] public int SpellLevel = 0;
    
    [Export] public SpecialAbilityType SpecialAbilityType = SpecialAbilityType.None;

    [ExportGroup("Classification")]
    [Export] public AbilityCategory Category = AbilityCategory.Spell;
    [Export] public MagicSchool School = MagicSchool.Universal;
    [Export] public SkillType RequiredSkill = SkillType.Acrobatics;
    [Export] public int MinSkillRanksRequired = 1;

    [ExportGroup("Usage & Targeting")]
    [Export] public ActionType ActionCost = ActionType.Standard;
    [Export] public TargetType TargetType = TargetType.SingleEnemy;
    
    [Export] public AttackRollType AttackRollType = AttackRollType.None;
    [Export] public TargetableEntityType EntityType = TargetableEntityType.CreaturesOnly;
    
    [Export] public RangeInfo Range; 
    
    [Export] public bool AllowsSpellResistance = true;
    [Export] public bool IsLanguageDependent = false;

    [ExportGroup("Target Eligibility & Save Adjustments")]
    [Export] public int MinimumTargetIntelligenceToAffect = 0;
    [Export] public int SaveBonusWhenTargetTypeDiffersFromCaster = 0;
    
    [Export] public Godot.Collections.Array<CommandWord> AvailableCommands = new();
    
    [Export] public AreaOfEffect AreaOfEffect; 
    
    [Export] public DurationData AuraEffectDuration; 
    
    [Export] public SavingThrowInfo SavingThrow; 
    [Export] public UsageLimitation Usage; 
    
    [Export] public int PsychicEnergyCost = 0;

    [ExportGroup("Skill Check")]
    [Export] public SkillCheckInfo SkillCheck; 

    [ExportGroup("Casting Components")]
    [Export] public bool IsMythicCapable = false;
    [Export] public SpellComponentInfo Components; 
    
    [ExportGroup("Perception & Signature")]
    [Export] public bool EmitsAudibleAction = false;
    [Export] public SoundActionType AudibleActionType = SoundActionType.Cast;
    [Export] public float AudibleDurationSeconds = 1.5f;
    [Export] public bool BreakStealthOnAudibleAction = true;

    [ExportGroup("Effect Components")]
    [Export] public Ability_SO SharesUsageWith = null;
    
    [Export] public Godot.Collections.Array<AbilityEffectComponent> EffectComponents = new();

    [ExportGroup("Mythic Version")]
    [Export] public Godot.Collections.Array<MythicAbilityEffectComponent> MythicComponents = new();
}

[GlobalClass]
public partial class DurationData : Resource
{
    [Export] public int BaseRounds = 0;
    [Export] public int DiceCount = 0;
    [Export] public int DieSides = 0;

    public int GetDurationInRounds()
    {
        int totalRounds = BaseRounds;
        if (DiceCount > 0 && DieSides > 0)
        {
            totalRounds += Dice.Roll(DiceCount, DieSides);
        }
        return totalRounds;
    }
}
