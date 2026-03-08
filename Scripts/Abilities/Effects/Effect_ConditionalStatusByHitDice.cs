using Godot;
using System;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_ConditionalStatusByHitDice.cs
// PURPOSE:
// A reusable, inspector-driven effect that applies one status on a failed save and optionally a
// different status on a successful save, while also enforcing a configurable Hit Dice limit.
//
// WHY THIS EXISTS:
// Some spells (like Cause Fear) have a "partial" save outcome and also stop working above a
// specific HD threshold. This component keeps that behavior data-driven so designers can tune the
// spell directly in the Godot inspector without changing importer/database code.
// =================================================================================================
[GlobalClass]
public partial class Effect_ConditionalStatusByHitDice : AbilityEffectComponent
{
    [ExportGroup("Hit Dice Gate")]
    [Export]
    [Tooltip("Maximum HD a target can have to be affected at all. Example: Cause Fear uses 5.")]
    public int MaximumTargetHitDice = 5;

    [Export]
    [Tooltip("When enabled, only living creatures are considered valid targets for this effect.")]
    public bool AffectLivingCreaturesOnly = true;

    [ExportGroup("Failed Save Outcome")]
    [Export]
    [Tooltip("Status to apply when the target fails the save (full effect).")]
    public StatusEffect_SO FailedSaveStatus;

    [Export]
    [Tooltip("Duration formula for failed save (for example: '1d4').")]
    public string FailedSaveDurationFormula = "1d4";

    [ExportGroup("Successful Save Outcome")]
    [Export]
    [Tooltip("Status to apply when the target succeeds on the save (partial effect).")]
    public StatusEffect_SO SuccessfulSaveStatus;

    [Export]
    [Tooltip("Duration formula for successful save (for example: '1'). Set to '0' for none.")]
    public string SuccessfulSaveDurationFormula = "1";

    [Export]
    [Tooltip("If true, no status is applied on a successful save, regardless of other fields.")]
    public bool NegateEffectOnSuccessfulSave = false;

    [ExportGroup("Execution Scope")]
    [Export]
    [Tooltip("When true, resolves against all targets in context. When false, uses only PrimaryTarget.")]
    public bool UseAllTargetsInAoE = false;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        // Defensive checks prevent null reference issues in unusual runtime states.
        if (context == null || context.Caster == null)
        {
            GD.PrintErr("Effect_ConditionalStatusByHitDice aborted: missing context or caster.");
            return;
        }

        // Build the final list of targets from either the primary target or current AoE list.
        // This keeps the component reusable for single-target and multi-target spells.
        var targets = new List<CreatureStats>();
        if (UseAllTargetsInAoE)
        {
            foreach (var t in context.AllTargetsInAoE)
            {
                if (t != null) targets.Add(t);
            }
        }
        else if (context.PrimaryTarget != null)
        {
            targets.Add(context.PrimaryTarget);
        }

        // Process each target independently so save outcomes and immunities can differ.
        foreach (var target in targets)
        {
            // Respect optional target filter so this effect behaves consistently with other components.
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target))
                continue;

            // Cause Fear and similar effects only function on living creatures.
            // This flag allows that rule without hardcoding a specific spell name.
            if (AffectLivingCreaturesOnly && !IsLivingCreature(target))
            {
                GD.Print($"{target.Name} ignored: target is not a living creature.");
                continue;
            }

            // Hit Dice gate: creatures above the configured limit are fully immune.
            int targetHitDice = CreatureRulesUtility.GetHitDiceCount(target, fallback: 1);
            if (targetHitDice > MaximumTargetHitDice)
            {
                GD.Print($"{target.Name} ignored: {targetHitDice} HD is above limit {MaximumTargetHitDice}.");
                continue;
            }

            // Read save result from precomputed map when available.
            // If absent, we treat it as a failed save so the effect still resolves predictably.
            bool didSave = targetSaveResults != null && targetSaveResults.TryGetValue(target, out bool saved) && saved;

            // Successful save branch.
            if (didSave)
            {
                if (NegateEffectOnSuccessfulSave)
                    continue;

                ApplyStatusFromFormula(
                    target,
                    context.Caster,
                    ability,
                    SuccessfulSaveStatus,
                    SuccessfulSaveDurationFormula,
                    "successful"
                );

                continue;
            }

            // Failed save branch (full effect).
            ApplyStatusFromFormula(
                target,
                context.Caster,
                ability,
                FailedSaveStatus,
                FailedSaveDurationFormula,
                "failed"
            );
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        // AI estimate should reward landing fear-like control on enemies and discourage friendly fire.
        if (context == null || context.Caster == null) return 0f;

        float score = 0f;
        foreach (var target in context.AllTargetsInAoE)
        {
            if (target == null || target == context.Caster) continue;
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;
            if (AffectLivingCreaturesOnly && !IsLivingCreature(target)) continue;

            int hd = CreatureRulesUtility.GetHitDiceCount(target, fallback: 1);
            if (hd > MaximumTargetHitDice) continue;

            bool isEnemy = target.IsInGroup("Player") != context.Caster.IsInGroup("Player");
            float perTargetValue = 0f;

            // Failed-save status is the primary value driver.
            if (FailedSaveStatus?.AiTacticalTag != null)
            {
                perTargetValue += AIScoringEngine.ScoreTacticalTag(FailedSaveStatus.AiTacticalTag, context, 1);
            }
            else
            {
                // Fallback value keeps AI from treating uncategorized statuses as worthless.
                perTargetValue += 40f;
            }

            // Partial effect on successful save contributes a smaller amount of value.
            if (!NegateEffectOnSuccessfulSave && SuccessfulSaveStatus?.AiTacticalTag != null)
            {
                perTargetValue += AIScoringEngine.ScoreTacticalTag(SuccessfulSaveStatus.AiTacticalTag, context, 1) * 0.35f;
            }

            score += isEnemy ? perTargetValue : -perTargetValue;
        }

        return score;
    }

    // This helper converts a text duration formula into rounds and applies a duplicated status.
    // Expected output:
    // - If formula resolves to 0 or less: no status is applied.
    // - If formula resolves to 1 or higher: status is added for that many rounds.
    private static void ApplyStatusFromFormula(
        CreatureStats target,
        CreatureStats caster,
        Ability_SO ability,
        StatusEffect_SO status,
        string durationFormula,
        string saveResultLabel)
    {
        if (target == null || caster == null || ability == null || status == null)
            return;

        int rounds = Math.Max(0, Dice.Roll(durationFormula));
        if (rounds <= 0)
        {
            GD.Print($"{target.Name} {saveResultLabel} save; duration resolved to 0, no status applied.");
            return;
        }

        var instance = (StatusEffect_SO)status.Duplicate();
        instance.DurationInRounds = rounds;
        target.MyEffects.AddEffect(instance, caster, ability);

        GD.Print($"{target.Name} {saveResultLabel} save; applied {instance.EffectName} for {rounds} rounds.");
    }

    // Living-creature check used by many fear/charm effects.
    // Expected output:
    // - true  => creature can be treated as biologically alive for targeting rules.
    // - false => creature is excluded when living-only gating is enabled.
    private static bool IsLivingCreature(CreatureStats target)
    {
        if (target?.Template == null) return false;

        return target.Template.Type != CreatureType.Undead && target.Template.Type != CreatureType.Construct;
    }
}
