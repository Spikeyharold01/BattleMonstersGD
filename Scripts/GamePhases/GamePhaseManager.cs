using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Main controller for high-level game phases.
///
/// It decides which phase is active and handles clean switching.
/// </summary>
public partial class GamePhaseManager : Godot.Node
{
    /// <summary>
    /// Sent after phase changes from one type to another.
    /// </summary>
    [Signal] public delegate void PhaseChangedEventHandler(GamePhaseType previous, GamePhaseType current);

    [ExportGroup("Arena")]
    [Export] public Node3D ArenaRoot;

    [ExportGroup("Travel")]
    [Export] public BiomeTravelDefinition TravelBiomeDefinition;

    [ExportGroup("Lifecycle Callbacks")]
    [Export] public Callable ArenaEnterCallback;
    [Export] public Callable ArenaExitCallback;

    // All available phase objects by type.
    private readonly Dictionary<GamePhaseType, IGamePhase> _phases = new Dictionary<GamePhaseType, IGamePhase>();

    // Shared tools/data for phases.
    private CreaturePersistenceService _creaturePersistence;
    private RecruitmentManager _recruitmentManager;
    private PartyRosterManager _partyRosterManager;
    private IntelligenceSystemManager _intelligenceSystemManager;
    private GamePhaseContext _context;

    // Internal roots used by this manager.
    private Node3D _phaseRoot;
    private GridNode _creatureStorage;

    // Current phase and direct travel reference.
    private IGamePhase _activePhase;
    private TravelPhase _travelPhase;

    /// <summary>
    /// Current active phase type.
    /// </summary>
    public GamePhaseType ActivePhaseType => _activePhase?.PhaseType ?? GamePhaseType.Arena;

    /// <summary>
    /// Setup manager and start in Arena.
    /// </summary>
    public override void _Ready()
    {
        _phaseRoot = GetNodeOrNull<Node3D>("PhaseRoot") ?? new Node3D { Name = "PhaseRoot" };
        if (_phaseRoot.GetParent() == null)
        {
            AddChild(_phaseRoot);
        }

        _creatureStorage = GetNodeOrNull<GridNode>("CreatureStorage") ?? new Node { Name = "CreatureStorage" };
        if (_creatureStorage.GetParent() == null)
        {
            AddChild(_creatureStorage);
        }

        _creaturePersistence = new CreaturePersistenceService(_creatureStorage);
        _creaturePersistence.RegisterFromSceneTree(GetTree());

        _recruitmentManager = new RecruitmentManager();
        _partyRosterManager = new PartyRosterManager();
        _intelligenceSystemManager = new IntelligenceSystemManager();
        RecruitmentRuntime.ActiveManager = _recruitmentManager;
        PartyRosterRuntime.ActiveManager = _partyRosterManager;
        IntelligenceSystemRuntime.Manager = _intelligenceSystemManager;
        RegisterDefaultRecruitmentIntelligenceProfiles(_recruitmentManager);

        _context = new GamePhaseContext(this, _phaseRoot, _creaturePersistence, _recruitmentManager, _partyRosterManager, _intelligenceSystemManager);

        RegisterPhases();
        SwitchPhase(GamePhaseType.Arena);
    }

    /// <summary>
    /// Forward updates to current phase.
    /// Also moves back to Arena when Travel finishes.
    /// </summary>
    public override void _Process(double delta)
    {
        _activePhase?.UpdatePhase(delta, _context);

        if (_travelPhase != null && _activePhase == _travelPhase && _travelPhase.IsCompleted)
        {
            SwitchPhase(GamePhaseType.Arena);
        }
    }

    /// <summary>
    /// Switch to a new phase in a safe order:
    /// - exit old phase,
    /// - enter new phase.
    /// </summary>
    public void SwitchPhase(GamePhaseType nextPhase)
    {
        if (!_phases.TryGetValue(nextPhase, out var incomingPhase))
        {
            GD.PrintErr($"Requested phase '{nextPhase}' is not registered.");
            return;
        }

        if (_activePhase == incomingPhase)
        {
            return;
        }

        var previousType = ActivePhaseType;

        _activePhase?.ExitPhase(_context);
        _activePhase = incomingPhase;
        _activePhase.EnterPhase(_context);

        // TurnManager stays the sole authority for turn length.
        TurnManager.Instance?.SetGameMode(nextPhase);

        EmitSignal(SignalName.PhaseChanged, previousType, nextPhase);
        GD.Print($"Phase transition: {previousType} -> {nextPhase}");
    }

    /// <summary>
    /// Build and register Arena + Travel phases.
    /// </summary>

    /// <summary>
    /// Creates a safe default travel biome definition so TravelPhase can run even before designers assign data.
    /// </summary>
    private static BiomeTravelDefinition CreateDefaultTravelBiomeDefinition()
    {
        return new BiomeTravelDefinition
        {
            BiomeName = "Default Travel Biome",
            ProceduralGenerationSettings = new TravelProceduralGenerationSettings(),
            EncounterArchetypeWeights = new TravelEncounterArchetypeWeights(),
            GroupSpawnSettings = new TravelGroupSpawnSettings(),
            EnvironmentalModifiers = new TravelEnvironmentalModifiers()
        };
    }
    /// <summary>
    /// Seeds practical intelligence profiles that can be unlocked by recruited allies.
    ///
    /// Expected output:
    /// - Rat recruits can teach ground predator movement patterns.
    /// - Goblin recruits can teach trap awareness habits.
    /// - Former dragon minions can expose breath cadence windows.
    /// </summary>
    private static void RegisterDefaultRecruitmentIntelligenceProfiles(RecruitmentManager recruitmentManager)
    {
        if (recruitmentManager == null)
        {
            return;
        }

        recruitmentManager.RegisterIntelligenceProfile(new CreatureIntelligenceProfile
        {
            CreatureId = "Rat:0",
            RelatedEnemyTags = new List<string> { "Beast", "Animal", "Vermin" },
            WeaknessTags = new List<string> { "Ground Predator Patterns" },
            HabitatKnowledge = new List<string> { "Sewer", "Cellar" },
            CombatTells = new List<string> { "Stealth Pounce" },
            VulnerabilityTriggers = new List<string> { "Tunnel Rush" },
            TacticalModifiers = new TacticalModifierBundle
            {
                PerceptionBonus = 2f,
                AmbushResistanceBonus = 0.08f
            }
        });

        recruitmentManager.RegisterIntelligenceProfile(new CreatureIntelligenceProfile
        {
            CreatureId = "Goblin:0",
            RelatedEnemyTags = new List<string> { "Humanoid", "Goblin" },
            WeaknessTags = new List<string> { "Trap Awareness" },
            HabitatKnowledge = new List<string> { "Ruins", "Camps" },
            CombatTells = new List<string> { "Tripwire Rush" },
            VulnerabilityTriggers = new List<string> { "Flank Bait" },
            TacticalModifiers = new TacticalModifierBundle
            {
                PerceptionBonus = 1f,
                TrapDamageReduction = 0.12f
            }
        });

        recruitmentManager.RegisterIntelligenceProfile(new CreatureIntelligenceProfile
        {
            CreatureId = "DragonMinion:0",
            RelatedEnemyTags = new List<string> { "Dragon" },
            WeaknessTags = new List<string> { "Breath Cooldown Window" },
            HabitatKnowledge = new List<string> { "Lair" },
            CombatTells = new List<string> { "Inhale Pause" },
            VulnerabilityTriggers = new List<string> { "Wing Reset" },
            TacticalModifiers = new TacticalModifierBundle
            {
                InitiativeBonus = 1f,
                AmbushResistanceBonus = 0.1f
            }
        });
    }

    private void RegisterPhases()
    {
        Action arenaEnter = () =>
        {
            if (ArenaEnterCallback.IsValid()) ArenaEnterCallback.Call();
        };

        Action arenaExit = () =>
        {
            if (ArenaExitCallback.IsValid()) ArenaExitCallback.Call();
        };

        var arenaPhase = new ArenaPhaseAdapter(ArenaRoot, arenaEnter, arenaExit);

        var mapBuilder = new TravelBiomeMapBuilder();
        AddChild(mapBuilder);

        var definition = TravelBiomeDefinition ?? CreateDefaultTravelBiomeDefinition();
        var provider = new ArenaMirroredTravelBiomeDefinitionProvider(definition);
        _travelPhase = new TravelPhase(provider, mapBuilder);

        _phases[arenaPhase.PhaseType] = arenaPhase;
        _phases[_travelPhase.PhaseType] = _travelPhase;
    }
}
