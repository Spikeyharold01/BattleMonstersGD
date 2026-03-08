using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_ExtinguishFire.cs (GODOT VERSION)
// PURPOSE: Extinguishes fires and burning creatures in an area.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_ExtinguishFire : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
// This effect acts on the environment.
// Call FireManager via Instance.
FireManager.Instance?.ExtinguishFireInArea(context.AimPoint, ability.AreaOfEffect.Range);

// Also extinguish any creatures that are on fire in the area.
    foreach (var target in context.AllTargetsInAoE)
    {
        if (target.MyEffects.HasEffect("On Fire"))
        {
            GD.Print($"The spell extinguishes the flames on {target.Name}!");
            target.MyEffects.RemoveEffect("On Fire");
        }
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    // Check if there are active fires threatening itself or allies.
    // We assume FireManager.Instance is available.
    if (FireManager.Instance == null) return 0f;

    float score = 0;
    var ai = context.Caster.GetNodeOrNull<AIController>("AIController");
    if (ai == null) return 0f;

    var allies = AISpatialAnalysis.FindAllies(context.Caster);
    foreach (var ally in allies)
    {
        if (FireManager.Instance.IsPositionOnFire(ally.GlobalPosition)) score += 100f;
        if (FireManager.Instance.IsPositionSmoky(ally.GlobalPosition)) score += 50f;
    }
    
    return score;
}
}