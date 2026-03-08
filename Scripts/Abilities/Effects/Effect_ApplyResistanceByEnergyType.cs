using Godot;
using System;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_ApplyResistanceByEnergyType.cs
// PURPOSE: A reusable, inspector-driven effect component that applies a temporary resistance buff
//          to one selected energy type (acid, cold, electricity, fire, or sonic).
//
// DESIGN NOTES:
// - This script is intentionally generic so it can support many spells, not just Resist Energy.
// - Designers choose the energy type and progression numbers directly in inspector data.
// - The script duplicates a shared status blueprint, then injects per-cast values
//   (duration and resistance amount) into that runtime copy.
// =================================================================================================

[GlobalClass]
public partial class Effect_ApplyResistanceByEnergyType : AbilityEffectComponent
{
    [ExportGroup("Status Blueprint")]
    [Export]
    [Tooltip("Base status resource to duplicate. The duplicate receives duration, resisted type, and final resistance amount at cast time.")]
    public StatusEffect_SO ResistanceStatusTemplate;

    [ExportGroup("Energy Selection")]
    [Export]
    [Tooltip("Energy type granted by this ability resource. Expected values: Acid, Cold, Electricity, Fire, Sonic.")]
    public string EnergyType = "Fire";

    [ExportGroup("Duration Rules")]
    [Export]
    [Tooltip("Minutes granted per caster level. Standard Resist Energy uses 10.")]
    public int MinutesPerCasterLevel = 10;

    [Export]
    [Tooltip("When enabled (communal-style casting), total minutes are split into fixed blocks across all valid targets.")]
    public bool DivideDurationAcrossTargets = false;

    [Export]
    [Tooltip("Minimum block size, in minutes, used when duration is divided.")]
    public int DurationDistributionStepMinutes = 10;

    [ExportGroup("Resistance Progression")]
    [Export]
    [Tooltip("Resistance value granted below the first threshold level. Standard value is 10.")]
    public int BaseResistanceAmount = 10;

    [Export]
    [Tooltip("Caster level where the resistance improves to the mid value. Standard threshold is 7.")]
    public int MidTierCasterLevel = 7;

    [Export]
    [Tooltip("Resistance value granted once the mid threshold is reached. Standard value is 20.")]
    public int MidTierResistanceAmount = 20;

    [Export]
    [Tooltip("Caster level where the resistance improves to the high value. Standard threshold is 11.")]
    public int HighTierCasterLevel = 11;

    [Export]
    [Tooltip("Resistance value granted once the high threshold is reached. Standard value is 30.")]
    public int HighTierResistanceAmount = 30;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        // Guard rails: if critical cast context is missing, we quietly stop.
        // Output expected: no application and no runtime crash.
        if (context?.Caster == null || context.AllTargetsInAoE == null) return;

        if (ResistanceStatusTemplate == null)
        {
            GD.PrintErr("Effect_ApplyResistanceByEnergyType is missing ResistanceStatusTemplate.");
            return;
        }

        string normalizedEnergyType = NormalizeEnergyType(EnergyType);
        if (string.IsNullOrEmpty(normalizedEnergyType))
        {
            GD.PrintErr($"Effect_ApplyResistanceByEnergyType received unsupported EnergyType '{EnergyType}'.");
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
        int resistanceAmount = ResolveResistanceAmountByLevel(casterLevel);

        int baseDurationMinutes = Math.Max(0, casterLevel * Math.Max(0, MinutesPerCasterLevel));
        int durationMinutesPerTarget = baseDurationMinutes;

        // Communal-style behavior:
        // We convert total minutes into fixed blocks and split them as evenly as possible.
        // Output expected: each target receives the same rounded-down duration block.
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
            GD.Print("Effect_ApplyResistanceByEnergyType calculated zero rounds; no status was applied.");
            return;
        }

        foreach (var target in validTargets)
        {
            // Each target receives its own duplicated copy so runtime changes stay isolated.
            var effectInstance = (StatusEffect_SO)ResistanceStatusTemplate.Duplicate();
            effectInstance.DurationInRounds = roundsPerTarget;
            effectInstance.ResistDamageTypes = new Godot.Collections.Array<string> { normalizedEnergyType };
            effectInstance.DamageResistanceAmount = resistanceAmount;

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

        int casterLevel = Math.Max(1, context.Caster.Template?.CasterLevel ?? 1);
        int resistanceAmount = ResolveResistanceAmountByLevel(casterLevel);

        // Heuristic: stronger prevention and more covered allies both improve tactical score.
        return validTargets * resistanceAmount * 1.5f;
    }

    /// <summary>
    /// Converts free-text inspector input to canonical damage type labels used by combat resolution.
    /// Output expected: "Acid", "Cold", "Electricity", "Fire", or "Sonic".
    /// </summary>
    private string NormalizeEnergyType(string energyType)
    {
        if (string.IsNullOrWhiteSpace(energyType)) return null;

        switch (energyType.Trim().ToLowerInvariant())
        {
            case "acid": return "Acid";
            case "cold": return "Cold";
            case "electricity": return "Electricity";
            case "fire": return "Fire";
            case "sonic": return "Sonic";
            default: return null;
        }
    }

    /// <summary>
    /// Resolves the final resistance amount from caster level using a simple three-tier progression.
    /// Output expected: base amount, mid amount, or high amount depending on thresholds.
    /// </summary>
    private int ResolveResistanceAmountByLevel(int casterLevel)
    {
        if (casterLevel >= HighTierCasterLevel)
        {
            return Math.Max(0, HighTierResistanceAmount);
        }

        if (casterLevel >= MidTierCasterLevel)
        {
            return Math.Max(0, MidTierResistanceAmount);
        }

        return Math.Max(0, BaseResistanceAmount);
    }
}
