using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: StabilizeEffect.cs (GODOT VERSION)
// PURPOSE: Logic for stabilizing a dying creature.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class StabilizeEffect : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
CreatureStats target = context.PrimaryTarget;

if (target != null)
    {
        if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target))
        {
            GD.Print($"Stabilize failed: {target.Name} is not a valid target (not dying).");
            return;
        }

        target.Stabilize();
        GD.PrintRich($"<color=green>{context.Caster.Name} casts Stabilize on {target.Name}. They are now stable.</color>");
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    if (context.PrimaryTarget == null) return 0;

    var target = context.PrimaryTarget;

    bool isDyingAndUnstable = target.CurrentHP < 0 && !target.IsStable;
    
     if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target))
    {
        return 0f;
    }

    if (isDyingAndUnstable)
    {
        return 300f; 
    }

    return 0f;
}
}