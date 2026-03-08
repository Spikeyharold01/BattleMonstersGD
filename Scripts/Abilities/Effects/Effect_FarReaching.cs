using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_FarReaching.cs (GODOT VERSION)
// PURPOSE: Toggles the "Far Reaching" stance.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_FarReaching : AbilityEffectComponent
{
[Export]
[Tooltip("The StatusEffect that represents the stance.")]
public StatusEffect_SO FarReachingStance;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    var caster = context.Caster;
    
    // Toggle Logic
    if (caster.MyEffects.HasConditionStr("FarReachingStance"))
    {
        GD.Print($"{caster.Name} deactivates Far Reaching.");
        caster.MyEffects.RemoveEffect("Far Reaching Stance");
    }
    else
    {
        GD.Print($"{caster.Name} activates Far Reaching! Reach extends, movement disabled.");
        var instance = (StatusEffect_SO)FarReachingStance.Duplicate();
        instance.EffectName = "Far Reaching Stance"; // Must match Remove key
        caster.MyEffects.AddEffect(instance, caster);
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var caster = context.Caster;
    var controller = caster.GetNodeOrNull<AIController>("AIController");
    if (controller == null) return 0f;

    bool isActive = caster.MyEffects.HasConditionStr("FarReachingStance");
    var enemies = AISpatialAnalysis.FindVisibleTargets(caster);
    
    float closestDist = float.MaxValue;
    foreach(var e in enemies)
    {
        float d = caster.GlobalPosition.DistanceTo(e.GlobalPosition);
        if (d < closestDist) closestDist = d;
    }

    // Logic:
    if (isActive)
    {
        if (closestDist < 20f) return 100f; // High score to toggle OFF
        return 0f; // Low score to keep ON
    }
    else
    {
        if (closestDist > caster.Template.Reach) return 100f; // High score to toggle ON
        return 0f;
    }
}
}