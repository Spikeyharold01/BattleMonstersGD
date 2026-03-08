using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: RollTwiceEffect.cs (GODOT VERSION)
// PURPOSE: A mythic effect component that grants a one-time "roll twice, take higher" benefit.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class RollTwiceEffect : MythicAbilityEffectComponent
{
public override void ExecuteMythicEffect(EffectContext context, Ability_SO ability)
{
foreach (var target in context.AllTargetsInAoE)
{
if (target != null)
{
var controller = target.GetNodeOrNull<OneTimeEffectController>("OneTimeEffectController");
if (controller == null)
{
// If component missing, add it dynamically (as Child Node)
controller = new OneTimeEffectController();
controller.Name = "OneTimeEffectController";
target.AddChild(controller);
}

controller.AddRollTwiceCharge(ResourceName); // Use ResourceName
        }
    }
}

public override string GetMythicDescription()
{
    return "Once during the duration, an affected creature can roll an attack roll or saving throw twice and take the higher result.";
}
}