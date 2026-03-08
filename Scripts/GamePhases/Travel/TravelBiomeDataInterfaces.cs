using Godot;


/// <summary>
/// Contract used by TravelPhase and any travel subsystem that needs biome data in a reusable way.
/// 
/// This interface keeps TravelPhase decoupled from concrete resource storage so new providers
/// can be added later (seasonal variants, campaign state, weather overlays) without rewriting core flow.
/// </summary>
public interface ITravelBiomeDefinitionProvider
{
    /// <summary>
    /// Returns the active biome definition.
    /// Expected output: a non-null data resource when travel should run with biome configuration.
    /// </summary>
    BiomeTravelDefinition GetActiveDefinition();

    /// <summary>
    /// Builds a snapshot tailored for map generation and travel encounter planning.
    /// Expected output: a stable data package consumed by TravelPhase/map builders.
    /// </summary>
    TravelBiomeQuerySnapshot BuildTravelSnapshot();
}

/// <summary>
/// Default provider that exposes one direct BiomeTravelDefinition resource.
/// Useful for inspector-driven setup in scenes.
/// </summary>
public sealed class ResourceTravelBiomeDefinitionProvider : ITravelBiomeDefinitionProvider
{
    private readonly BiomeTravelDefinition _definition;

    public ResourceTravelBiomeDefinitionProvider(BiomeTravelDefinition definition)
    {
        _definition = definition;
    }

    public BiomeTravelDefinition GetActiveDefinition() => _definition;

    public TravelBiomeQuerySnapshot BuildTravelSnapshot()
    {
        return TravelBiomeQuerySnapshot.FromDefinition(_definition);
    }
}

/// <summary>
/// Travel biome provider that mirrors the active Arena environment profile.
///
/// Behavior:
/// - starts from a baseline BiomeTravelDefinition,
/// - clones it per travel run,
/// - overrides ecology/environment selectors from EnvironmentManager's current arena properties.
///
/// This keeps travel generation aligned with the arena biome the player just left,
/// while avoiding mutation of designer-authored baseline resources.
///
/// Important: this mirrors biome/environment semantics only, not map identity.
/// Travel keeps generating a fresh map runtime each time it starts.
/// </summary>
public sealed class ArenaMirroredTravelBiomeDefinitionProvider : ITravelBiomeDefinitionProvider
{
    private readonly BiomeTravelDefinition _baselineDefinition;
    private BiomeTravelDefinition _cachedDefinition;

    public ArenaMirroredTravelBiomeDefinitionProvider(BiomeTravelDefinition baselineDefinition)
    {
        _baselineDefinition = baselineDefinition;
    }

    public BiomeTravelDefinition GetActiveDefinition()
    {
        _cachedDefinition = BuildMirroredDefinition();
        return _cachedDefinition;
    }

    public TravelBiomeQuerySnapshot BuildTravelSnapshot()
    {
        _cachedDefinition = BuildMirroredDefinition();
        return TravelBiomeQuerySnapshot.FromDefinition(_cachedDefinition);
    }

    private BiomeTravelDefinition BuildMirroredDefinition()
    {
        if (_baselineDefinition == null)
        {
            return null;
        }

        var workingDefinition = (BiomeTravelDefinition)_baselineDefinition.Duplicate(true);
        if (workingDefinition == null)
        {
            return _baselineDefinition;
        }

        var arenaProperties = EnvironmentManager.Instance?.CurrentSceneProperties;
        var mirroredProperties = new Godot.Collections.Array<EnvironmentProperty>();

        if (arenaProperties != null)
        {
            foreach (EnvironmentProperty property in arenaProperties)
            {
                if (!mirroredProperties.Contains(property))
                {
                    mirroredProperties.Add(property);
                }
            }
        }

        if (EnvironmentManager.Instance != null && EnvironmentManager.Instance.IsUnderwaterCombat)
        {
            if (!mirroredProperties.Contains(EnvironmentProperty.Aquatic))
            {
                mirroredProperties.Add(EnvironmentProperty.Aquatic);
            }
        }

        if (mirroredProperties.Count > 0)
        {
            workingDefinition.RequiredEnvironmentProperties = mirroredProperties;
            workingDefinition.BiomeName = $"Arena Mirror - {BuildEnvironmentLabel(mirroredProperties)}";
        }

        // Biome should mirror Arena context, but map should remain unique per travel run.
        // Seed=0 keeps TravelBiomeMapBuilder in randomized mode.
        if (workingDefinition.ProceduralGenerationSettings != null)
        {
            workingDefinition.ProceduralGenerationSettings.Seed = 0;
        }

        return workingDefinition;
    }

    private static string BuildEnvironmentLabel(Godot.Collections.Array<EnvironmentProperty> mirroredProperties)
    {
        if (mirroredProperties == null || mirroredProperties.Count == 0)
        {
            return "Unspecified";
        }

        var parts = new string[mirroredProperties.Count];
        for (int i = 0; i < mirroredProperties.Count; i++)
        {
            parts[i] = mirroredProperties[i].ToString();
        }

        return string.Join(", ", parts);
    }
}

/// <summary>
/// Immutable-style query payload generated from BiomeTravelDefinition.
/// TravelPhase asks for this once and shares it with systems that need synchronized travel data.
/// </summary>
public sealed class TravelBiomeQuerySnapshot
{
    /// <summary>
    /// Source biome definition used to build this snapshot.
    /// </summary>
    public BiomeTravelDefinition Definition { get; private set; }

    /// <summary>
    /// Procedural layout controls used by map generation.
    /// </summary>
    public TravelProceduralGenerationSettings Procedural { get; private set; }

    /// <summary>
    /// Ecology-validated creature list for travel encounter planning.
    /// </summary>
    public Godot.Collections.Array<CreatureTemplate_SO> EcologyValidatedEncounterPool { get; private set; }

    /// <summary>
    /// Environmental travel modifiers such as fog/water/elevation.
    /// </summary>
    public TravelEnvironmentalModifiers Environmental { get; private set; }

    /// <summary>
    /// Relative encounter archetype weights.
    /// </summary>
    public TravelEncounterArchetypeWeights ArchetypeWeights { get; private set; }

    /// <summary>
    /// Extensible archetype weight table used by TravelEncounterSpawner.
    /// </summary>
    public Godot.Collections.Array<TravelEncounterArchetypeWeightEntry> ArchetypeWeightEntries { get; private set; }

    /// <summary>
    /// Group-size bounds for encounter planning.
    /// </summary>
    public TravelGroupSpawnSettings GroupSpawns { get; private set; }

    /// <summary>
    /// Default travel morale profile resolved from biome data.
    /// </summary>
    public TravelMoraleProfile DefaultMorale { get; private set; }

    /// <summary>
    /// Optional biome table for archetype-specific morale overrides.
    /// </summary>
    public Godot.Collections.Array<TravelArchetypeMoraleModifier> ArchetypeMoraleModifiers { get; private set; }

    /// <summary>
    /// Encounter density scalar.
    /// </summary>
    public float EncounterDensity { get; private set; }

    /// <summary>
    /// Creates a synchronized snapshot from one biome definition.
    /// Expected output: a self-contained package with all travel-facing biome fields.
    /// </summary>
    public static TravelBiomeQuerySnapshot FromDefinition(BiomeTravelDefinition definition)
    {
        if (definition == null)
        {
            return null;
        }

        return new TravelBiomeQuerySnapshot
        {
            Definition = definition,
            Procedural = definition.ProceduralGenerationSettings,
            EcologyValidatedEncounterPool = definition.BuildEcologyValidatedPool(),
            Environmental = definition.EnvironmentalModifiers,
            ArchetypeWeights = definition.EncounterArchetypeWeights,
            ArchetypeWeightEntries = definition.BuildEffectiveArchetypeWeights(),
            GroupSpawns = definition.GroupSpawnSettings,
            DefaultMorale = definition.DefaultMoraleProfile,
            ArchetypeMoraleModifiers = definition.ArchetypeMoraleModifiers,
            EncounterDensity = definition.EncounterDensity
        };
    }
}
