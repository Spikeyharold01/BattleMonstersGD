using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// A fully data-driven biome package that TravelPhase can read to know what kind of world to build,
/// which creatures are ecologically valid for that biome, and how often encounters should be considered.
///
/// This resource intentionally avoids any combat logic. It only describes travel-world content.
/// </summary>
[GlobalClass]
public partial class BiomeTravelDefinition : Resource
{
    /// <summary>
    /// Human-readable biome label used by designers and debug tools.
    ///
    /// Expected output:
    /// - A clear biome identity such as "Verdant Wetlands" or "Ashen Highlands".
    /// - This value is used for content selection and logs, not for combat behavior.
    /// </summary>
    [ExportGroup("Identity")]
    [Export] public string BiomeName = "Unnamed Biome";

    /// <summary>
    /// Shared procedural layout recipe for terrain generation in TravelPhase.
    ///
    /// Expected output:
    /// - Width/height/seed and shaping controls that a map builder can consume.
    /// - Designers can reuse the same settings asset across many biome definitions.
    /// </summary>
    [ExportGroup("Procedural Layout")]
    [Export] public TravelProceduralGenerationSettings ProceduralGenerationSettings;

    /// <summary>
    /// Raw creature references that may appear while traveling in this biome.
    ///
    /// Expected output:
    /// - References to existing CreatureTemplate_SO resources only.
    /// - No duplicate creature template data is copied into this resource.
    /// </summary>
    [ExportGroup("Encounter Ecology")]
    [Export] public Godot.Collections.Array<CreatureTemplate_SO> EncounterCreaturePool = new();

    /// <summary>
    /// Scalar controlling how frequently travel systems should attempt encounter placement checks.
    ///
    /// Expected output:
    /// - 0 means no encounter checks.
    /// - 1 is a neutral baseline.
    /// - Values above 1 increase expected encounter pressure.
    /// </summary>
    [Export(PropertyHint.Range, "0,5,0.01")]
    public float EncounterDensity = 1.0f;

    /// <summary>
    /// Overall biome danger rating used by travel encounter scaling systems.
    ///
    /// Expected output:
    /// - Low values produce lighter group scaling.
    /// - Higher values allow denser or larger encounter groups via data-driven settings.
    /// </summary>
    [Export(PropertyHint.Range, "1,30,1")]
    public int BiomeDifficultyRating = 5;


    /// <summary>
    /// Relative weighting of broad behavior archetypes for encounter composition in TravelPhase.
    ///
    /// Expected output:
    /// - Normalized or non-normalized weights; consumers can normalize when needed.
    /// - Used for travel encounter flavor and placement intent only.
    /// </summary>
    [Export] public TravelEncounterArchetypeWeights EncounterArchetypeWeights;

    /// <summary>
    /// Extensible archetype weight table for travel encounters.
    ///
    /// Expected output:
    /// - Zero or more entries that map an archetype definition to a weight.
    /// - Lets designers add new archetypes without changing spawner code.
    /// - When left empty, systems can safely fall back to EncounterArchetypeWeights.
    /// </summary>
    [Export] public Godot.Collections.Array<TravelEncounterArchetypeWeightEntry> EncounterArchetypeWeightEntries = new();

    /// <summary>
    /// Default morale behavior used by TravelMoraleResolver when no archetype override is supplied.
    ///
    /// Expected output:
    /// - A reusable baseline describing when ecological encounters decide to disengage.
    /// - Arena combat remains unaffected because this data is consumed only by TravelPhase systems.
    /// </summary>
    [Export] public TravelMoraleProfile DefaultMoraleProfile;

    /// <summary>
    /// Optional biome-level override table that modifies morale by archetype identifier.
    ///
    /// Expected output:
    /// - Designers can tune ambush predators and territorial defenders without touching code.
    /// - Missing entries safely fall back to DefaultMoraleProfile.
    /// </summary>
    [Export] public Godot.Collections.Array<TravelArchetypeMoraleModifier> ArchetypeMoraleModifiers = new();

    /// <summary>
    /// Group size boundaries used when a travel encounter group is assembled.
    ///
    /// Expected output:
    /// - A minimum and maximum count that higher-level travel systems can roll within.
    /// </summary>
    [Export] public TravelGroupSpawnSettings GroupSpawnSettings;

    /// <summary>
    /// Environmental world modifiers that influence visibility, movement feel, and route readability.
    ///
    /// Expected output:
    /// - Pure environmental values such as fog, water prevalence, and elevation variance.
    /// - No values here should decide combat outcomes.
    /// </summary>
    [ExportGroup("Environment")]
    [Export] public TravelEnvironmentalModifiers EnvironmentalModifiers;

    /// <summary>
    /// Ecology tags used to validate creature eligibility for this biome.
    ///
    /// Expected output:
    /// - Creatures are considered valid when their NaturalEnvironmentProperties intersect this list.
    /// - Keeping this as data allows future rule expansion without touching creature templates.
    /// </summary>
    [ExportGroup("Ecology Validation")]
    [Export] public Godot.Collections.Array<EnvironmentProperty> RequiredEnvironmentProperties = new();

    /// <summary>
    /// Produces an ecology-validated creature pool by checking each reference against the biome criteria.
    ///
    /// Expected output:
    /// - Returns only creature templates with ecological alignment to this biome.
    /// - Skips null entries and duplicate templates while preserving deterministic order.
    /// </summary>
    public Godot.Collections.Array<CreatureTemplate_SO> BuildEcologyValidatedPool(ITravelCreatureEcologyRule additionalRule = null)
    {
        var result = new Godot.Collections.Array<CreatureTemplate_SO>();
        var seen = new HashSet<CreatureTemplate_SO>();

        foreach (CreatureTemplate_SO template in EncounterCreaturePool)
        {
            if (template == null || seen.Contains(template))
            {
                continue;
            }

            if (!BiomeTravelEcologyUtility.MatchesRequiredEnvironment(template, RequiredEnvironmentProperties))
            {
                continue;
            }

            // Travel encounters must honor per-creature phase availability.
            // Arena-only templates stay in arena pools and are excluded from travel spawns.
            if (!template.CanAppearInTravel)
            {
                continue;
            }

            if (additionalRule != null && !additionalRule.IsCreatureEligible(this, template))
            {
                continue;
            }

            seen.Add(template);
            result.Add(template);
        }

        return result;
    }

    /// <summary>
    /// Resolves the effective archetype weight table for travel systems.
    ///
    /// Expected output:
    /// - Returns explicit EncounterArchetypeWeightEntries when present.
    /// - Otherwise emits default Territorial/Ambush/Passive entries built from legacy weights.
    /// - Entries with null archetypes or non-positive weight are ignored.
    /// </summary>
    public Godot.Collections.Array<TravelEncounterArchetypeWeightEntry> BuildEffectiveArchetypeWeights()
    {
        var result = new Godot.Collections.Array<TravelEncounterArchetypeWeightEntry>();

        if (EncounterArchetypeWeightEntries != null)
        {
            foreach (TravelEncounterArchetypeWeightEntry entry in EncounterArchetypeWeightEntries)
            {
                if (entry?.ArchetypeDefinition == null || entry.Weight <= 0f)
                {
                    continue;
                }

                result.Add(entry);
            }
        }

        if (result.Count > 0)
        {
            return result;
        }

        TravelEncounterArchetypeWeights legacy = EncounterArchetypeWeights ?? new TravelEncounterArchetypeWeights();
        result.Add(new TravelEncounterArchetypeWeightEntry { ArchetypeDefinition = TravelEncounterArchetypeDefinition.CreateDefaultTerritorial(), Weight = legacy.Territorial });
        result.Add(new TravelEncounterArchetypeWeightEntry { ArchetypeDefinition = TravelEncounterArchetypeDefinition.CreateDefaultAmbush(), Weight = legacy.Ambush });
        result.Add(new TravelEncounterArchetypeWeightEntry { ArchetypeDefinition = TravelEncounterArchetypeDefinition.CreateDefaultPassive(), Weight = legacy.Passive });
        return result;
    }
}

/// <summary>
/// Data-only procedural controls for travel map generation.
/// This resource is designed to be reusable so multiple biomes can share one terrain template.
/// </summary>
[GlobalClass]
public partial class TravelProceduralGenerationSettings : Resource
{
    [Export] public int Width = TravelScaleDefinitions.TacticalWindowSquaresPerSide;
    [Export] public int Height = TravelScaleDefinitions.TacticalWindowSquaresPerSide;
    [Export] public float TileSize = 2.0f;
    [Export] public int Seed = 0;

    /// <summary>
    /// World-space strategic coordinate used as the deterministic parent tile for this travel run.
    /// Expected output: same coordinate + seed always projects to the same tactical map details.
    /// </summary>
    [Export] public int StrategicCoordinateX = 0;
    [Export] public int StrategicCoordinateY = 0;

    /// <summary>
    /// Locked strategic tile span in feet. This approximates one hour of movement at baseline speed.
    /// </summary>
    [Export] public float StrategicTileFeet = TravelScaleDefinitions.StrategicTileFeet;

    /// <summary>
    /// Event anchor inside the strategic tile in feet.
    /// Expected output: tactical projection centers on this location when strategic travel resolves down to tactical scope.
    /// </summary>
    [Export] public float TacticalEventFeetX = TravelScaleDefinitions.StrategicTileFeet * 0.5f;

    /// <summary>
    /// Event anchor inside the strategic tile in feet.
    /// Expected output: tactical projection centers on this location when strategic travel resolves down to tactical scope.
    /// </summary>
    [Export] public float TacticalEventFeetZ = TravelScaleDefinitions.StrategicTileFeet * 0.5f;

    /// <summary>
    /// Height variation applied when creating travel tiles.
    /// Expected output: a small terrain undulation value used for visual and traversal texture.
    /// </summary>
    [Export(PropertyHint.Range, "0,5,0.01")]
    public float HeightVariance = 0.15f;

    /// <summary>
    /// Number of broad zones that the travel layer can treat as encounter-capable areas.
    /// Expected output: a positive integer used to distribute encounter opportunities.
    /// </summary>
    [Export(PropertyHint.Range, "1,64,1")]
    public int EncounterZoneCount = 6;

    /// <summary>
    /// Space kept clear near map borders when deriving encounter zones.
    /// Expected output: safer perimeter spacing for spawn-zone planning.
    /// </summary>
    [Export(PropertyHint.Range, "0,10,0.1")]
    public float EncounterZoneBorderPadding = 1.5f;

    /// <summary>
    /// Ally spawn point count for travel phase placement.
    /// Expected output: number of ally positions requested from the map builder.
    /// </summary>
    [Export(PropertyHint.Range, "0,16,1")]
    public int AllySpawnCount = 3;
}

/// <summary>
/// Relative weights for broad encounter archetypes used during travel composition.
/// </summary>
[GlobalClass]
public partial class TravelEncounterArchetypeWeights : Resource
{
    [Export(PropertyHint.Range, "0,1,0.01")] public float Territorial = 0.45f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float Ambush = 0.35f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float Passive = 0.20f;
}

/// <summary>
/// Group-size envelope used by travel encounter assembly systems.
/// </summary>
[GlobalClass]
public partial class TravelGroupSpawnSettings : Resource
{
    [Export(PropertyHint.Range, "1,20,1")] public int MinGroupSize = 1;
    [Export(PropertyHint.Range, "1,20,1")] public int MaxGroupSize = 3;

    /// <summary>
    /// Baseline biome challenge rating used to determine how strongly encounter groups scale.
    /// Expected output: larger group rolls when current biome challenge exceeds this baseline.
    /// </summary>
    [Export(PropertyHint.Range, "1,30,1")] public int BaselineBiomeDifficulty = 5;

    /// <summary>
    /// Multiplier applied when scaling extra members above baseline difficulty.
    /// Expected output: fractional growth so scaling remains controlled and designer-friendly.
    /// </summary>
    [Export(PropertyHint.Range, "0,3,0.01")] public float DifficultyScalingFactor = 0.25f;
}

/// <summary>
/// Data-driven behavior profile for one travel encounter archetype.
/// This resource controls spawn presentation and morale tendencies without touching combat internals.
/// </summary>
[GlobalClass]
public partial class TravelEncounterArchetypeDefinition : Resource
{
    [Export] public string ArchetypeId = "territorial";
    [Export] public string DisplayName = "Territorial";

    /// <summary>
    /// Optional travel behavior overlay profile used only by TravelBehaviorController.
    ///
    /// Expected output:
    /// - When assigned, this profile drives patrol/awareness/engagement thresholds in TravelPhase.
    /// - When left empty, TravelBehaviorController chooses a safe default by archetype id.
    /// - Arena combat AI remains untouched because this data is consumed only during TravelPhase.
    /// </summary>
    [Export] public TravelBehaviorArchetypeProfile BehaviorProfile;

    /// <summary>
    /// If true, the encounter can spawn as concealed and reveal on trigger.
    /// </summary>
    [Export] public bool StartsHidden = false;

    /// <summary>
    /// Describes whether this archetype begins hostile, neutral, or defensive in travel context.
    /// </summary>
    [Export] public TravelInitialAggressionState InitialAggression = TravelInitialAggressionState.Hostile;

    /// <summary>
    /// Fraction of max group health where retreat should be considered.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float MoraleRetreatThreshold = 0.3f;

    /// <summary>
    /// Relative preference for ambush-zone generation.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float AmbushPreference = 0f;

    /// <summary>
    /// Optional profile override for this archetype.
    ///
    /// Expected output:
    /// - When present, this profile is copied onto encounter groups that use this archetype.
    /// - When absent, TravelMoraleResolver falls back to biome defaults.
    /// </summary>
    [Export] public TravelMoraleProfile MoraleProfileOverride;

    public static TravelEncounterArchetypeDefinition CreateDefaultTerritorial()
    {
        return new TravelEncounterArchetypeDefinition
        {
            ArchetypeId = "territorial",
            DisplayName = "Territorial",
            StartsHidden = false,
            InitialAggression = TravelInitialAggressionState.Defensive,
            MoraleRetreatThreshold = 0.25f,
            AmbushPreference = 0.1f
        };
    }

    public static TravelEncounterArchetypeDefinition CreateDefaultAmbush()
    {
        return new TravelEncounterArchetypeDefinition
        {
            ArchetypeId = "ambush_predator",
            DisplayName = "Ambush Predator",
            StartsHidden = true,
            InitialAggression = TravelInitialAggressionState.Hostile,
            MoraleRetreatThreshold = 0.4f,
            AmbushPreference = 0.9f
        };
    }

    public static TravelEncounterArchetypeDefinition CreateDefaultPassive()
    {
        return new TravelEncounterArchetypeDefinition
        {
            ArchetypeId = "passive_defensive",
            DisplayName = "Passive/Defensive",
            StartsHidden = false,
            InitialAggression = TravelInitialAggressionState.Passive,
            MoraleRetreatThreshold = 0.7f,
            AmbushPreference = 0.05f
        };
    }
}

/// <summary>
/// Data-only morale model used by TravelMoraleResolver.
///
/// This model is intentionally phase-local and does not influence combat calculations.
/// </summary>
[GlobalClass]
public partial class TravelMoraleProfile : Resource
{
    /// <summary>
    /// Group-health threshold that can trigger retreat behavior.
    ///
    /// Expected output:
    /// - 0.35 means retreat can trigger once the group falls to 35% combined health.
    /// - 0 disables this trigger.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MoraleThreshold = 0.35f;

    /// <summary>
    /// Casualty ratio threshold for group-level panic.
    ///
    /// Expected output:
    /// - 0.5 means retreat can trigger when half the group is down.
    /// - 0 disables this trigger.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float CasualtyRatioThreshold = 0.5f;

    /// <summary>
    /// If true, losing the assigned leader immediately allows disengagement.
    /// </summary>
    [Export] public bool LeaderDeathTrigger = true;

    /// <summary>
    /// Index used to select which member is treated as leader.
    ///
    /// Expected output:
    /// - 0 uses the first spawned member.
    /// - Values outside range are clamped safely at runtime.
    /// </summary>
    [Export(PropertyHint.Range, "0,20,1")]
    public int LeaderMemberIndex = 0;

    /// <summary>
    /// Time in seconds before prolonged combat pressure allows retreat.
    ///
    /// Expected output:
    /// - Higher values make encounters hold longer.
    /// - 0 disables time-based retreat.
    /// </summary>
    [Export(PropertyHint.Range, "0,300,1")]
    public float TimeInCombatThreshold = 45f;

    /// <summary>
    /// Relative party-to-encounter force ratio where retreat becomes valid.
    ///
    /// Expected output:
    /// - 1.5 means the party is about 50% stronger before this trigger can fire.
    /// - 0 disables force comparison checks.
    /// </summary>
    [Export(PropertyHint.Range, "0,5,0.01")]
    public float RelativeForceDisadvantageThreshold = 1.5f;

    /// <summary>
    /// Scalar applied to all thresholds for fast archetype-wide tuning.
    ///
    /// Expected output:
    /// - Below 1.0 causes earlier retreat decisions.
    /// - Above 1.0 causes more stubborn behavior.
    /// </summary>
    [Export(PropertyHint.Range, "0.25,3,0.01")]
    public float ArchetypeModifier = 1.0f;

    /// <summary>
    /// Optional toggle to allow XP on morale-based escapes.
    /// TravelPhase can consume this flag when reward systems are integrated.
    /// </summary>
    [Export] public bool GrantXpOnDisengage = false;
}

/// <summary>
/// Biome table entry that maps archetype identifiers to morale profile overrides.
/// </summary>
[GlobalClass]
public partial class TravelArchetypeMoraleModifier : Resource
{
    [Export] public string ArchetypeId = string.Empty;
    [Export] public TravelMoraleProfile ProfileOverride;
}

/// <summary>
/// Weighted selector entry for one archetype definition.
/// </summary>
[GlobalClass]
public partial class TravelEncounterArchetypeWeightEntry : Resource
{
    [Export] public TravelEncounterArchetypeDefinition ArchetypeDefinition;
    [Export(PropertyHint.Range, "0,5,0.01")] public float Weight = 1f;
}

/// <summary>
/// High-level aggression posture used only by TravelPhase orchestration.
/// </summary>
public enum TravelInitialAggressionState
{
    Passive,
    Defensive,
    Hostile
}

/// <summary>
/// Purely environmental travel modifiers. These values influence feel and readability,
/// not combat math or combat outcomes.
/// </summary>
[GlobalClass]
public partial class TravelEnvironmentalModifiers : Resource
{
    [Export(PropertyHint.Range, "0,1,0.01")] public float FogDensity = 0.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float WaterPresence = 0.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float ElevationVariance = 0.2f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float VegetationDensity = 0.3f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float VisibilityPenalty = 0.0f;
}

/// <summary>
/// Optional extension point for additional ecology rules.
/// Implementations can add seasonal, weather, or faction rules later without changing base data.
/// </summary>
public interface ITravelCreatureEcologyRule
{
    /// <summary>
    /// Returns true when a creature reference is eligible for this biome under an additional rule.
    /// Expected output: true for allowed, false for filtered out.
    /// </summary>
    bool IsCreatureEligible(BiomeTravelDefinition biomeDefinition, CreatureTemplate_SO creatureTemplate);
}

/// <summary>
/// Shared helper for baseline ecology validation against CreatureTemplate_SO environment properties.
/// </summary>
public static class BiomeTravelEcologyUtility
{
    /// <summary>
    /// Validates that a creature lists at least one of the required environment properties.
    ///
    /// Expected output:
    /// - true when biome has no required properties (open rule),
    /// - true when creature ecology contains at least one required property,
    /// - false otherwise.
    /// </summary>
    public static bool MatchesRequiredEnvironment(CreatureTemplate_SO creatureTemplate, Godot.Collections.Array<EnvironmentProperty> requiredProperties)
    {
        if (creatureTemplate == null)
        {
            return false;
        }

        if (requiredProperties == null || requiredProperties.Count == 0)
        {
            return true;
        }

        foreach (EnvironmentProperty required in requiredProperties)
        {
            if (creatureTemplate.NaturalEnvironmentProperties.Contains(required))
            {
                return true;
            }
        }

        return false;
    }
}
