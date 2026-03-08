using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Data-driven stimulus attachment used by creatures that can influence enemy decisions.
///
/// Expected output:
/// - Designers can grant deception, distraction, or pressure tools without writing creature-specific code.
/// - Runtime systems can inspect this data and create the same behavior for any qualifying creature.
/// - Mimicry remains a cognitive nudge and never directly forces movement or a fixed tactic script.
/// </summary>
[GlobalClass]
public partial class StimulusComponent : Resource
{
    [Export] public StimulusType StimulusType = StimulusType.None;

    [Export]
    [Tooltip("How far this stimulus can be projected from the source creature.")]
    public float Range = 18f;

    [Export]
    [Tooltip("If true, the source should only project this stimulus when practical sight lines are open.")]
    public bool RequiresLineOfSight = false;

    [Export]
    [Tooltip("How strongly this stimulus shifts the target's awareness during follow-up evaluation.")]
    public float AwarenessModifier = 1f;

    [Export]
    [Tooltip("Minimum delay before this same creature can project this component again.")]
    public float CooldownSeconds = 8f;

    [Export]
    [Tooltip("Open tactical descriptors used by scoring systems. Keep these generic and system-facing.")]
    public Godot.Collections.Array<string> TacticalTags = new();
}

/// <summary>
/// Supported cognitive stimulus families.
/// </summary>
public enum StimulusType
{
    None,
    AudibleMimic
}

/// <summary>
/// Shared terrain hazard model consumed by tactical evaluators.
///
/// Expected output:
/// - Terrain names are never needed by decision logic.
/// - Any map can express danger through consistent scalar values.
/// - New hazards become immediately usable by AI as long as they publish properties.
/// </summary>
public sealed class TerrainProperties
{
    public float MovementCost;
    public float VisibilityModifier;
    public TerrainHazardType HazardType;
    public float HazardSeverity;
    public float LethalityScore;
    public HashSet<string> AppliesTo = new(StringComparer.OrdinalIgnoreCase);
}

public enum TerrainHazardType
{
    None,
    DrowningRisk,
    FireRisk,
    FallRisk,
    ToxinRisk,
    ArcaneRisk,
    GenericRisk
}

/// <summary>
/// Guardrail helper that catches architecture drift toward identity-locked or biome-locked tactics.
///
/// Expected output:
/// - Logs a violation when tactical tags encode creature names or biome names.
/// - Gives teams a single place to tighten policy without touching behavior code.
/// </summary>
public static class AICoreArchitectureGuard
{
    private static readonly string[] ForbiddenIdentityTokens =
    {
        "creature:",
        "specific_creature",
        "named_target"
    };

    private static readonly string[] ForbiddenBiomeTokens =
    {
        "biome:",
        "named_biome",
        "terrain_name"
    };

    public static void ValidateStimulusComponentTags(string ownerLabel, Godot.Collections.Array<string> tacticalTags)
    {
        if (tacticalTags == null || tacticalTags.Count == 0)
        {
            return;
        }

        foreach (string tag in tacticalTags)
        {
            string token = tag?.ToLowerInvariant() ?? string.Empty;
            foreach (string forbidden in ForbiddenIdentityTokens)
            {
                if (token.Contains(forbidden))
                {
                    GD.PushError($"[ArchitectureViolation] {ownerLabel} attempted creature-identity tactical branching via tag '{tag}'.");
                }
            }

            foreach (string forbidden in ForbiddenBiomeTokens)
            {
                if (token.Contains(forbidden))
                {
                    GD.PushError($"[ArchitectureViolation] {ownerLabel} attempted biome-identity tactical branching via tag '{tag}'.");
                }
            }
        }
    }
}
