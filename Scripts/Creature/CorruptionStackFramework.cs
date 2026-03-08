using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Shared duration model for corruption stacks.
/// 
/// Expected output:
/// - Every corruption origin can define exactly how long its burden should last.
/// - Runtime code can process rounds, encounter cleanup, or true persistence without bespoke branches.
/// </summary>
public enum DurationType
{
    Rounds,
    Encounter,
    PersistentUntilHealed,
    Permanent
}

/// <summary>
/// Bundle of per-stack penalties authored by a corruption profile.
/// 
/// Expected output:
/// - Percentage-based penalties scale naturally with each creature's real stats.
/// - Flat penalties are avoided to keep high and low power creatures balanced proportionally.
/// </summary>
[GlobalClass]
public partial class CorruptionModifierProfile : Resource
{
    [Export(PropertyHint.Range, "0,1,0.01")] public float MaxHPPercentReductionPerStack = 0.05f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float DamagePercentReductionPerStack = 0.05f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float MoraleStabilityReductionPerStack = 0.05f;
    [Export] public int AwarenessReductionPerStack = 2;
    [Export] public int ResistanceReductionPerStack = 2;
}

/// <summary>
/// Reusable corruption effect definition.
/// 
/// Expected output:
/// - Designers can configure stack caps, healability, duration, and decay behavior per originating effect.
/// - Runtime logic can remain generic and source-driven rather than ability-name-driven.
/// </summary>
[GlobalClass]
public partial class CorruptionEffectDefinition : Resource
{
    [Export] public string CorruptionId = "SoulCorruption";
    [Export] public int MaxStacks = 12;
    [Export] public CorruptionModifierProfile Modifiers = new();
    [Export] public DurationType DurationRule = DurationType.Encounter;
    [Export] public bool IsHealable = false;
    [Export] public Godot.Collections.Array<string> HealRules = new();
    [Export] public string StackDecayRule = "None";
    [Export] public int DurationRounds = 0;
}

/// <summary>
/// Runtime payload for one source-specific corruption instance.
/// 
/// Expected output:
/// - Multiple corruption origins can coexist on one target without overwriting one another.
/// - Healing and duration updates can target a precise source when needed.
/// </summary>
public sealed class CorruptionStackInstance
{
    public long SourceID;
    public int StackCount;
    public DurationType DurationType;
    public int RemainingDuration;
    public float RemainingDurationSeconds;
    public bool IsHealable;
    public List<string> HealConditions = new();
    public CorruptionModifierProfile Modifiers;
    public string CorruptionId = "Corruption";
    public string StackDecayRule = "None";

    public float GetMaxHpPercentPenalty() => Mathf.Clamp(StackCount * (Modifiers?.MaxHPPercentReductionPerStack ?? 0f), 0f, 0.95f);
    public float GetDamagePercentPenalty() => Mathf.Clamp(StackCount * (Modifiers?.DamagePercentReductionPerStack ?? 0f), 0f, 0.95f);
    public float GetMoraleStabilityPenalty() => Mathf.Clamp(StackCount * (Modifiers?.MoraleStabilityReductionPerStack ?? 0f), 0f, 0.95f);
    public int GetAwarenessPenalty() => Mathf.Max(0, StackCount * (Modifiers?.AwarenessReductionPerStack ?? 0));
    public int GetResistancePenalty() => Mathf.Max(0, StackCount * (Modifiers?.ResistanceReductionPerStack ?? 0));
}

/// <summary>
/// Aggregated corruption snapshot for AI and simulation systems.
/// 
/// Expected output:
/// - AI systems can read one severity score for retreat/aggression decisions.
/// - Recruitment and simulation code can stay decoupled from raw stack internals.
/// </summary>
public sealed class CorruptionMetrics
{
    public int TotalCorruptionStacks;
    public float CorruptionSeverityScore;
    public List<long> CorruptionSources = new();
}
