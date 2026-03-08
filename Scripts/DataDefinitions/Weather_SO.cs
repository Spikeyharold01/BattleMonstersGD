using Godot;

[GlobalClass]
public partial class Weather_SO : Resource
{
    [ExportGroup("Identity")]
    [Export] public string WeatherName = "Clear Skies";
    [Export(PropertyHint.MultilineText)] public string Description = "The weather is calm and clear.";

    [ExportGroup("Visuals")]
    [Export] public PackedScene VisualEffectPrefab; // Replaces GameObject

    [ExportGroup("Gameplay Effects")]
    [Export] public int PerceptionPenalty = 0;
    [Export] public int RangedAttackPenalty = 0;
    [Export] public int FlyPenalty = 0;
    [Export] public int MovementCostModifier = 0;
    
    // Enum moved to CombatEnums.cs
    [Export] public WindStrength WindStrength = WindStrength.None;

    [Export] public bool IsPrecipitation = false;

    [ExportGroup("Special Effects")]
    [Export] public string AssociatedDamageType = "None";
    [Export] public int DamagePerRound = 0;
}