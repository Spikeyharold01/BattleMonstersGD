using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_ConcentrateTelekinesis.cs (GODOT VERSION)
// PURPOSE: Refreshes the duration/concentration of an active Telekinesis effect.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_ConcentrateTelekinesis : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
context.Caster.GetNodeOrNull<TelekinesisController>("TelekinesisController")?.RefreshConcentration();
}

public override float GetAIEstimatedValue(EffectContext context)
{
    // The value of this action is in what it *enables*. The AI will plan a (Concentrate -> Free Action) turn.
    // This action itself has a low score; the free action it's paired with will have the high score.
    return 5f;
}
}