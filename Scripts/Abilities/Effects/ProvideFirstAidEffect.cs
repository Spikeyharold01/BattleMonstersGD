using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: ProvideFirstAidEffect.cs (GODOT VERSION)
// PURPOSE: An effect component for stabilizing a dying creature using the Heal skill.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class ProvideFirstAidEffect : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
CreatureStats target = context.PrimaryTarget;
CreatureStats caster = context.Caster;
if (target == null || caster == null || !ability.SkillCheck.RequiresSkillCheck) return;

if (TargetFilter != null && !TargetFilter.IsTargetValid(caster, target))
    {
        GD.Print($"Provide First Aid failed: {target.Name} is not a valid target (not dying).");
        return;
    }

    int healCheck = Dice.Roll(1, 20) + caster.GetHealBonus();
    int dc = ability.SkillCheck.BaseDC;

    GD.Print($"{caster.Name} attempts to Provide First Aid to {target.Name} (Heal Check). Rolls {healCheck} vs DC {dc}.");

    if (healCheck >= dc)
    {
        target.Stabilize();
        GD.PrintRich($"<color=green>Success! {target.Name} is now stable.</color>");
    }
    else
    {
        GD.PrintRich($"<color=red>Failure. The attempt to stabilize {target.Name} has no effect.</color>");
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    if (context.PrimaryTarget == null) return 0;
    var target = context.PrimaryTarget;

    if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target))
    {
        return 0f;
    }

    if (!target.IsStable)
    {
        float successChance = Mathf.Clamp((21f + context.Caster.GetHealBonus() - 15) / 20f, 0f, 1f); 
        return 400f * successChance;
    }

    return 0f;
}
}