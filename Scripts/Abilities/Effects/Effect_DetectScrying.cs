using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_DetectScrying.cs
// PURPOSE: Applies a generic anti-scrying awareness controller for long-duration detection effects.
// =================================================================================================
[GlobalClass]
public partial class Effect_DetectScrying : AbilityEffectComponent
{
    [Export] public float DetectionRadiusFeet = 40f;
    [Export] public float DurationSeconds = 86400f; // 24 hours by default.
    [Export] public bool MythicAlwaysRevealScrier = true;
    [Export] public bool MythicAlwaysRevealDirectionAndDistance = true;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (context?.Caster == null) return;

        var caster = context.Caster;
        var existing = caster.GetNodeOrNull<ScryingAwarenessController>("ScryingAwarenessController");
        if (existing != null) existing.QueueFree();

        var controller = new ScryingAwarenessController();
        controller.Name = "ScryingAwarenessController";
        caster.AddChild(controller);

        bool revealScrier = context.IsMythicCast && MythicAlwaysRevealScrier;
        bool revealDirectionAndDistance = context.IsMythicCast && MythicAlwaysRevealDirectionAndDistance;

        controller.Initialize(caster, DurationSeconds, DetectionRadiusFeet, revealScrier, revealDirectionAndDistance);

        GD.PrintRich($"[color=cyan]{caster.Name} is now warded against scrying for {DurationSeconds / 3600f:0.#} hour(s).[/color]");
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        return 80f;
    }
}