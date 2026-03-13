using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Travel mode phase.
///
/// Responsibilities:
/// - request biome travel data,
/// - build travel map,
/// - place player/allies,
/// - end when player reaches exit.
/// </summary>
public sealed class TravelPhase : IGamePhase
{
    // Provider for active biome travel definition data.
    private readonly ITravelBiomeDefinitionProvider _biomeDefinitionProvider;

    // Builder that creates map runtime objects.
    private readonly TravelBiomeMapBuilder _mapBuilder;

    // Current runtime map data (only valid while this phase is active).
    private TravelBiomeMapRuntime _runtime;

    // Active query snapshot for this travel run.
    private TravelBiomeQuerySnapshot _snapshot;

    // Cached phase context so asynchronous travel callbacks can request clean phase transitions.
    private GamePhaseContext _context;

    // Travel-only encounter orchestration system.
    private readonly TravelEncounterSpawner _encounterSpawner = new TravelEncounterSpawner();

    // Travel-only morale observer that orders disengagement without touching combat internals.
    private readonly TravelMoraleResolver _moraleResolver = new TravelMoraleResolver();

    // Travel-only non-combat behavior overlay for allies and encounter creatures.
    private readonly TravelBehaviorController _behaviorController = new TravelBehaviorController();

    // Travel-only sensory ecosystem overlay that blends authentic biome life with mimic signals.
    private readonly TravelSensoryEventController _sensoryEventController = new TravelSensoryEventController();

    // Strategic-hour encounter and AI conflict resolver for persistent tile entities.
    private readonly StrategicEncounterResolver _strategicEncounterResolver = new StrategicEncounterResolver();

    // Set after the first valid exit-zone contact so exit stays player-confirmed.
    private bool _exitRequested;

    // Travel turn cadence accumulator. Travel time now advances only through TurnManager authority.
    private float _travelTurnAccumulatorSeconds;

    /// <summary>
    /// Create travel phase.
    /// </summary>
    public TravelPhase(ITravelBiomeDefinitionProvider biomeDefinitionProvider, TravelBiomeMapBuilder mapBuilder)
    {
        _biomeDefinitionProvider = biomeDefinitionProvider;
        _mapBuilder = mapBuilder;
    }

    /// <summary>
    /// This class is the Travel phase.
    /// </summary>
    public GamePhaseType PhaseType => GamePhaseType.Travel;

    /// <summary>
    /// True after player reaches exit zone.
    /// </summary>
    public bool IsCompleted { get; private set; }

    /// <summary>
    /// Latest travel biome snapshot consumed by this phase.
    ///
    /// Expected output:
    /// - Non-null while a valid biome definition exists.
    /// - Gives travel systems access to procedural settings, ecology-validated pool,
    ///   encounter density, archetype weights, and environmental modifiers.
    /// </summary>
    public TravelBiomeQuerySnapshot ActiveSnapshot => _snapshot;

    /// <summary>
    /// Start Travel:
    /// - query biome data,
    /// - build map,
    /// - listen to exit zone,
    /// - place player and allies.
    /// </summary>
    public void EnterPhase(GamePhaseContext context)
    {
        IsCompleted = false;
        _exitRequested = false;
        _context = context;
        _travelTurnAccumulatorSeconds = 0f;

        TurnManager.Instance?.SetGameMode(GamePhaseType.Travel);
        TurnManager.Instance?.SetTravelResolutionState(TravelResolutionState.StrategicHour);

        _snapshot = _biomeDefinitionProvider?.BuildTravelSnapshot();
        if (_snapshot == null)
        {
            GD.PrintErr("TravelPhase could not start because no BiomeTravelDefinition snapshot was available.");
            IsCompleted = true;
            return;
        }

        _runtime = _mapBuilder.Build(_snapshot, context.PhaseRoot);
        if (_runtime == null || _runtime.ExitZone == null)
        {
            GD.PrintErr("TravelPhase failed to create map runtime from biome snapshot.");
            IsCompleted = true;
            return;
        }

        _runtime.ExitZone.PlayerReachedExit += OnPlayerReachedExit;


        List<Vector3> allySpawns = _runtime.AllySpawnPoints;

        context.CreaturePersistence.SpawnCreatures(
            context.PhaseRoot,
            c => c.IsInGroup("Player"), // ONLY spawn the main player token
            _ => _runtime.PlayerSpawnPoint);

        // (The massive block of Ally spawning code has been deleted. Allies stay safely hidden in storage until combat!)

        // Initialize travel-only encounter orchestration after map and party placement are ready.
        _encounterSpawner.Initialize(_snapshot, _runtime, context);

        // Wire TravelMoraleResolver as a phase-local overlay.
        _encounterSpawner.EncounterSpawned += OnEncounterSpawned;
        _encounterSpawner.EncounterDisengaged += OnEncounterDisengaged;

        _moraleResolver.Initialize(_snapshot, _runtime, context);
        _moraleResolver.RetreatOrdered += OnRetreatOrdered;

        _behaviorController.Initialize(_snapshot, _runtime, context);
        _behaviorController.CombatEngagementRequested += OnCombatEngagementRequested;

        // Reset cross-layer pursuit state for this travel run so stale chase context never leaks
        // between sessions and deterministic projections stay reproducible.
        TravelPursuitSystem.ResetForTravelPhase();

        _strategicEncounterResolver.Initialize(_snapshot, _runtime, context);

        _sensoryEventController.Initialize(_snapshot, _runtime, context);
        _sensoryEventController.SensoryStimulusBroadcasted += OnSensoryStimulusBroadcasted;
        _sensoryEventController.AmbushEscalationRequested += OnAmbushEscalationRequested;
    }

    /// <summary>
    /// Stop Travel:
    /// - stop listening to exit signal,
    /// - park creatures,
    /// - remove generated map.
    /// </summary>
    public void ExitPhase(GamePhaseContext context)
    {
        if (_runtime != null && _runtime.ExitZone != null)
        {
            _runtime.ExitZone.PlayerReachedExit -= OnPlayerReachedExit;
        }

        _moraleResolver.RetreatOrdered -= OnRetreatOrdered;
        _moraleResolver.Shutdown();

        _behaviorController.CombatEngagementRequested -= OnCombatEngagementRequested;
        _behaviorController.Shutdown();

        _sensoryEventController.SensoryStimulusBroadcasted -= OnSensoryStimulusBroadcasted;
        _sensoryEventController.AmbushEscalationRequested -= OnAmbushEscalationRequested;
        _sensoryEventController.Shutdown();
        _strategicEncounterResolver.Shutdown();

        _encounterSpawner.EncounterSpawned -= OnEncounterSpawned;
        _encounterSpawner.EncounterDisengaged -= OnEncounterDisengaged;

        // Shut down travel encounter orchestration before we free map content.
        _encounterSpawner.Shutdown();

        context.CreaturePersistence.ParkAllCreatures();

        if (_runtime?.MapRoot != null && GodotObject.IsInstanceValid(_runtime.MapRoot))
        {
            _runtime.MapRoot.QueueFree();
        }

        _runtime = null;
        _snapshot = null;
        _exitRequested = false;
        _context = null;
    }

    /// <summary>
    /// Per-frame travel update hook.
    /// </summary>
    public void UpdatePhase(double delta, GamePhaseContext context)
    {
        // Travel durations now advance strictly by TurnManager turn authority.
        // This removes real-time travel compression and keeps spell timing deterministic.
        TurnManager turnManager = TurnManager.Instance;
        if (turnManager != null)
        {
            _travelTurnAccumulatorSeconds += (float)delta;
            if (_travelTurnAccumulatorSeconds >= turnManager.CurrentTurnLengthSeconds)
            {
                _travelTurnAccumulatorSeconds = 0f;
                bool strategicEscalation = false;
                if (turnManager.CurrentTravelResolutionState == TravelResolutionState.StrategicHour)
                {
                    StrategicResolutionResult strategicResult = _strategicEncounterResolver.ResolveStrategicHourTurn();
                    strategicEscalation = strategicResult.EncounterTriggered || strategicResult.HuntedEscalationTriggered;
                    if (strategicEscalation)
                    {
                        // Tell the spawner to generate the specific AI entities that caught us!
                        _encounterSpawner.SpawnFromStrategicEntities(strategicResult.EngagingEntities);

                        turnManager.RegisterTravelResolutionEvent(TravelResolutionEvent.PerceptionEvent);
                        turnManager.RegisterTravelResolutionEvent(TravelResolutionEvent.HostileProximityThresholdCrossed);
                    }
                }

                bool hasHostiles = _encounterSpawner.HasAnyHostileEncounter();
                bool hasPerceptionEvents = strategicEscalation || (turnManager.CurrentTravelResolutionState != TravelResolutionState.StrategicHour && _sensoryEventController.HasRecentStimulus);
                bool hasThreatState = _behaviorController.HasThreatState;
                turnManager.AdvanceTravelTurn(context?.CreaturePersistence?.PersistentCreatures, hasHostiles, hasPerceptionEvents, hasThreatState);

                // The pursuit layer advances on the same authoritative turn cadence used by travel time,
                // ensuring clarity and fatigue changes are deterministic and resolution-aware.
                TravelPursuitSystem.AdvanceTravelTurn(turnManager.CurrentTravelResolutionState, context?.CreaturePersistence?.PersistentCreatures);

                if (turnManager.CurrentTravelResolutionState == TravelResolutionState.CombatSixSeconds && !HasActivePartyCombat())
                {
                    turnManager.RegisterTravelResolutionEvent(TravelResolutionEvent.CombatEnded);
                }
            }
        }

        // Travel encounter orchestration runs here and remains isolated from combat engine internals.
        _encounterSpawner.Update(delta);

        // Morale overlay observes travel combat state and emits retreat orders.
        _moraleResolver.Update(delta);

        // Behavior overlay runs only during TravelPhase and can request combat handoff.
        _behaviorController.Update(delta);

        // Sensory ecosystem layer remains tactical-only. Strategic-hour resolution uses abstract encounter probabilities.
        if (turnManager?.CurrentTravelResolutionState != TravelResolutionState.StrategicHour)
        {
            _sensoryEventController.Update(delta);
        }
    }


    /// <summary>
    /// Registers new encounters with the travel morale overlay.
    ///
    /// Expected output:
    /// - Every spawned travel encounter gets morale tracking attached.
    /// - Arena systems remain untouched because this callback is phase-local.
    /// </summary>
    private void OnEncounterSpawned(TravelActiveEncounter encounter)
    {
        _moraleResolver.RegisterEncounter(encounter);
        _behaviorController.RegisterEncounter(encounter);
        _sensoryEventController.RegisterEncounter(encounter);
        _strategicEncounterResolver.RegisterEncounter(encounter);
    }

    /// <summary>
    /// Executes morale retreat orders through travel encounter orchestration.
    ///
    /// Expected output:
    /// - Encounter is moved into disengaging state.
    /// - Surviving creatures are removed safely from active travel threat groups.
    /// </summary>
    private void OnRetreatOrdered(TravelRetreatOrder retreatOrder)
    {
        _behaviorController.NotifyRetreatOrder(retreatOrder?.Encounter?.EncounterId);
        _encounterSpawner.ExecuteRetreatOrder(retreatOrder);
    }

    /// <summary>
    /// Removes disengaged encounters from morale tracking.
    /// </summary>
    private void OnEncounterDisengaged(TravelActiveEncounter encounter)
    {
        if (encounter == null)
        {
            return;
        }

        _moraleResolver.UnregisterEncounter(encounter.EncounterId);
        _behaviorController.UnregisterEncounter(encounter.EncounterId);
        _sensoryEventController.UnregisterEncounter(encounter.EncounterId);
        _strategicEncounterResolver.UnregisterEncounter(encounter.EncounterId);
    }


    /// <summary>
    /// Routes unified sensory stimuli into travel behavior so panic/investigation overlays stay phase-local.
    /// </summary>
    private void OnSensoryStimulusBroadcasted(TravelSensoryStimulus stimulus)
    {
        _behaviorController.ApplySensoryStimulus(stimulus);

        TurnManager turnManager = TurnManager.Instance;
        if (turnManager?.CurrentTravelResolutionState != TravelResolutionState.StrategicHour)
        {
            turnManager?.RegisterTravelResolutionEvent(TravelResolutionEvent.PerceptionEvent);
            turnManager?.RegisterTravelResolutionEvent(TravelResolutionEvent.NoiseDetected);
        }
    }

    /// <summary>
    /// Receives travel behavior handoff requests and starts existing combat flow.
    ///
    /// Expected output:
    /// - Travel overlay decides when engagement is valid.
    /// - Existing TurnManager combat startup takes over without combat AI duplication.
    /// - If combat is already active, duplicate starts are skipped safely.
    /// </summary>
    private void OnCombatEngagementRequested(TravelCombatEngagementRequest request)
    {
        if (request == null)
        {
            return;
        }

        TurnManager.Instance?.RegisterTravelResolutionEvent(TravelResolutionEvent.HostileActionDeclared);
        TurnManager.Instance?.RegisterTravelResolutionEvent(TravelResolutionEvent.AttackRollInitiated);


        RequestArenaTransition(new ArenaStartContext
        {
            IsSurpriseAttack = false,
            SurpriseSourceCreatureId = request.TriggerCreature?.GetInstanceId() ?? 0,
            IsTravelCombat = true
        });
    }

    /// <summary>
    /// Receives surprise escalation requests created by travel sensory pressure.
    ///
    /// Expected output:
    /// - Transition to Arena is initiated from travel without direct combat-rule ownership.
    /// - Arena receives a surprise flag so initiative handling can stay in Arena-owned logic.
    /// </summary>
    private void OnAmbushEscalationRequested(TravelAmbushEscalationRequest request)
    {
        if (request == null)
        {
            return;
        }

        TurnManager.Instance?.RegisterTravelResolutionEvent(TravelResolutionEvent.AggressionStateTriggered);
        TurnManager.Instance?.RegisterTravelResolutionEvent(TravelResolutionEvent.AttackRollInitiated);

        RequestArenaTransition(new ArenaStartContext
        {
            IsSurpriseAttack = request.IsSurpriseAttack,
            SurpriseSourceCreatureId = request.TriggerCreature?.GetInstanceId() ?? 0,
            IsTravelCombat = true
        });
    }

    private void RequestArenaTransition(ArenaStartContext arenaStartContext)
    {
        TurnManager turnManager = TurnManager.Instance;
        if (turnManager != null && turnManager.GetAllCombatants().Any())
        {
            return;
        }

        if (_runtime == null || _context == null)
        {
            return;
        }

        _context.SetArenaStartContext(arenaStartContext);

        if (_context.Owner is GamePhaseManager manager)
        {
            manager.SwitchPhase(GamePhaseType.Arena);
            return;
        }

        // Fallback keeps old behavior operational in rare host contexts where phase manager is unavailable.
        turnManager?.StartCombat();
    }

    /// <summary>
    /// Mark this phase complete when player reaches exit.
    /// </summary>
    private void OnPlayerReachedExit()
    {
        if (HasActivePartyCombat())
        {
            GD.Print("Exit denied: A party member is still in combat.");
            _exitRequested = false;
            return;
        }

        if (!_exitRequested)
        {
            _exitRequested = true;
            GD.Print("Exit available. Enter the exit zone again to return to arena, or continue traveling.");
            return;
        }

        // Strategic map lifetime is bound to this travel phase instance.
        // Exit remains player-confirmed while still guaranteeing full teardown once travel completes.
        IsCompleted = true;
    }

    /// <summary>
    /// Checks whether the player party is currently in combat.
    ///
    /// Expected output:
    /// - true when at least one living player/ally and one living enemy are both present in active combatants.
    /// - Enemies no longer in the Enemy group (for example travel retreat/flee resolution) are treated as out of combat for exit checks.
    /// - false when no active combat is currently involving the party.
    /// </summary>
    private static bool HasActivePartyCombat()
    {
        TurnManager turnManager = TurnManager.Instance;
        if (turnManager == null)
        {
            return false;
        }

        List<CreatureStats> activeCombatants = turnManager
            .GetAllCombatants()
            .Where(c =>
                c != null &&
                GodotObject.IsInstanceValid(c) &&
                c.Template != null &&
                !c.IsDead &&
                c.CurrentHP > -c.Template.Constitution)
            .ToList();

        bool partyInCombat = activeCombatants.Any(c => c.IsInGroup("Player") || c.IsInGroup("Ally"));
        bool enemiesInCombat = activeCombatants.Any(c => c.IsInGroup("Enemy"));

        return partyInCombat && enemiesInCombat;
    }
}
