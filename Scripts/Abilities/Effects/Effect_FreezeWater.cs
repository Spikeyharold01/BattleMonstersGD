using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_FreezeWater.cs (GODOT VERSION)
// PURPOSE: A reusable effect component that turns water nodes into ice nodes.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_FreezeWater : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
GridManager.Instance?.FreezeWaterInArea(context.AimPoint, ability.AreaOfEffect.Range);
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var aiController = context.Caster.GetNodeOrNull<AIController>("AIController");
    if (aiController == null) return 0f;

    var primaryTarget = aiController.GetPerceivedHighestThreat();
    if (primaryTarget == null) return 5f; 

    var path = Pathfinding.Instance.FindPath(context.Caster, context.Caster.GlobalPosition, primaryTarget.GlobalPosition);
    if (path == null || path.Count > 10) 
    {
        if (GridManager.Instance.IsWaterBetween(context.Caster.GlobalPosition, primaryTarget.GlobalPosition))
        {
            return 150f;
        }
    }
    
    return 10f; 
}
}