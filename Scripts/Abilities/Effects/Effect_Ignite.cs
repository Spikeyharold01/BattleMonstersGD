using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: Effect_Ignite.cs (GODOT VERSION)
// PURPOSE: A reusable effect component that gives a fire-based ability a chance to set targets on fire.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_Ignite : AbilityEffectComponent
{
[Export]
[Tooltip("The base DC of the Reflex save to avoid catching on fire.")]
public int SaveDC = 15;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    foreach (var target in context.AllTargetsInAoE)
    {
        if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;
        
        target.MyEffects.CatchOnFire(context.Caster, SaveDC);
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    float score = 0;
    float valuePerTarget = 50f; 

    foreach (var target in context.AllTargetsInAoE)
    {
        if (target.MyEffects.HasEffect("On Fire")) continue; 

        // AI estimates the target's chance to FAIL the Reflex save.
        int predictedReflex = target.GetReflexSave(context.Caster);
        int rollNeeded = SaveDC - predictedReflex;
        float chanceToFail = Mathf.Clamp((rollNeeded - 1f) / 20f, 0f, 1f);

        score += valuePerTarget * chanceToFail;
    }

    return score;
}
}