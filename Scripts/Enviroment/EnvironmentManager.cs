using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: EnvironmentManager.cs (GODOT VERSION)
// PURPOSE: Applies penalties to creatures fighting outside their natural environment and manages biome rules.
// ATTACH TO: A persistent "GameManager" Node.
// =================================================================================================
public static class BiomeEffects
{
public static StatusEffect_SO WaterBreathingEffect;

static BiomeEffects()
{
    WaterBreathingEffect = new StatusEffect_SO();
    WaterBreathingEffect.EffectName = "Water Breathing (Environment)";
    WaterBreathingEffect.Description = "This creature can breathe water freely due to environmental magic.";
    WaterBreathingEffect.DurationInRounds = 0; // Permanent for the combat
}
}
public partial class EnvironmentManager : GridNode
{
public static EnvironmentManager Instance { get; private set; }

[ExportGroup("Scene Settings")]
[Export]
[Tooltip("The environmental properties for the current scene or level.")]
public Godot.Collections.Array<EnvironmentProperty> CurrentSceneProperties = new();

[Export]
[Tooltip("Is the combat in this scene taking place underwater? This is a special flag for applying breathing/movement rules.")]
public bool IsUnderwaterCombat = false;

// --- Cached Status Effects for performance ---
private StatusEffect_SO aquaticOnLandEffect;
private StatusEffect_SO landCreatureUnderwaterEffect;
private StatusEffect_SO minorMismatchEffect;
private StatusEffect_SO moderateMismatchEffect;
private StatusEffect_SO severeMismatchEffect;

public override void _Ready()
{
    if (Instance != null && Instance != this) 
    {
        QueueFree();
    }
    else 
    {
        Instance = this;
    }
    
    // Pre-load the status effects
    if(ResourceLoader.Exists("res://Data/StatusEffects/SE_AquaticOnLand.tres"))
        aquaticOnLandEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/SE_AquaticOnLand.tres");
    if(ResourceLoader.Exists("res://Data/StatusEffects/SE_LandCreatureUnderwater.tres"))
        landCreatureUnderwaterEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/SE_LandCreatureUnderwater.tres");
    if(ResourceLoader.Exists("res://Data/StatusEffects/SE_BiomeMismatch_Minor.tres"))
        minorMismatchEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/SE_BiomeMismatch_Minor.tres");
    if(ResourceLoader.Exists("res://Data/StatusEffects/SE_BiomeMismatch_Moderate.tres"))
        moderateMismatchEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/SE_BiomeMismatch_Moderate.tres");
    if(ResourceLoader.Exists("res://Data/StatusEffects/SE_BiomeMismatch_Severe.tres"))
        severeMismatchEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/SE_BiomeMismatch_Severe.tres");
}

// This method is called by the TurnManager when combat begins.
// TurnManager should call this manually.
public void OnCombatStart(List<CreatureStats> allCombatants)
{
    if ((CurrentSceneProperties == null || CurrentSceneProperties.Contains(EnvironmentProperty.Any)) && !IsUnderwaterCombat) return;

    foreach (var creature in allCombatants)
    {
        ApplyBiomeSpecificRules(creature);
        ApplyEnvironmentMismatch(creature);
        ApplyColdHazard(creature);
        ApplyHeatHazard(creature);
    }
}

private void ApplyBiomeSpecificRules(CreatureStats creature)
{
    bool isNativeAquatic = creature.Template.NaturalEnvironmentProperties != null && creature.Template.NaturalEnvironmentProperties.Contains(EnvironmentProperty.Aquatic);

    if (IsUnderwaterCombat)
    {
         // A non-aquatic creature is now underwater. Grant them Water Breathing.
        if (!isNativeAquatic)
        {
            GD.PrintRich($"[color=blue]{creature.Name} is a non-aquatic creature in an underwater biome. Granting Water Breathing.[/color]");
            creature.MyEffects.AddEffect(BiomeEffects.WaterBreathingEffect, null);

            // They still suffer other penalties for being a land creature underwater.
            if (landCreatureUnderwaterEffect != null)
            {
                creature.MyEffects.AddEffect((StatusEffect_SO)landCreatureUnderwaterEffect.Duplicate(), null);
            }
        }
    }
    else // Combat is on land/air
    {
        // An aquatic creature is now out of the water.
        if (isNativeAquatic)
        {
            GD.PrintRich($"[color=green]{creature.Name} is an aquatic creature on land. Granting flight but applying penalties.[/color]");
            if (aquaticOnLandEffect != null)
            {
                creature.MyEffects.AddEffect((StatusEffect_SO)aquaticOnLandEffect.Duplicate(), null);
            }
        }
    }
}

private void ApplyEnvironmentMismatch(CreatureStats creature)
{
    // Convert to C# List/Hashset for checks
    var nativeProps = creature.Template.NaturalEnvironmentProperties;
    var currentProps = this.CurrentSceneProperties;

    if (nativeProps == null || nativeProps.Count == 0 || nativeProps.Contains(EnvironmentProperty.Any) ||
        currentProps == null || currentProps.Count == 0 || currentProps.Contains(EnvironmentProperty.Any) ||
        nativeProps.Contains(EnvironmentProperty.Planar))
    {
        return;
    }
    
    int mismatchScore = 0;
    var mismatchReasons = new List<string>();

    // SEVERE
    if ((nativeProps.Contains(EnvironmentProperty.Arctic) && currentProps.Contains(EnvironmentProperty.Warm)) ||
        (nativeProps.Contains(EnvironmentProperty.Warm) && nativeProps.Contains(EnvironmentProperty.Arctic)))
    {
        mismatchScore += 3;
        mismatchReasons.Add("extreme temperature shock");
    }

    // MODERATE
    else if ((nativeProps.Contains(EnvironmentProperty.Cold) && currentProps.Contains(EnvironmentProperty.Warm)) ||
             (nativeProps.Contains(EnvironmentProperty.Warm) && currentProps.Contains(EnvironmentProperty.Cold)))
    {
        mismatchScore += 2;
        mismatchReasons.Add("severe temperature change");
    }
    
    // MODERATE: Humidity
    if ((nativeProps.Contains(EnvironmentProperty.Arid) && currentProps.Contains(EnvironmentProperty.Humid)) ||
        (nativeProps.Contains(EnvironmentProperty.Humid) && currentProps.Contains(EnvironmentProperty.Arid)))
    {
        mismatchScore += 2;
        mismatchReasons.Add("drastic humidity change");
    }

    // MINOR
    if (nativeProps.Contains(EnvironmentProperty.Subterranean) && currentProps.Contains(EnvironmentProperty.OpenTerrain))
    {
        mismatchScore += 1;
        mismatchReasons.Add("unaccustomed to open sky");
    }
    
    // MINOR
    if (nativeProps.Contains(EnvironmentProperty.Mountainous) && !currentProps.Contains(EnvironmentProperty.Mountainous))
    {
        mismatchScore += 1;
        mismatchReasons.Add("unfamiliar flat terrain");
    }

    StatusEffect_SO effectToApply = null;
    if (mismatchScore >= 3) effectToApply = severeMismatchEffect;
    else if (mismatchScore == 2) effectToApply = moderateMismatchEffect;
    else if (mismatchScore == 1) effectToApply = minorMismatchEffect;

    if (effectToApply != null)
    {
        string reasonText = string.Join(", ", mismatchReasons);
        GD.PrintRich($"[color=orange]{creature.Name} is mismatched and suffers due to: {reasonText}.[/color]");
        creature.MyEffects.AddEffect((StatusEffect_SO)effectToApply.Duplicate(), null);
    }
}

private void ApplyColdHazard(CreatureStats creature)
{
    if (CurrentSceneProperties == null) return;
    
    if (creature.GetNodeOrNull<ColdHazardController>("ColdHazardController") != null) return;

    ColdSeverity? severity = null;
    if (CurrentSceneProperties.Contains(EnvironmentProperty.Arctic))
    {
        severity = ColdSeverity.Extreme;
    }
    else if (CurrentSceneProperties.Contains(EnvironmentProperty.Cold))
    {
        severity = ColdSeverity.Severe;
    }
    
    if (severity.HasValue)
    {
        var controller = new ColdHazardController();
        controller.Name = "ColdHazardController";
        creature.AddChild(controller);
        controller.Initialize(severity.Value);
    }
}

private void ApplyHeatHazard(CreatureStats creature)
{
    if (CurrentSceneProperties == null) return;

    HeatSeverity? severity = null;
    if (CurrentSceneProperties.Contains(EnvironmentProperty.SevereHeat)) 
    {
        severity = HeatSeverity.Severe;
    }
    else if (CurrentSceneProperties.Contains(EnvironmentProperty.Hot)) 
    {
        severity = HeatSeverity.Hot;
    }

    if (severity.HasValue)
    {
        var controller = new HeatHazardController();
        controller.Name = "HeatHazardController";
        creature.AddChild(controller);
        controller.Initialize(severity.Value);
    }
}
}