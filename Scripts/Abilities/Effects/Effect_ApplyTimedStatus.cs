using Godot;
using System;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_ApplyTimedStatus.cs
// PURPOSE: Generic timed status applier for abilities that use dice-string durations.
//          Supports "on failed save" and "on successful save" duration definitions.
// =================================================================================================
[GlobalClass]
public partial class Effect_ApplyTimedStatus : AbilityEffectComponent
{
    [Export] public StatusEffect_SO EffectToApply;

    [ExportGroup("Duration")]
    [Export] public string FailedSaveDuration = "1";
    [Export] public string SuccessfulSaveDuration = "0";

    [ExportGroup("Save Handling")]
    [Export] public bool UseAbilitySaveResult = true;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (EffectToApply == null || context?.Caster == null) return;

        foreach (var target in context.AllTargetsInAoE)
        {
            if (target == null) continue;
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;

            bool saved = false;
            if (UseAbilitySaveResult && targetSaveResults != null && targetSaveResults.TryGetValue(target, out bool didSave))
            {
                saved = didSave;
            }

            int rounds = ParseDuration(saved ? SuccessfulSaveDuration : FailedSaveDuration);
            if (rounds <= 0) continue;

            var instance = (StatusEffect_SO)EffectToApply.Duplicate();
            instance.DurationInRounds = rounds;
            target.MyEffects.AddEffect(instance, context.Caster, ability);
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (EffectToApply?.AiTacticalTag == null || context == null) return 0f;

        int validTargets = 0;
        foreach (var target in context.AllTargetsInAoE)
        {
            if (target == null) continue;
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;
            if (target.MyEffects.HasEffect(EffectToApply.EffectName)) continue;
            validTargets++;
        }

        return AIScoringEngine.ScoreTacticalTag(EffectToApply.AiTacticalTag, context, validTargets);
    }

    private static int ParseDuration(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return 0;

        string trimmed = expression.Trim();
        if (int.TryParse(trimmed, out int flatRounds)) return Math.Max(0, flatRounds);

        return Math.Max(0, Dice.Roll(trimmed));
    }
}