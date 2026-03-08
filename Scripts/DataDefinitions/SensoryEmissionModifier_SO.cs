using Godot;

[GlobalClass]
public partial class SensoryEmissionModifier_SO : Resource
{
    [ExportGroup("Inspector Metadata")]
    [Export] public string AbilityName = "Sensory Emission Modifier";
    [Export] public string AbilityType = "Passive / Innate";
    [Export] public string Category = "Sensory Modifier";
    [Export] public Godot.Collections.Array<string> AffectedSystems = new()
    {
        "Sound emission intensity",
        "Sound positional clarity",
        "Perception confidence",
        "Location memory persistence"
    };
    [Export] public string ScalingMethod = "Value-based modifier";
    [Export(PropertyHint.Range, "0,1,0.01")] public float MaximumEffectCap = 0.35f;

    [ExportGroup("Core")]
    [Export] public string ModifierName = "Generic Sensory Emission Modifier";
    [Export] public bool IsEnabled = true;
    [Export(PropertyHint.Range, "0,5,0.01")] public float ModifierStrength = 1.0f;

    [ExportGroup("Noise Shaping")]
    [Export(PropertyHint.Range, "0.5,1.0,0.01")]
    [Tooltip("Multiplier for emitted sound intensity at full strength. Never reaches 0.")]
    public float NoiseIntensityMultiplier = 0.92f;

    [Export(PropertyHint.Range, "0.05,0.9,0.01")]
    [Tooltip("Lower bound for emitted intensity as a fraction of the pre-modifier intensity.")]
    public float MinimumIntensityFraction = 0.2f;

    [ExportGroup("Positional Ambiguity")]
    [Export(PropertyHint.Range, "0,15,0.1")]
    [Tooltip("Base positional uncertainty radius added to heard location estimates.")]
    public float PositionalUncertaintyFeet = 2.0f;

    [Export(PropertyHint.Range, "0,20,0.1")]
    [Tooltip("Hard cap on positional uncertainty to prevent teleport-like behavior.")]
    public float MaxPositionalUncertaintyFeet = 6.0f;

    [ExportGroup("Perception Confidence")]
    [Export(PropertyHint.Range, "0.5,1.0,0.01")]
    [Tooltip("Multiplier for confidence when target is detected via sound-only channels.")]
    public float SoundOnlyConfidenceMultiplier = 0.9f;

    [ExportGroup("Location Memory")]
    [Export(PropertyHint.Range, "1.0,3.0,0.01")]
    [Tooltip("How much faster sound-derived location certainty decays (1.0 = normal).")]
    public float LocationUncertaintyDecayMultiplier = 1.2f;
}