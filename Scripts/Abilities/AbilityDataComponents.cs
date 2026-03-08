// =================================================================================================
// FILE: AbilityDataComponents.cs (Godot C# Version)
// PURPOSE: Holds various data structures used by Ability_SO.
// =================================================================================================
using Godot;
using System;

[GlobalClass]
public partial class AreaOfEffect : Resource
{
    [Export] public AoEShape Shape = AoEShape.Burst;
    [Export] public float Range = 0f; 
    [Export] public float Angle = 0f;
    [Export] public float Width = 0f;
    [Export] public float Height = 0f;

    [ExportGroup("Optional Alternate Mode")]
    [Export] public bool UseAlternateWhenCenteredOnCaster = false;
    [Export] public float AlternateActivationDistanceFeet = 5f;
    [Export] public AoEShape AlternateShape = AoEShape.Burst;
    [Export] public float AlternateRange = 0f;
    [Export] public float AlternateAngle = 0f;
    [Export] public float AlternateWidth = 0f;
    [Export] public float AlternateHeight = 0f;
}

[GlobalClass]
public partial class DamageInfo : Resource
{
    [Export] public int DiceCount = 1;
    [Export] public int DieSides = 6;
    [Export] public int FlatBonus = 0;
    [Export] public string DamageType = "Physical";
    [Export] public bool MultipliesOnCrit = true;
}

[GlobalClass]
public partial class SavingThrowInfo : Resource
{
    [Export] public SaveType SaveType = SaveType.None;
    [Export] public int BaseDC = 10;
    [Export] public bool IsDynamicDC = false;
    [Export] public AbilityScore DynamicDCStat = AbilityScore.None;
    [Export] public bool IsSpecialAbilityDC = false;
	[Export] public int DynamicDCBonus = 0;
    [Export] public SaveEffect EffectOnSuccess = SaveEffect.None;
}

[GlobalClass]
public partial class UsageLimitation : Resource
{
    [Export] public UsageType Type = UsageType.AtWill;
    [Export] public int UsesPerDay = 0;
    [Export] public bool IsDynamicUses = false;
    [Export] public AbilityScore DynamicUsesStat = AbilityScore.None;
    [Export] public int DynamicUsesBase = 0;
	[Export] public int DynamicUsesCasterLevelDivisor = 0;
    [Export] public int DynamicUsesCasterLevelMultiplier = 1;
    [Export] public int MinimumUsesPerDay = 0;
    [Export] public int CooldownDiceSides = 0;
    [Export] public string CooldownDurationDice = "1d4";
}

[GlobalClass]
public partial class SpellComponentInfo : Resource
{
    [Export] public bool HasVerbal = false;
    [Export] public bool HasSomatic = false;
    [Export] public bool HasMaterial = false;
    [Export] public bool HasFocus = false;
    [Export] public bool HasDivineFocus = false;
}

[GlobalClass]
public partial class SkillCheckInfo : Resource
{
    [Export] public bool RequiresSkillCheck = false;
    [Export] public SkillType SkillToUse = SkillType.Heal;
    [Export] public DCRule DcRule = DCRule.BaseDC;
    [Export] public int BaseDC = 15;
}