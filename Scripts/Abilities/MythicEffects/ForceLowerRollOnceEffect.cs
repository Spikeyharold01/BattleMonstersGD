using Godot;

// =================================================================================================
// FILE: ForceLowerRollOnceEffect.cs
// PURPOSE:
// A reusable mythic component that marks affected creatures with a one-time "roll twice, keep lower"
// penalty. This supports effects like Mythic Bane without tying behavior to one specific spell.
//
// DESIGN GOALS:
// - Data centric: behavior is controlled from inspector data and target filters.
// - Generic: can be reused by other abilities that need a one-time reliability penalty.
// - Minimal coupling: relies only on existing EffectContext target lists and OneTimeEffectController.
// =================================================================================================

[GlobalClass]
public partial class ForceLowerRollOnceEffect : MythicAbilityEffectComponent
{
    [Export]
    [Tooltip("If true, only targets that failed their save are marked. If false, all valid targets are marked.")]
    public bool OnlyTargetsThatFailedSave = true;

    [Export]
    [Tooltip("Optional extra filter for who receives the one-time penalty.")]
    public TargetFilter_SO AdditionalTargetFilter;

    /// <summary>
    /// Applies a one-time forced lower-roll marker to selected targets.
    ///
    /// Expected outcome for designers:
    /// - Targets receiving the marker will roll their next attack roll or saving throw twice.
    /// - The lower die result is used.
    /// - The marker is consumed automatically after that roll.
    /// </summary>
    public override void ExecuteMythicEffect(EffectContext context, Ability_SO ability)
    {
        if (context == null || context.AllTargetsInAoE == null || context.Caster == null)
        {
            GD.PrintErr("ForceLowerRollOnceEffect could not run because required effect context data was missing.");
            return;
        }

        foreach (var target in context.AllTargetsInAoE)
        {
            if (target == null) continue;
            if (AdditionalTargetFilter != null && !AdditionalTargetFilter.IsTargetValid(context.Caster, target)) continue;

            if (OnlyTargetsThatFailedSave && context.LastSaveResults != null && context.LastSaveResults.ContainsKey(target) && context.LastSaveResults[target])
            {
                // The target succeeded on its save. For this mode, we intentionally skip it.
                continue;
            }

            var controller = target.GetNodeOrNull<OneTimeEffectController>("OneTimeEffectController");
            if (controller == null)
            {
                // We add the node dynamically so this component can operate on creatures
                // that were authored before the one-time controller became standard.
                controller = new OneTimeEffectController();
                controller.Name = "OneTimeEffectController";
                target.AddChild(controller);
            }

            controller.AddForcedLowerRollCharge(ability != null ? ability.AbilityName : ResourceName);
        }
    }

    /// <summary>
    /// Human-readable text used by UI/tooltip systems for mythic summary lines.
    /// </summary>
    public override string GetMythicDescription()
    {
        return "Each affected creature must roll its next attack roll or saving throw twice and use the lower result.";
    }
}
