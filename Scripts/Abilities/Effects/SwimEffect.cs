using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: SwimEffect.cs (GODOT VERSION)
// PURPOSE: Logic for resolving the Swim skill check.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class SwimEffect : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
CreatureStats swimmer = context.Caster;
if (swimmer == null) return;

if (swimmer.Template.Speed_Swim > 0)
    {
        GD.Print($"{swimmer.Name} has a swim speed and moves without a check.");
		// Mark as attempted even if auto-success, to prevent Whirlpool sinking
        var autoSwimCtrl = swimmer.GetNodeOrNull<SwimController>("SwimController");
        if (autoSwimCtrl != null) autoSwimCtrl.HasAttemptedSwimCheck = true;
        return;
    }
	// Flag that we tried to swim (prevents auto-sink in Whirlpool)
    var swimCtrl = swimmer.GetNodeOrNull<SwimController>("SwimController");
    if (swimCtrl != null) swimCtrl.HasAttemptedSwimCheck = true;

    // Use environmental DC if higher (e.g. Whirlpool DC 25)
    int envDC = swimCtrl != null ? swimCtrl.CurrentEnvironmentalDC : 0;
    int dc = Mathf.Max(ability.SkillCheck.BaseDC, envDC);
    int swimCheck = Dice.Roll(1, 20) + swimmer.GetSkillBonus(SkillType.Swim);

    GD.Print($"{swimmer.Name} makes a Swim check. Rolls {swimCheck} vs DC {dc}.");

    if (swimCheck >= dc)
    {
        GD.PrintRich("<color=green>Success!</color> Swimmer can move.");
    }
    else if (dc - swimCheck <= 4)
    {
        GD.PrintRich($"<color=orange>Failure by 4 or less.</color> Swimmer makes no progress this turn.");
        
        var impededEffect = new StatusEffect_SO();
        impededEffect.EffectName = "Failed Swim Check";
        impededEffect.DurationInRounds = 1;
        impededEffect.ConditionApplied = Condition.Impeded; 
        swimmer.MyEffects.AddEffect(impededEffect, swimmer, ability);
    }
    else 
    {
        GD.PrintRich($"<color=red>Failure by 5 or more.</color> Swimmer goes underwater.");
        swimmer.MySwimController?.GoUnderwater();
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    return 5f; 
}
}