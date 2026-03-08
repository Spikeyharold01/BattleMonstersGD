using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_BeginSustainedTelekinesis.cs (GODOT VERSION)
// PURPOSE: Initializes the TelekinesisController on the caster for sustained usage.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_BeginSustainedTelekinesis : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
var controller = context.Caster.GetNodeOrNull<TelekinesisController>("TelekinesisController");
if (controller == null)
{
controller = new TelekinesisController();
controller.Name = "TelekinesisController";
context.Caster.AddChild(controller);
}
controller.Initialize(context.Caster);
}

public override float GetAIEstimatedValue(EffectContext context)
{
    // AI values this for its versatility. The true value is in the free actions it unlocks.
    // We'll give it a moderate base score so the AI considers setting up future maneuvers.
    return 75f;
}
}