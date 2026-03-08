using Godot;
using System;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_ApplyProtectionByEnergyType.cs
// PURPOSE: Generic and data-centric helper for typed protection spells (for example, Protection
//          from Energy). This version does NOT rely on CommandWord. Instead, each ability resource
//          declares one energy type in inspector data.
// =================================================================================================
[GlobalClass]
public partial class Effect_ApplyProtectionByEnergyType : AbilityEffectComponent
{
    [ExportGroup("Status Blueprints (one per energy type)")]
    [Export] public StatusEffect_SO AcidProtectionEffect;
    [Export] public StatusEffect_SO ColdProtectionEffect;
    [Export] public StatusEffect_SO ElectricityProtectionEffect;
    [Export] public StatusEffect_SO FireProtectionEffect;
    [Export] public StatusEffect_SO SonicProtectionEffect;

    [ExportGroup("Energy Selection")]
    [Export]
    [Tooltip("Choose the protected damage type for this ability resource. Expected values: Acid, Cold, Electricity, Fire, Sonic.")]
    public string EnergyType = "Fire";

    [ExportGroup("Duration Rules")]
    [Export]
    [Tooltip("Minutes of duration granted per caster level. Pathfinder default for this spell family is 10.")]
    public int MinutesPerCasterLevel = 10;

    [Export]
    [Tooltip("When true (communal version), total duration is split equally among touched targets in 10-minute blocks.")]
    public bool DivideDurationAcrossTargets = false;

    [Export]
    [Tooltip("Smallest distribution chunk in minutes. For communal Protection from Energy this is 10 minutes.")]
    public int DurationDistributionStepMinutes = 10;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (context?.Caster == null || context.AllTargetsInAoE == null) return;

        StatusEffect_SO selectedTemplate = GetTemplateFromEnergyType(EnergyType);
        if (selectedTemplate == null)
        {
            GD.PrintErr($"Effect_ApplyProtectionByEnergyType has no status template for EnergyType '{EnergyType}'.");
            return;
        }

        var validTargets = new List<CreatureStats>();
        foreach (var target in context.AllTargetsInAoE)
        {
            if (target == null) continue;
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;
            validTargets.Add(target);
        }

        if (validTargets.Count == 0) return;

        int casterLevel = Math.Max(1, context.Caster.Template?.CasterLevel ?? 1);
        int baseDurationMinutes = Math.Max(0, casterLevel * Math.Max(0, MinutesPerCasterLevel));

        int durationMinutesPerTarget = baseDurationMinutes;
        if (DivideDurationAcrossTargets)
        {
            int step = Math.Max(1, DurationDistributionStepMinutes);
            int availableSteps = baseDurationMinutes / step;
            int stepsPerTarget = validTargets.Count > 0 ? availableSteps / validTargets.Count : 0;
            durationMinutesPerTarget = stepsPerTarget * step;
        }

        int roundsPerTarget = Mathf.Max(0, durationMinutesPerTarget * 10);
        if (roundsPerTarget <= 0)
        {
            GD.Print("Effect_ApplyProtectionByEnergyType calculated zero rounds; no status was applied.");
            return;
        }

        foreach (var target in validTargets)
        {
            var effectInstance = (StatusEffect_SO)selectedTemplate.Duplicate();
            effectInstance.DurationInRounds = roundsPerTarget;
            target.MyEffects.AddEffect(effectInstance, context.Caster, ability);
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (context?.Caster == null || context.AllTargetsInAoE == null) return 0f;

        int validTargets = 0;
        foreach (var target in context.AllTargetsInAoE)
        {
            if (target == null) continue;
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;
            validTargets++;
        }

        return validTargets * 20f;
    }

    /// <summary>
    /// Maps inspector-selected energy text to the matching status template.
    /// Output expected: the status blueprint for that damage type, or null when unmatched.
    /// </summary>
    private StatusEffect_SO GetTemplateFromEnergyType(string energyType)
    {
        if (string.IsNullOrWhiteSpace(energyType)) return null;

        switch (energyType.Trim().ToLowerInvariant())
        {
            case "acid": return AcidProtectionEffect;
            case "cold": return ColdProtectionEffect;
            case "electricity": return ElectricityProtectionEffect;
            case "fire": return FireProtectionEffect;
            case "sonic": return SonicProtectionEffect;
            default: return null;
        }
    }
}
