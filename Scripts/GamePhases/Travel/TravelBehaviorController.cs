using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// TravelPhase-only behavioral overlay.
///
/// Design goals:
/// - Gives creatures non-combat behavior before engagement.
/// - Lives entirely inside TravelPhase lifecycle.
/// - Never edits combat AI logic.
/// - Hands off to existing combat systems when engagement is confirmed.
/// - Scales to large populations through relevance-based update cadence.
/// </summary>
public sealed class TravelBehaviorController
{
    private TravelBiomeQuerySnapshot _snapshot;
    private TravelBiomeMapRuntime _runtime;
    private GamePhaseContext _context;

    // All encounter actors and allies currently tracked by this phase-local overlay.
    private readonly Dictionary<CreatureStats, TravelBehaviorRuntimeState> _states = new();

    // Fast lookup to apply encounter-wide orders such as morale retreats.
    private readonly Dictionary<string, List<CreatureStats>> _encounterMembers = new();

    // Reusable random source for light roaming/patrol variance.
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    // Tick timing used to degrade awareness and dispatch periodic disturbance signals.
    private double _worldClockSeconds;
    private double _disturbancePulseAccumulator;

    // Prevents repeated combat handoff requests during the same transition window.
    private bool _combatHandoffQueued;

    // Short-lived scare pressure that temporarily pushes travel state toward panic/flee behavior.
    private double _scareOverrideUntilSeconds;

    // Optional travel tuning override for this run. If null, defaults are used.
    private TravelBehaviorGlobalTuning _globalTuning;


    /// <summary>
    /// Indicates whether travel behavior currently reports a threat posture.
    /// </summary>
    public bool HasThreatState => _states.Values.Any(state =>
        state != null &&
        (state.BehaviorState == TravelBehaviorState.Alert ||
         state.BehaviorState == TravelBehaviorState.Engaging ||
         state.BehaviorState == TravelBehaviorState.Pursuing ||
         state.BehaviorState == TravelBehaviorState.Investigating));

    /// <summary>
    /// Raised when travel behavior confirms that engagement should begin.
    ///
    /// Expected output:
    /// - TravelPhase can call existing combat startup methods.
    /// - No combat decisions are produced by this overlay.
    /// </summary>
    public event Action<TravelCombatEngagementRequest> CombatEngagementRequested;

    /// <summary>
    /// Starts the travel behavior overlay for one TravelPhase run.
    /// </summary>
    public void Initialize(
        TravelBiomeQuerySnapshot snapshot,
        TravelBiomeMapRuntime runtime,
        GamePhaseContext context,
        TravelBehaviorGlobalTuning globalTuning = null)
    {
        _snapshot = snapshot;
        _runtime = runtime;
        _context = context;
        _globalTuning = globalTuning ?? new TravelBehaviorGlobalTuning();

        _states.Clear();
        _encounterMembers.Clear();
        _worldClockSeconds = 0d;
        _disturbancePulseAccumulator = 0d;
        _combatHandoffQueued = false;
        _scareOverrideUntilSeconds = 0d;

        if (_snapshot?.Procedural != null && _snapshot.Procedural.Seed != 0)
        {
            _rng.Seed = (ulong)_snapshot.Procedural.Seed + 1337u;
        }
        else
        {
            _rng.Randomize();
        }

        RegisterAllies();
    }

    /// <summary>
    /// Registers encounter members after they are spawned by TravelEncounterSpawner.
    /// </summary>
    public void RegisterEncounter(TravelActiveEncounter encounter)
    {
        if (encounter == null || encounter.Members == null || encounter.Members.Count == 0)
        {
            return;
        }

        if (!_encounterMembers.ContainsKey(encounter.EncounterId))
        {
            _encounterMembers[encounter.EncounterId] = new List<CreatureStats>();
        }

        foreach (CreatureStats member in encounter.Members)
        {
            if (member == null || !GodotObject.IsInstanceValid(member))
            {
                continue;
            }

            TravelBehaviorArchetypeProfile profile = ResolveArchetypeProfile(encounter.Archetype);
            TravelBehaviorState state = ResolveInitialState(profile);

            var runtime = new TravelBehaviorRuntimeState
            {
                Creature = member,
                EncounterId = encounter.EncounterId,
                IsAlly = false,
                Archetype = encounter.Archetype,
                Profile = profile,
                BehaviorState = state,
                AwarenessLevel = profile.BaseAwareness,
                DetectionConfidence = 0f,
                LastKnownTargetPosition = new Vector3(float.NaN, float.NaN, float.NaN),
                TerritoryCenter = encounter.SpawnCenter,
                HomePosition = member.GlobalPosition,
                PatrolAnchor = member.GlobalPosition,
                NextUpdateAtSeconds = 0d,
                NextPatrolPointAtSeconds = 0d,
                CurrentTravelTarget = member.GlobalPosition,
                IsDormant = false,
                LastDetectionTimeSeconds = -999d,
                PendingDisturbanceTime = -999d,
                LastHearingContacts = 0,
                EngagementThreshold = Mathf.Clamp(profile.EngagementDetectionThreshold, 0f, 1f)
            };

            _states[member] = runtime;
            _encounterMembers[encounter.EncounterId].Add(member);
        }
    }

    /// <summary>
    /// Called when TravelMoraleResolver asks an encounter to retreat.
    /// </summary>
    public void NotifyRetreatOrder(string encounterId)
    {
        if (string.IsNullOrWhiteSpace(encounterId) || !_encounterMembers.TryGetValue(encounterId, out List<CreatureStats> members))
        {
            return;
        }

        foreach (CreatureStats member in members)
        {
            if (member == null || !_states.TryGetValue(member, out TravelBehaviorRuntimeState state))
            {
                continue;
            }

            state.BehaviorState = TravelBehaviorState.Retreating;
            state.AwarenessLevel = TravelAwarenessLevel.Engaged;
            state.DetectionConfidence = 1f;
            state.PendingRetreat = true;
            state.LastDetectionTimeSeconds = _worldClockSeconds;
            state.NextUpdateAtSeconds = 0d;
        }
    }

    /// <summary>
    /// Removes encounter members from tracking after disengagement cleanup.
    /// </summary>
    public void UnregisterEncounter(string encounterId)
    {
        if (string.IsNullOrWhiteSpace(encounterId))
        {
            return;
        }

        if (_encounterMembers.TryGetValue(encounterId, out List<CreatureStats> members))
        {
            foreach (CreatureStats member in members)
            {
                if (member != null)
                {
                    _states.Remove(member);
                }
            }
        }

        _encounterMembers.Remove(encounterId);
    }

    /// <summary>
    /// Per-frame travel behavior update.
    /// </summary>
    public void Update(double deltaSeconds)
    {
        if (_snapshot == null || _runtime == null || _context == null)
        {
            return;
        }

        _worldClockSeconds += deltaSeconds;
        _disturbancePulseAccumulator += deltaSeconds;

        CreatureStats player = ResolvePlayer();
        Vector3 playerPosition = player != null ? player.GlobalPosition : _runtime.PlayerSpawnPoint;

        if (PartyRosterRuntime.ActiveManager != null && player != null)
        {
            PartyRosterRuntime.ActiveManager.ApplyAlignmentFrictionDecay(player, (float)deltaSeconds);
        }

        if (_disturbancePulseAccumulator >= _globalTuning.DisturbancePulseSeconds)
        {
            _disturbancePulseAccumulator = 0d;
            BroadcastDisturbance(playerPosition, _globalTuning.DefaultDisturbanceStrength);
        }

        foreach (TravelBehaviorRuntimeState state in _states.Values.ToList())
        {
            if (state.Creature == null || !GodotObject.IsInstanceValid(state.Creature) || state.Creature.IsDead)
            {
                continue;
            }

            if (_worldClockSeconds < state.NextUpdateAtSeconds)
            {
                continue;
            }

            float distanceToPlayer = state.Creature.GlobalPosition.DistanceTo(playerPosition);
            state.IsDormant = distanceToPlayer > _globalTuning.DormantDistance;

            double interval = ResolveUpdateInterval(state, distanceToPlayer);
            state.NextUpdateAtSeconds = _worldClockSeconds + interval;

            UpdateAwareness(state, playerPosition, distanceToPlayer);

            if (_worldClockSeconds <= _scareOverrideUntilSeconds && !state.IsAlly)
            {
                state.BehaviorState = TravelBehaviorState.Panic;
            }

            UpdateStateMachine(state, playerPosition, distanceToPlayer, deltaSeconds);
            UpdateMovement(state, deltaSeconds);
            EvaluateCombatEngagement(state, playerPosition, distanceToPlayer);
        }
    }

    /// <summary>
    /// Applies sensory stimulus effects to behavior state in a lightweight, reversible way.
    ///
    /// Expected output:
    /// - Scare signals briefly push non-allies into panic/flee movement.
    /// - Curiosity signals can nudge nearby non-allies into investigation posture.
    /// - No permanent combat AI mutation occurs.
    /// </summary>
    public void ApplySensoryStimulus(TravelSensoryStimulus stimulus)
    {
        if (stimulus == null)
        {
            return;
        }

        if (stimulus.IsScareEvent)
        {
            _scareOverrideUntilSeconds = Math.Max(_scareOverrideUntilSeconds, _worldClockSeconds + 2.5d);

            foreach (TravelBehaviorRuntimeState state in _states.Values)
            {
                if (state.IsAlly || state.Creature == null || !GodotObject.IsInstanceValid(state.Creature))
                {
                    continue;
                }

                if (state.Creature.GlobalPosition.DistanceTo(stimulus.Position) > 18f)
                {
                    continue;
                }

                state.BehaviorState = TravelBehaviorState.Flee;
                state.CurrentTravelTarget = state.Creature.GlobalPosition + (state.Creature.GlobalPosition - stimulus.Position).Normalized() * 8f;
                state.NextUpdateAtSeconds = 0d;
            }

            return;
        }

        bool curiosityCue = stimulus.Channel == TravelSensoryChannel.Sound ||
                            stimulus.EventType == TravelSensoryEventType.FalseVisualLure;

        if (!curiosityCue)
        {
            return;
        }

        foreach (TravelBehaviorRuntimeState state in _states.Values)
        {
            if (state.IsAlly || state.Creature == null || !GodotObject.IsInstanceValid(state.Creature))
            {
                continue;
            }

            if (state.Creature.GlobalPosition.DistanceTo(stimulus.Position) > 14f)
            {
                continue;
            }

            state.BehaviorState = TravelBehaviorState.Investigating;
            state.LastKnownTargetPosition = stimulus.Position;
            state.CurrentTravelTarget = stimulus.Position;
            state.NextUpdateAtSeconds = 0d;
        }
    }

    /// <summary>
    /// Ends this travel overlay and clears all runtime references.
    /// </summary>
    public void Shutdown()
    {
        _states.Clear();
        _encounterMembers.Clear();

        _snapshot = null;
        _runtime = null;
        _context = null;

        _worldClockSeconds = 0d;
        _disturbancePulseAccumulator = 0d;
        _combatHandoffQueued = false;
        _scareOverrideUntilSeconds = 0d;
    }

    private void RegisterAllies()
    {
        IEnumerable<CreatureStats> party = _context?.CreaturePersistence?.PersistentCreatures ?? Enumerable.Empty<CreatureStats>();

        foreach (CreatureStats member in party)
        {
            if (member == null || !GodotObject.IsInstanceValid(member))
            {
                continue;
            }

            bool isPlayer = member.IsInGroup("Player");
            bool isAlly = member.IsInGroup("Ally") || isPlayer;
            if (!isAlly)
            {
                continue;
            }

            var state = new TravelBehaviorRuntimeState
            {
                Creature = member,
                EncounterId = string.Empty,
                IsAlly = true,
                Archetype = null,
                Profile = BuildDefaultAllyProfile(),
                BehaviorState = isPlayer ? TravelBehaviorState.Guard : TravelBehaviorState.Roaming,
                AwarenessLevel = TravelAwarenessLevel.Alert,
                DetectionConfidence = 0f,
                TerritoryCenter = _runtime.PlayerSpawnPoint,
                HomePosition = member.GlobalPosition,
                PatrolAnchor = member.GlobalPosition,
                CurrentTravelTarget = member.GlobalPosition,
                LastKnownTargetPosition = new Vector3(float.NaN, float.NaN, float.NaN),
                NextUpdateAtSeconds = 0d,
                NextPatrolPointAtSeconds = 0d,
                IsDormant = false,
                LastDetectionTimeSeconds = -999d,
                PendingDisturbanceTime = -999d,
                LastHearingContacts = 0,
                EngagementThreshold = 1.0f
            };

            _states[member] = state;
        }
    }

    private TravelBehaviorArchetypeProfile ResolveArchetypeProfile(TravelEncounterArchetypeDefinition archetype)
    {
        if (archetype?.BehaviorProfile != null)
        {
            return archetype.BehaviorProfile;
        }

        if (string.Equals(archetype?.ArchetypeId, "ambush_predator", StringComparison.OrdinalIgnoreCase))
        {
            return TravelBehaviorArchetypeProfile.CreateAmbushDefault();
        }

        if (string.Equals(archetype?.ArchetypeId, "passive_defensive", StringComparison.OrdinalIgnoreCase))
        {
            return TravelBehaviorArchetypeProfile.CreatePassiveDefault();
        }

        return TravelBehaviorArchetypeProfile.CreateTerritorialDefault();
    }

    private static TravelBehaviorState ResolveInitialState(TravelBehaviorArchetypeProfile profile)
    {
        if (profile == null)
        {
            return TravelBehaviorState.Idle;
        }

        if (profile.PreferredInitialStates == null || profile.PreferredInitialStates.Count == 0)
        {
            return TravelBehaviorState.Idle;
        }

        return profile.PreferredInitialStates[0];
    }

    private static TravelBehaviorArchetypeProfile BuildDefaultAllyProfile()
    {
        return new TravelBehaviorArchetypeProfile
        {
            PreferredInitialStates = new Godot.Collections.Array<TravelBehaviorState> { TravelBehaviorState.Guard },
            DetectionGainPerSecond = 0.6f,
            DetectionDecayPerSecond = 0.35f,
            SuspicionThreshold = 0.2f,
            AlertThreshold = 0.55f,
            EngagementDetectionThreshold = 1.0f,
            InvestigationDurationSeconds = 3f,
            TrackingDistance = 14f,
            TerritoryRadius = 9f,
            RoamingRadius = 3f,
            PatrolPointChangeSeconds = 4f,
            ChaseDistance = 8f,
            MinEngagementDistance = 1.5f,
            RequiresDirectThreatToEngage = true,
            BaseAwareness = TravelAwarenessLevel.Alert,
            CanAmbush = false,
            SupportsInvestigation = true,
            MoraleBreakThreshold = 0.25f,
            MoveSpeed = 4.2f,
            HiddenDetectionPenalty = 0f
        };
    }

    private static double ResolveUpdateInterval(TravelBehaviorRuntimeState state, float distanceToPlayer)
    {
        if (state.IsDormant)
        {
            return 0.75d;
        }

        if (distanceToPlayer > 35f)
        {
            return 0.4d;
        }

        if (distanceToPlayer > 18f)
        {
            return 0.2d;
        }

        return 0.05d;
    }

    private void UpdateAwareness(TravelBehaviorRuntimeState state, Vector3 playerPosition, float distanceToPlayer)
    {
        TravelBehaviorArchetypeProfile profile = state.Profile;
        if (profile == null)
        {
            return;
        }

        float morale = ResolveMorale(state.Creature);
        MoraleBand moraleBand = MoraleLoyaltyResolver.ResolveMoraleBand(morale);

        float visibilityPenalty = Mathf.Clamp(_snapshot?.Environmental?.VisibilityPenalty ?? 0f, 0f, 1f);
        float hiddenPenalty = state.BehaviorState == TravelBehaviorState.Hidden ? Mathf.Clamp(profile.HiddenDetectionPenalty, 0f, 1f) : 0f;

        float losDistance = Mathf.Max(2f, profile.TrackingDistance);
        bool lineOfSight = distanceToPlayer <= losDistance * (1f - visibilityPenalty * 0.5f);

        int hearingContacts = 0;
        if (_worldClockSeconds - state.PendingDisturbanceTime <= 1.2d)
        {
            hearingContacts = 1;
        }

        float gain = 0f;
        if (lineOfSight)
        {
            float normalized = 1f - Mathf.Clamp(distanceToPlayer / Mathf.Max(0.01f, losDistance), 0f, 1f);
            gain += normalized * profile.DetectionGainPerSecond;
        }

        if (hearingContacts > 0)
        {
            gain += profile.DetectionGainPerSecond * 0.35f;
        }

        gain *= 1f - hiddenPenalty;

        if (state.IsAlly)
        {
            // Lower-morale allies escalate awareness less reliably and hesitate to engage stimuli.
            float moraleAwarenessBias = moraleBand switch
            {
                MoraleBand.Steadfast => 1.15f,
                MoraleBand.Stable => 1.05f,
                MoraleBand.Unsteady => 0.9f,
                MoraleBand.Shaken => 0.75f,
                _ => 0.6f
            };

            gain *= moraleAwarenessBias;
        }

        float decay = profile.DetectionDecayPerSecond;
        if (lineOfSight || hearingContacts > 0)
        {
            decay *= 0.25f;
        }

        float dt = (float)Mathf.Max(0.01d, state.NextUpdateAtSeconds - (_worldClockSeconds - ResolveUpdateInterval(state, distanceToPlayer)));
        state.DetectionConfidence = Mathf.Clamp(state.DetectionConfidence + (gain - decay) * dt, 0f, 1f);

        if (lineOfSight || hearingContacts > 0)
        {
            state.LastKnownTargetPosition = playerPosition;
            state.LastDetectionTimeSeconds = _worldClockSeconds;
        }

        state.LineOfSight = lineOfSight;
        state.LastHearingContacts = hearingContacts;

        if (state.DetectionConfidence >= profile.AlertThreshold)
        {
            state.AwarenessLevel = TravelAwarenessLevel.Alert;
        }
        else if (state.DetectionConfidence >= profile.SuspicionThreshold)
        {
            state.AwarenessLevel = TravelAwarenessLevel.Suspicious;
        }
        else
        {
            state.AwarenessLevel = TravelAwarenessLevel.Unaware;
        }
    }

    private void UpdateStateMachine(TravelBehaviorRuntimeState state, Vector3 playerPosition, float distanceToPlayer, double deltaSeconds)
    {
        if (state.IsAlly)
        {
            UpdateAllyStateMachine(state, playerPosition, distanceToPlayer);
            return;
        }

        if (state.PendingRetreat)
        {
            state.BehaviorState = TravelBehaviorState.Retreating;
            return;
        }

        if (_worldClockSeconds <= _scareOverrideUntilSeconds)
        {
            if (state.BehaviorState != TravelBehaviorState.Flee)
            {
                state.BehaviorState = TravelBehaviorState.Panic;
            }

            return;
        }

        if (state.BehaviorState == TravelBehaviorState.Panic || state.BehaviorState == TravelBehaviorState.Flee)
        {
            state.BehaviorState = TravelBehaviorState.ReturningToTerritory;
        }

        TravelBehaviorArchetypeProfile profile = state.Profile;

        if (state.AwarenessLevel == TravelAwarenessLevel.Unaware)
        {
            if (state.BehaviorState == TravelBehaviorState.Suspicious ||
                state.BehaviorState == TravelBehaviorState.Investigating ||
                state.BehaviorState == TravelBehaviorState.Alert ||
                state.BehaviorState == TravelBehaviorState.Tracking)
            {
                state.BehaviorState = TravelBehaviorState.ReturningToTerritory;
            }

            if (state.BehaviorState == TravelBehaviorState.ReturningToTerritory &&
                state.Creature.GlobalPosition.DistanceTo(state.TerritoryCenter) <= 1.2f)
            {
                state.BehaviorState = ResolveInitialState(profile);
            }

            if (state.BehaviorState == TravelBehaviorState.Idle && profile.RoamingRadius > 0.1f)
            {
                state.BehaviorState = TravelBehaviorState.Roaming;
            }
        }
        else if (state.AwarenessLevel == TravelAwarenessLevel.Suspicious)
        {
            if (state.BehaviorState == TravelBehaviorState.Idle || state.BehaviorState == TravelBehaviorState.Hidden || state.BehaviorState == TravelBehaviorState.Roaming || state.BehaviorState == TravelBehaviorState.Patrol || state.BehaviorState == TravelBehaviorState.Guard)
            {
                state.BehaviorState = TravelBehaviorState.Suspicious;
            }

            if (profile.SupportsInvestigation && state.BehaviorState == TravelBehaviorState.Suspicious)
            {
                state.BehaviorState = TravelBehaviorState.Investigating;
            }
        }
        else
        {
            if (state.BehaviorState == TravelBehaviorState.Hidden && profile.CanAmbush)
            {
                state.BehaviorState = TravelBehaviorState.AmbushReady;
            }
            else if (state.BehaviorState != TravelBehaviorState.AmbushReady)
            {
                state.BehaviorState = TravelBehaviorState.Tracking;
            }

            if (distanceToPlayer <= profile.ChaseDistance)
            {
                state.BehaviorState = TravelBehaviorState.Pursuing;
            }
        }

        if (state.BehaviorState == TravelBehaviorState.Investigating)
        {
            if (_worldClockSeconds - state.LastDetectionTimeSeconds > profile.InvestigationDurationSeconds)
            {
                state.BehaviorState = TravelBehaviorState.ReturningToTerritory;
            }
        }

        if (state.BehaviorState == TravelBehaviorState.Pursuing && distanceToPlayer > profile.TrackingDistance * 1.4f)
        {
            state.BehaviorState = TravelBehaviorState.Disengaging;
        }

        if (state.BehaviorState == TravelBehaviorState.Disengaging)
        {
            state.BehaviorState = TravelBehaviorState.ReturningToTerritory;
        }
    }

    private static void UpdateAllyStateMachine(TravelBehaviorRuntimeState state, Vector3 playerPosition, float distanceToPlayer)
    {
        if (state.Creature.IsInGroup("Player"))
        {
            state.BehaviorState = TravelBehaviorState.Guard;
            state.CurrentTravelTarget = playerPosition;
            return;
        }

        float morale = ResolveMorale(state.Creature);
        MoraleBand moraleBand = MoraleLoyaltyResolver.ResolveMoraleBand(morale);
        float formationRadius = Mathf.Max(1.5f, state.Profile.TerritoryRadius * 0.5f);

        // Low morale allies lag, avoid dangerous scouting, and prefer retreat-ready postures.
        if (moraleBand == MoraleBand.Broken || moraleBand == MoraleBand.Shaken)
        {
            if (distanceToPlayer > formationRadius * 0.75f)
            {
                state.BehaviorState = TravelBehaviorState.Tracking;
                state.CurrentTravelTarget = playerPosition;
                return;
            }

            state.BehaviorState = TravelBehaviorState.Guard;
            state.CurrentTravelTarget = playerPosition - new Vector3(0.8f, 0f, 0.8f);
            return;
        }

        if (distanceToPlayer > formationRadius)
        {
            state.BehaviorState = TravelBehaviorState.Tracking;
            state.CurrentTravelTarget = playerPosition;
            return;
        }

        if (state.AwarenessLevel == TravelAwarenessLevel.Suspicious && state.Profile.SupportsInvestigation)
        {
            state.BehaviorState = TravelBehaviorState.Investigating;
            state.CurrentTravelTarget = state.LastKnownTargetPosition.IsFinite() ? state.LastKnownTargetPosition : playerPosition;
            return;
        }

        state.BehaviorState = TravelBehaviorState.Guard;
        state.CurrentTravelTarget = playerPosition;
    }

    private void UpdateMovement(TravelBehaviorRuntimeState state, double deltaSeconds)
    {
        TravelBehaviorArchetypeProfile profile = state.Profile;
        if (profile == null)
        {
            return;
        }

        Vector3 target = state.Creature.GlobalPosition;

        switch (state.BehaviorState)
        {
            case TravelBehaviorState.Idle:
            case TravelBehaviorState.Hidden:
            case TravelBehaviorState.Guard:
                target = state.HomePosition;
                break;

            case TravelBehaviorState.Roaming:
            case TravelBehaviorState.Patrol:
                if (_worldClockSeconds >= state.NextPatrolPointAtSeconds)
                {
                    Vector2 radial = new Vector2(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f)).Normalized();
                    float radius = Mathf.Max(0.5f, profile.RoamingRadius);
                    Vector3 offset = new Vector3(radial.X, 0f, radial.Y) * _rng.RandfRange(radius * 0.35f, radius);
                    state.CurrentTravelTarget = state.TerritoryCenter + offset;
                    state.NextPatrolPointAtSeconds = _worldClockSeconds + Mathf.Max(0.5f, profile.PatrolPointChangeSeconds);
                }

                target = state.CurrentTravelTarget;
                break;

            case TravelBehaviorState.Suspicious:
            case TravelBehaviorState.Investigating:
            case TravelBehaviorState.Alert:
            case TravelBehaviorState.Tracking:
            case TravelBehaviorState.Pursuing:
            case TravelBehaviorState.AmbushReady:
            case TravelBehaviorState.Panic:
                if (state.LastKnownTargetPosition.IsFinite())
                {
                    target = state.LastKnownTargetPosition;
                }
                break;

            case TravelBehaviorState.Retreating:
            case TravelBehaviorState.Disengaging:
                target = state.TerritoryCenter + (state.TerritoryCenter - ResolvePlayerPosition()).Normalized() * 8f;
                break;

            case TravelBehaviorState.Flee:
                target = state.CurrentTravelTarget;
                break;

            case TravelBehaviorState.ReturningToTerritory:
                target = state.TerritoryCenter;
                break;
        }

        MoveCreatureToward(state.Creature, target, profile.MoveSpeed, deltaSeconds);
    }

    private void EvaluateCombatEngagement(TravelBehaviorRuntimeState state, Vector3 playerPosition, float distanceToPlayer)
    {
        if (_combatHandoffQueued || state.IsAlly || state.PendingRetreat)
        {
            return;
        }

        TravelBehaviorArchetypeProfile profile = state.Profile;
        if (profile == null)
        {
            return;
        }

        if (profile.RequiresDirectThreatToEngage)
        {
            return;
        }

        bool thresholdReached = state.DetectionConfidence >= state.EngagementThreshold;
        bool closeEnough = distanceToPlayer <= Mathf.Max(0.5f, profile.MinEngagementDistance);
        bool postureAllows = state.BehaviorState == TravelBehaviorState.Pursuing || state.BehaviorState == TravelBehaviorState.AmbushReady || state.BehaviorState == TravelBehaviorState.Tracking;

        if (!thresholdReached || !closeEnough || !postureAllows)
        {
            return;
        }

        _combatHandoffQueued = true;
        state.BehaviorState = TravelBehaviorState.Engaging;
        state.AwarenessLevel = TravelAwarenessLevel.Engaged;

        CombatEngagementRequested?.Invoke(new TravelCombatEngagementRequest
        {
            EncounterId = state.EncounterId,
            TriggerCreature = state.Creature,
            TriggerPosition = playerPosition,
            TriggerReason = TravelEngagementReason.DetectionThreshold,
            TimestampSeconds = _worldClockSeconds
        });
    }

    private void BroadcastDisturbance(Vector3 source, float strength)
    {
        foreach (TravelBehaviorRuntimeState state in _states.Values)
        {
            if (state.Creature == null || !GodotObject.IsInstanceValid(state.Creature) || state.Creature.IsDead)
            {
                continue;
            }

            float hearingRange = Mathf.Max(4f, state.Profile.TrackingDistance) * Mathf.Clamp(strength, 0.1f, 3f);
            float distance = state.Creature.GlobalPosition.DistanceTo(source);
            if (distance > hearingRange)
            {
                continue;
            }

            state.PendingDisturbanceTime = _worldClockSeconds;
        }
    }

    private Vector3 ResolvePlayerPosition()
    {
        CreatureStats player = ResolvePlayer();
        return player != null ? player.GlobalPosition : _runtime?.PlayerSpawnPoint ?? Vector3.Zero;
    }


    private static float ResolveMorale(CreatureStats creature)
    {
        if (PartyRosterRuntime.ActiveManager != null && PartyRosterRuntime.ActiveManager.TryGetMemberState(creature, out PartyMemberState state))
        {
            return state.CurrentMorale;
        }

        return 0.7f;
    }

    private CreatureStats ResolvePlayer()
    {
        return _context?.CreaturePersistence?.PersistentCreatures?.FirstOrDefault(c => c != null && c.IsInGroup("Player"));
    }

    private static void MoveCreatureToward(CreatureStats creature, Vector3 target, float speed, double deltaSeconds)
    {
        if (creature == null || !GodotObject.IsInstanceValid(creature))
        {
            return;
        }

        Vector3 current = creature.GlobalPosition;
        Vector3 toTarget = target - current;
        toTarget.Y = 0f;

        if (toTarget.LengthSquared() <= 0.0001f)
        {
            return;
        }

        float maxStep = Mathf.Max(0.1f, speed) * (float)deltaSeconds;
        Vector3 step = toTarget.Length() <= maxStep ? toTarget : toTarget.Normalized() * maxStep;
        creature.GlobalPosition = current + step;
    }
}

/// <summary>
/// Lightweight per-creature travel runtime state.
/// </summary>
public sealed class TravelBehaviorRuntimeState
{
    public CreatureStats Creature;
    public string EncounterId;
    public bool IsAlly;
    public TravelEncounterArchetypeDefinition Archetype;
    public TravelBehaviorArchetypeProfile Profile;

    public TravelBehaviorState BehaviorState;
    public TravelAwarenessLevel AwarenessLevel;
    public float DetectionConfidence;
    public float EngagementThreshold;

    public bool LineOfSight;
    public int LastHearingContacts;

    public Vector3 TerritoryCenter;
    public Vector3 HomePosition;
    public Vector3 PatrolAnchor;
    public Vector3 CurrentTravelTarget;
    public Vector3 LastKnownTargetPosition;

    public double NextUpdateAtSeconds;
    public double NextPatrolPointAtSeconds;
    public double LastDetectionTimeSeconds;
    public double PendingDisturbanceTime;

    public bool PendingRetreat;
    public bool IsDormant;
}

/// <summary>
/// Travel behavior states used in the phase-local finite state model.
/// </summary>
public enum TravelBehaviorState
{
    Idle,
    Roaming,
    Patrol,
    Guard,
    Hidden,

    Suspicious,
    Investigating,
    Alert,
    Tracking,

    AmbushReady,
    Pursuing,
    Engaging,

    Panic,
    Flee,

    Retreating,
    Disengaging,
    ReturningToTerritory
}

/// <summary>
/// Awareness progression used by travel behavior before combat begins.
/// </summary>
public enum TravelAwarenessLevel
{
    Unaware,
    Suspicious,
    Alert,
    Engaged
}

/// <summary>
/// Data sent when travel overlay requests handoff into existing combat systems.
/// </summary>
public sealed class TravelCombatEngagementRequest
{
    public string EncounterId;
    public CreatureStats TriggerCreature;
    public Vector3 TriggerPosition;
    public TravelEngagementReason TriggerReason;
    public double TimestampSeconds;
}

/// <summary>
/// Human-readable engagement reason for telemetry and quest hooks.
/// </summary>
public enum TravelEngagementReason
{
    DetectionThreshold,
    ThreatenedAlly,
    PlayerInitiated
}

/// <summary>
/// Global travel behavior tuning that can be adjusted in inspector-driven assets.
/// </summary>
[GlobalClass]
public partial class TravelBehaviorGlobalTuning : Resource
{
    [Export(PropertyHint.Range, "5,120,1")]
    public float DormantDistance = 45f;

    [Export(PropertyHint.Range, "0.1,10,0.1")]
    public float DisturbancePulseSeconds = 1.0f;

    [Export(PropertyHint.Range, "0,3,0.01")]
    public float DefaultDisturbanceStrength = 1.0f;
}

/// <summary>
/// Archetype-driven behavior profile for TravelBehaviorController.
/// </summary>
[GlobalClass]
public partial class TravelBehaviorArchetypeProfile : Resource
{
    [Export]
    public Godot.Collections.Array<TravelBehaviorState> PreferredInitialStates = new();

    [Export(PropertyHint.Range, "0,2,0.01")]
    public float DetectionGainPerSecond = 0.45f;

    [Export(PropertyHint.Range, "0,2,0.01")]
    public float DetectionDecayPerSecond = 0.2f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float SuspicionThreshold = 0.2f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float AlertThreshold = 0.55f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float EngagementDetectionThreshold = 0.85f;

    [Export(PropertyHint.Range, "0,30,0.1")]
    public float InvestigationDurationSeconds = 4f;

    [Export(PropertyHint.Range, "1,80,0.1")]
    public float TrackingDistance = 16f;

    [Export(PropertyHint.Range, "1,60,0.1")]
    public float TerritoryRadius = 10f;

    [Export(PropertyHint.Range, "0,30,0.1")]
    public float RoamingRadius = 4f;

    [Export(PropertyHint.Range, "0.25,20,0.1")]
    public float PatrolPointChangeSeconds = 4f;

    [Export(PropertyHint.Range, "1,40,0.1")]
    public float ChaseDistance = 9f;

    [Export(PropertyHint.Range, "0.25,10,0.1")]
    public float MinEngagementDistance = 2f;

    [Export]
    public bool RequiresDirectThreatToEngage = false;

    [Export]
    public TravelAwarenessLevel BaseAwareness = TravelAwarenessLevel.Unaware;

    [Export]
    public bool CanAmbush = false;

    [Export]
    public bool SupportsInvestigation = true;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MoraleBreakThreshold = 0.3f;

    [Export(PropertyHint.Range, "0.5,10,0.1")]
    public float MoveSpeed = 2.8f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float HiddenDetectionPenalty = 0.2f;

    public static TravelBehaviorArchetypeProfile CreateTerritorialDefault()
    {
        return new TravelBehaviorArchetypeProfile
        {
            PreferredInitialStates = new Godot.Collections.Array<TravelBehaviorState> { TravelBehaviorState.Guard, TravelBehaviorState.Patrol },
            BaseAwareness = TravelAwarenessLevel.Unaware,
            RequiresDirectThreatToEngage = false,
            CanAmbush = false,
            SupportsInvestigation = true,
            DetectionGainPerSecond = 0.5f,
            DetectionDecayPerSecond = 0.18f,
            EngagementDetectionThreshold = 0.8f,
            TrackingDistance = 15f,
            ChaseDistance = 9f,
            MoveSpeed = 2.9f,
            HiddenDetectionPenalty = 0.05f
        };
    }

    public static TravelBehaviorArchetypeProfile CreateAmbushDefault()
    {
        return new TravelBehaviorArchetypeProfile
        {
            PreferredInitialStates = new Godot.Collections.Array<TravelBehaviorState> { TravelBehaviorState.Hidden, TravelBehaviorState.AmbushReady },
            BaseAwareness = TravelAwarenessLevel.Unaware,
            RequiresDirectThreatToEngage = false,
            CanAmbush = true,
            SupportsInvestigation = true,
            DetectionGainPerSecond = 0.65f,
            DetectionDecayPerSecond = 0.12f,
            EngagementDetectionThreshold = 0.72f,
            TrackingDistance = 13f,
            ChaseDistance = 8f,
            MoveSpeed = 3.2f,
            HiddenDetectionPenalty = 0.35f
        };
    }

    public static TravelBehaviorArchetypeProfile CreatePassiveDefault()
    {
        return new TravelBehaviorArchetypeProfile
        {
            PreferredInitialStates = new Godot.Collections.Array<TravelBehaviorState> { TravelBehaviorState.Idle, TravelBehaviorState.Roaming },
            BaseAwareness = TravelAwarenessLevel.Unaware,
            RequiresDirectThreatToEngage = true,
            CanAmbush = false,
            SupportsInvestigation = true,
            DetectionGainPerSecond = 0.3f,
            DetectionDecayPerSecond = 0.28f,
            EngagementDetectionThreshold = 0.95f,
            TrackingDistance = 11f,
            ChaseDistance = 6f,
            MoveSpeed = 2.2f,
            HiddenDetectionPenalty = 0f
        };
    }
}
