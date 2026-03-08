using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: DetectMagicEffect.cs (GODOT VERSION)
// PURPOSE: An effect component for initiating the Detect Magic spell.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class DetectMagicEffect : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
// This spell affects the caster, not the target.
var controller = context.Caster.GetNodeOrNull<DetectMagicController>("DetectMagicController");
if (controller == null)
{
controller = new DetectMagicController();
controller.Name = "DetectMagicController";
context.Caster.AddChild(controller);
}
controller.Initialize(context.Caster);
// The first round of information is revealed immediately upon casting.
controller.Concentrate();
}

public override float GetAIEstimatedValue(EffectContext context)
{
     // AI currently doesn't know how to value pure information.
    // Give it a very low score so it's only chosen if there's nothing better to do.
    if (context.Caster.GetNodeOrNull<DetectMagicController>("DetectMagicController") != null) return -1f; // Don't cast if already active

    // AI is "curious". It wants to detect magic on creatures it hasn't identified yet.
    var aiController = context.Caster.GetNodeOrNull<AIController>("AIController");
    if (aiController == null) return 1f;

    // Access static helper on AISpatialAnalysis
    int unknownTargets = AISpatialAnalysis.FindVisibleTargets(context.Caster).Count(t => !CombatMemory.IsKnownToBeMagical(t));
    
    // The more unknown targets, the more valuable it is to cast Detect Magic.
    // The base value is low, so it won't do this over a powerful attack, but it's now a viable tactical choice.
    return 5f * unknownTargets;
}
}