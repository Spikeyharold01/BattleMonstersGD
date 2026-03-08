using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_ApplyColdHazard.cs (GODOT VERSION)
// PURPOSE: An effect that adds the ColdHazardController to a target for a limited duration.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_ApplyColdHazard : AbilityEffectComponent
{
[Export]
[Tooltip("The severity of the cold to apply.")]
public ColdSeverity ColdSeverity = ColdSeverity.Severe;

[Export]
[Tooltip("How many rounds the lingering cold hazard lasts.")]
public int Duration = 3;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    foreach (var target in context.AllTargetsInAoE)
    {
        if (target.GetNodeOrNull<ColdHazardController>("ColdHazardController") != null) continue; // Don't stack this controller.

        GD.Print($"{target.Name} is now subject to a lingering {ColdSeverity} cold hazard for {Duration} rounds.");
        var controller = new ColdHazardController();
        controller.Name = "ColdHazardController";
        target.AddChild(controller);
        controller.Initialize(ColdSeverity, Duration);
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    // Applying a lingering debilitating hazard is a strong tactical move.
    return 70f * context.AllTargetsInAoE.Count;
}
}