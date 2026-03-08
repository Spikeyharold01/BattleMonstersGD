using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: TreatAfflictionEffect.cs (GODOT VERSION)
// PURPOSE: Logic for treating poison or disease with the Heal skill.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class TreatAfflictionEffect : AbilityEffectComponent
{
[Export]
[Tooltip("The status effect to grant on a successful Heal check.")]
public StatusEffect_SO EffectToApply;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    CreatureStats target = context.PrimaryTarget;
    CreatureStats caster = context.Caster;
    if (target == null || caster == null || EffectToApply == null || !ability.SkillCheck.RequiresSkillCheck) return;
    
    int dc = ability.SkillCheck.BaseDC;
    int healCheck = Dice.Roll(1, 20) + caster.GetHealBonus();

    GD.Print($"{caster.Name} attempts to Treat Poison for {target.Name} (Heal Check). Rolls {healCheck} vs DC {dc}.");
    
    if (healCheck >= dc)
    {
        GD.PrintRich($"<color=green>Success!</color> {target.Name} gains a bonus against the poison.");
        target.MyEffects.AddEffect(EffectToApply, caster);
    }
    else
    {
        GD.PrintRich($"<color=red>Failure.</color> The attempt has no effect.");
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    if (context.PrimaryTarget == null || EffectToApply == null) return 0f;
    
    var caster = context.Caster;
    var target = context.PrimaryTarget;

    if (TargetFilter != null && !TargetFilter.IsTargetValid(caster, target))
    {
        return 0f;
    }

    if (target.MyEffects.HasEffect(EffectToApply.EffectName)) return 0f;

    float baseValue = 80f; 
    
    int dc = context.Ability.SkillCheck.BaseDC;
    float successChance = (21f + caster.GetHealBonus() - dc) / 20f;

    return baseValue * Mathf.Clamp(successChance, 0f, 1f);
}
}