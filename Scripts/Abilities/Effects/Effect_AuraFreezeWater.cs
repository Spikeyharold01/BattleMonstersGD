using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_AuraFreezeWater.cs (GODOT VERSION)
// PURPOSE: A component for an aura ability that continuously freezes nearby water nodes.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_AuraFreezeWater : AbilityEffectComponent
{
// This component is special. It doesn't have a one-time "Execute" effect.
// Instead, its presence on an active aura's Ability_SO will be checked by the AuraController.
// We can leave these methods empty as they aren't used for this passive, continuous effect.
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
// This is a continuous effect, not an instantaneous one. The logic is in AuraController.
}

public override float GetAIEstimatedValue(EffectContext context)
{
    // The value of this effect is part of the overall value of the aura ability.
    // We can add the AI logic from the Effect_FreezeWater component here for creating bridges.
    
    // AIController is a Child Node of CreatureStats root.
    var aiController = context.Caster.GetNodeOrNull<AIController>("AIController");
    if (aiController == null) return 0f;

    var primaryTarget = aiController.GetPerceivedHighestThreat();
    if (primaryTarget == null) return 5f;

    // Pathfinding.Instance is a Node (Autoload), FindPath is instance method
    var path = Pathfinding.Instance.FindPath(context.Caster, context.Caster.GlobalPosition, primaryTarget.GlobalPosition);
    if (path == null)
    {
        if (GridManager.Instance.IsWaterBetween(context.Caster.GlobalPosition, primaryTarget.GlobalPosition))
        {
            // This aura would create a path where none exists. Very valuable.
            return 150f;
        }
    }
    return 10f;
}
}