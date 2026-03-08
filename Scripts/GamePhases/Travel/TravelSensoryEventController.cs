using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Travel-only sensory layer that sits above normal movement and encounter spawning.
///
/// Design promise:
/// - This system never replaces encounters.
/// - This system never forces combat.
/// - This system continuously mixes authentic biome life with creature mimicry.
/// - This system keeps both channels (vision and sound) in one stream so the player cannot trivially separate truth from bait.
/// </summary>
public sealed class TravelSensoryEventController
{
    private TravelBiomeQuerySnapshot _snapshot;
    private TravelBiomeMapRuntime _runtime;
    private GamePhaseContext _context;

    // Single RNG per travel run so event timing feels alive but stable under the same seed.
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    // Encounter registry used for mimic-driven stimuli.
    private readonly Dictionary<string, TravelActiveEncounter> _encountersById = new();

    // Runtime records for stimuli currently visible/audible in the travel layer.
    private readonly Dictionary<string, TravelSensoryStimulus> _activeStimuli = new();

    // Physical vision actors generated from sensory stimuli.
    private readonly Dictionary<string, Node3D> _spawnedVisualActors = new();

    // Timers that pace authentic world events and mimic broadcasts.
    private double _worldClockSeconds;
    private double _nextAuthenticEventAtSeconds;
    private double _nextMimicCheckAtSeconds;

    // Per-creature cooldown memory so mimic predators cannot spam the same bait in lockstep.
    private readonly Dictionary<ulong, double> _creatureNextMimicAtSeconds = new();

    // Travel sensory configuration kept local to this system to avoid hardcoded ecology numbers.
    private readonly TravelSensoryConfig _config = new();

    // Simple noise proxy used by awareness logic; can later be replaced by richer travel acoustics.
    private float _stubPartyNoiseLevel;

    // Tracks party movement direction so lure-based ambush rules can validate that the party moved deeper into danger.

    // Delayed escalation queue so ambushes feel like predator timing rather than instant teleports into combat.
    private readonly Dictionary<string, double> _pendingAmbushTriggerTimes = new();


    /// <summary>
    /// True when unresolved sensory events still exist in the current travel turn context.
    /// </summary>
    public bool HasRecentStimulus => _activeStimuli.Values.Any(stimulus => stimulus != null && !stimulus.IsResolved);

    /// <summary>
    /// Raised every time a new sensory stimulus is created.
    ///
    /// Expected output:
    /// - TravelBehaviorController can respond with investigation, panic, or avoidance overlays.
    /// - Player perception systems can render UI hints.
    /// - Ally systems can react without touching combat AI internals.
    /// </summary>
    public event Action<TravelSensoryStimulus> SensoryStimulusBroadcasted;

    /// <summary>
    /// Raised when a stimulus resolves into a meaningful gameplay reward opportunity.
    ///
    /// Expected output:
    /// - XP/loot/reputation hooks can be attached by higher-level systems.
    /// - The sensory layer remains lightweight and does not build quest trees.
    /// </summary>
    public event Action<TravelSensoryRewardOpportunity> RewardOpportunityGenerated;

    /// <summary>
    /// Raised when a travel stimulus escalates into a surprise-attack handoff request.
    ///
    /// Expected output:
    /// - TravelPhase can perform a clean travel -> arena transition.
    /// - Arena startup receives context flags without this sensory layer owning combat resolution rules.
    /// </summary>
    public event Action<TravelAmbushEscalationRequest> AmbushEscalationRequested;

    /// <summary>
    /// Starts the sensory event stream for one travel run.
    /// </summary>
    public void Initialize(TravelBiomeQuerySnapshot snapshot, TravelBiomeMapRuntime runtime, GamePhaseContext context)
    {
        _snapshot = snapshot;
        _runtime = runtime;
        _context = context;

        _encountersById.Clear();
        _activeStimuli.Clear();
        _spawnedVisualActors.Clear();
        _creatureNextMimicAtSeconds.Clear();
        _pendingAmbushTriggerTimes.Clear();

        _worldClockSeconds = 0d;

        if (_snapshot?.Procedural != null && _snapshot.Procedural.Seed != 0)
        {
            _rng.Seed = (ulong)_snapshot.Procedural.Seed + 4242u;
        }
        else
        {
            _rng.Randomize();
        }

        _nextAuthenticEventAtSeconds = _rng.RandfRange(4f, 9f);
        _nextMimicCheckAtSeconds = _rng.RandfRange(2f, 5f);
    }

    /// <summary>
    /// Registers encounter data so mimic creatures can emit sensory events through the same stream.
    /// </summary>
    public void RegisterEncounter(TravelActiveEncounter encounter)
    {
        if (encounter == null || string.IsNullOrWhiteSpace(encounter.EncounterId))
        {
            return;
        }

        _encountersById[encounter.EncounterId] = encounter;
    }

    /// <summary>
    /// Removes encounter data when travel encounter lifecycle ends.
    /// </summary>
    public void UnregisterEncounter(string encounterId)
    {
        if (string.IsNullOrWhiteSpace(encounterId))
        {
            return;
        }

        _encountersById.Remove(encounterId);
        _pendingAmbushTriggerTimes.Remove(encounterId);
    }

    /// <summary>
    /// Per-frame update for authentic event generation and mimic signal routing.
    /// </summary>
    public void Update(double deltaSeconds)
    {
        if (_snapshot == null || _runtime == null || _context == null)
        {
            return;
        }

        _worldClockSeconds += deltaSeconds;
        Vector3 partyCenter = ResolvePartyCenter();

        UpdateEncounterAwareness((float)deltaSeconds, partyCenter);
        EvaluateAwareRangeAmbushEscalation(partyCenter);
        EvaluatePendingAmbushEscalations(partyCenter);

        if (_worldClockSeconds >= _nextAuthenticEventAtSeconds)
        {
            CreateAuthenticStimulus();
            _nextAuthenticEventAtSeconds = _worldClockSeconds + _rng.RandfRange(8f, 15f);
        }

        if (_worldClockSeconds >= _nextMimicCheckAtSeconds)
        {
            TryCreateMimicStimulus();
            _nextMimicCheckAtSeconds = _worldClockSeconds + _rng.RandfRange(4f, 8f);
        }

        CleanupExpiredStimuli();
    }

    /// <summary>
    /// Reports how the player/allies handled a sensory event.
    ///
    /// Expected output:
    /// - Higher-level systems can listen for reward hooks.
    /// - Ignoring events remains valid and is treated as a safe low-yield choice.
    /// </summary>
    public void ReportInteractionOutcome(string stimulusId, TravelSensoryInteractionChoice choice, bool succeeded)
    {
        if (string.IsNullOrWhiteSpace(stimulusId) || !_activeStimuli.TryGetValue(stimulusId, out TravelSensoryStimulus stimulus))
        {
            return;
        }

        TravelSensoryRewardOpportunity reward = BuildRewardOpportunity(stimulus, choice, succeeded);
        if (reward != null)
        {
            RewardOpportunityGenerated?.Invoke(reward);
        }

        if (!stimulus.IsAuthentic && !string.IsNullOrWhiteSpace(stimulus.EncounterId) && _encountersById.TryGetValue(stimulus.EncounterId, out TravelActiveEncounter encounter))
        {
            encounter.ApplyStimulusFeedback(choice, succeeded, _config, stimulus.AwarenessModifier);

            // Investigating a predator lure is intentionally risky: once awareness rises, this choice can unlock ambush timing.
            if (choice == TravelSensoryInteractionChoice.Investigate && CanEscalateToAmbush(encounter, ResolvePartyCenter()) && EvaluateAmbushEligibilityGuards(encounter, stimulus, ResolvePartyCenter()))
            {
                QueueAmbushEscalation(encounter, stimulus.SourceCreature, TravelAmbushEscalationReason.PlayerInvestigatedStimulus, stimulus.StimulusId);
            }
        }

        stimulus.IsResolved = true;
    }

    private void UpdateEncounterAwareness(float deltaSeconds, Vector3 partyCenter)
    {
        if (_encountersById.Count == 0)
        {
            return;
        }

        _stubPartyNoiseLevel = ResolveStubPartyNoiseLevel();

        foreach (TravelActiveEncounter encounter in _encountersById.Values)
        {
            if (encounter == null || encounter.IsDisengaged || encounter.IsDisengaging)
            {
                continue;
            }

            CreatureStats playerLeader = TurnManager.Instance?.GetPlayerLeader();
            encounter.UpdateAwareness(deltaSeconds, partyCenter, _stubPartyNoiseLevel, _config, _worldClockSeconds, _context?.Intelligence ?? IntelligenceSystemRuntime.Manager, playerLeader);
        }
    }

    /// <summary>
    /// Shared eligibility gate for surprise escalation from travel stimuli into arena handoff.
    ///
    /// Expected output:
    /// - Returns true only when awareness is fully Aware and the predator is within configured ambush distance.
    /// - Returns false whenever travel conditions are no longer valid for a fair surprise setup.
    /// </summary>
    private bool CanEscalateToAmbush(TravelActiveEncounter encounter, Vector3 partyCenter)
    {
        return encounter?.CanEscalateToAmbush(encounter, partyCenter, _config, isTravelPhaseActive: _snapshot != null && _runtime != null && _context != null, IsCreatureInCombat) == true;
    }

    /// <summary>
    /// Evaluates natural proximity-driven ambush pressure while the predator is already aware.
    ///
    /// Expected output:
    /// - Close, aware predators may launch a surprise with a simple probability roll.
    /// - Failed rolls preserve tension but do not force encounter escalation.
    /// </summary>
    private void EvaluateAwareRangeAmbushEscalation(Vector3 partyCenter)
    {
        foreach (TravelActiveEncounter encounter in _encountersById.Values)
        {
            if (!CanEscalateToAmbush(encounter, partyCenter) || !EvaluateAmbushEligibilityGuards(encounter, null, partyCenter))
            {
                continue;
            }

            float adjustedAmbushChance = ResolveAdjustedAmbushChance();
            if (_rng.Randf() > adjustedAmbushChance)
            {
                continue;
            }

            CreatureStats source = encounter.Members?.FirstOrDefault(member => member != null && GodotObject.IsInstanceValid(member) && !member.IsDead && !IsCreatureInCombat(member));
            QueueAmbushEscalation(encounter, source, TravelAmbushEscalationReason.AwareProximity, null);
        }
    }

    /// <summary>
    /// Handles delayed ambush requests so predators can spring from cover after a short setup window.
    ///
    /// Expected output:
    /// - Escalation triggers once the configured delay expires and all guards are still valid.
    /// - Stale or invalid ambush windows are removed without side effects.
    /// </summary>
    private void EvaluatePendingAmbushEscalations(Vector3 partyCenter)
    {
        if (_pendingAmbushTriggerTimes.Count == 0)
        {
            return;
        }

        List<string> encounterIds = _pendingAmbushTriggerTimes.Keys.ToList();
        foreach (string encounterId in encounterIds)
        {
            if (!_pendingAmbushTriggerTimes.TryGetValue(encounterId, out double triggerAt) || _worldClockSeconds < triggerAt)
            {
                continue;
            }

            _pendingAmbushTriggerTimes.Remove(encounterId);

            if (!_encountersById.TryGetValue(encounterId, out TravelActiveEncounter encounter) || !CanEscalateToAmbush(encounter, partyCenter) || !EvaluateAmbushEligibilityGuards(encounter, null, partyCenter))
            {
                continue;
            }

            CreatureStats source = encounter.Members?.FirstOrDefault(member => member != null && GodotObject.IsInstanceValid(member) && !member.IsDead && !IsCreatureInCombat(member));
            TriggerAmbushEscalation(encounter, source, TravelAmbushEscalationReason.DelayedStrike, null);
        }
    }

    private void QueueAmbushEscalation(TravelActiveEncounter encounter, CreatureStats source, TravelAmbushEscalationReason reason, string triggerStimulusId)
    {
        if (encounter == null || string.IsNullOrWhiteSpace(encounter.EncounterId))
        {
            return;
        }

        _pendingAmbushTriggerTimes[encounter.EncounterId] = _worldClockSeconds + _config.AmbushDelaySeconds;

        if (_config.AmbushDelaySeconds <= 0.01f)
        {
            TriggerAmbushEscalation(encounter, source, reason, triggerStimulusId);
        }
    }

    private bool EvaluateAmbushEligibilityGuards(TravelActiveEncounter encounter, TravelSensoryStimulus stimulus, Vector3 partyCenter)
    {
        if (encounter == null)
        {
            return false;
        }

        // If the party already has practical detection on the predator, surprise is blocked.
        if (IsPredatorDetectedFirst(encounter, partyCenter))
        {
            return false;
        }

        // Optional line-of-sight blocker stub: when this says blocked, ambush is prevented.
        if (IsLineOfSightBlockedForAmbush(encounter, partyCenter))
        {
            return false;
        }

        return true;
    }

    private void TriggerAmbushEscalation(TravelActiveEncounter encounter, CreatureStats source, TravelAmbushEscalationReason reason, string triggerStimulusId)
    {
        if (encounter == null || encounter.SurpriseAttackTriggered)
        {
            return;
        }

        encounter.AwarenessState = TravelEncounterAwarenessState.Engaged;
        encounter.IsLockedForCombat = true;
        encounter.SurpriseAttackTriggered = true;

        // Clear active sensory artifacts so the transition has no dangling lure visuals or sounds.
        List<string> encounterStimuli = _activeStimuli.Values
            .Where(stimulus => string.Equals(stimulus.EncounterId, encounter.EncounterId, StringComparison.OrdinalIgnoreCase))
            .Select(stimulus => stimulus.StimulusId)
            .ToList();

        foreach (string stimulusId in encounterStimuli)
        {
            _activeStimuli.Remove(stimulusId);
            if (_spawnedVisualActors.TryGetValue(stimulusId, out Node3D actor) && actor != null && GodotObject.IsInstanceValid(actor))
            {
                actor.QueueFree();
            }

            _spawnedVisualActors.Remove(stimulusId);
        }

        _pendingAmbushTriggerTimes.Remove(encounter.EncounterId);

        AmbushEscalationRequested?.Invoke(new TravelAmbushEscalationRequest
        {
            EncounterId = encounter.EncounterId,
            TriggerCreature = source,
            TriggerReason = reason,
            TriggerStimulusId = triggerStimulusId,
            TimestampSeconds = _worldClockSeconds,
            IsSurpriseAttack = true
        });
    }

    /// <summary>
    /// Ends the sensory stream and clears spawned visual stand-ins.
    /// </summary>
    public void Shutdown()
    {
        foreach (Node3D actor in _spawnedVisualActors.Values)
        {
            if (actor != null && GodotObject.IsInstanceValid(actor))
            {
                actor.QueueFree();
            }
        }

        _encountersById.Clear();
        _activeStimuli.Clear();
        _spawnedVisualActors.Clear();
        _creatureNextMimicAtSeconds.Clear();
        _pendingAmbushTriggerTimes.Clear();

        _snapshot = null;
        _runtime = null;
        _context = null;
    }

    /// <summary>
    /// Stores a stimulus in the active registry and emits one consolidated broadcast event.
    /// </summary>
    private void RegisterAndBroadcast(TravelSensoryStimulus stimulus)
    {
        if (stimulus == null || string.IsNullOrWhiteSpace(stimulus.StimulusId))
        {
            return;
        }

        _activeStimuli[stimulus.StimulusId] = stimulus;
        SensoryStimulusBroadcasted?.Invoke(stimulus);
    }

    private void CreateAuthenticStimulus()
    {
        Vector3 center = ResolveEventCenterNearParty();
        TravelSensoryChannel channel = _rng.Randf() < 0.55f ? TravelSensoryChannel.Vision : TravelSensoryChannel.Sound;

        // Authentic events still happen naturally, but they now respect perception plausibility.
        // This keeps distant biome activity from magically reaching the party across the entire map.
        if (!CanPartyPerceiveStimulus(center, channel, isAuthenticEvent: true))
        {
            return;
        }

        TravelSensoryStimulus stimulus = channel == TravelSensoryChannel.Vision
            ? CreateAuthenticVisionStimulus(center)
            : CreateAuthenticSoundStimulus(center);

        RegisterAndBroadcast(stimulus);
    }

    private void TryCreateMimicStimulus()
    {
        // Each alive encounter contributes only creatures that can plausibly attempt mimicry.
        // This filters out dead members, disengaging groups, combat-locked units, and out-of-range actors.
        List<(TravelActiveEncounter Encounter, CreatureStats Source)> eligibleSources = new();
        foreach (TravelActiveEncounter encounter in _encountersById.Values.Where(e => e != null && e.Members != null))
        {
            foreach (CreatureStats member in encounter.Members.Where(m => m != null && GodotObject.IsInstanceValid(m) && !m.IsDead))
            {
                if (CanCreatureAttemptMimic(encounter, member))
                {
                    eligibleSources.Add((encounter, member));
                }
            }
        }

        if (eligibleSources.Count == 0)
        {
            return;
        }

        (TravelActiveEncounter encounter, CreatureStats source) = eligibleSources[_rng.RandiRange(0, eligibleSources.Count - 1)];
        TravelSensoryStimulus stimulus = BuildMimicStimulus(encounter, source);
        if (stimulus == null)
        {
            return;
        }

        // Even if a creature can attempt mimicry, the party must still be able to perceive the result.
        if (!CanPartyPerceiveStimulus(stimulus.Position, stimulus.Channel, isAuthenticEvent: false))
        {
            return;
        }

        _creatureNextMimicAtSeconds[source.GetInstanceId()] = _worldClockSeconds + Mathf.Max(0.1f, stimulus.CooldownSeconds);

        if (stimulus.Channel == TravelSensoryChannel.Vision)
        {
            SpawnVisualActor(stimulus, "MimicVisual");
        }

        RegisterAndBroadcast(stimulus);
    }

    /// <summary>
    /// Builds a data-driven stimulus from creature components and tactical context.
    ///
    /// Expected output:
    /// - Any creature with an AudibleMimic component can emit deceptive sound pressure.
    /// - Stimulus generation remains generic and does not branch on creature names or biome names.
    /// - Chosen position reflects hazard opportunity through tactical scoring instead of scripted lure themes.
    /// </summary>
    private TravelSensoryStimulus BuildMimicStimulus(TravelActiveEncounter encounter, CreatureStats source)
    {
        if (encounter == null || source?.Template?.StimulusComponents == null)
        {
            return null;
        }

        Vector3 partyCenter = ResolvePartyCenter();
        if (encounter.AwarenessState == TravelEncounterAwarenessState.Unaware || encounter.AwarenessState == TravelEncounterAwarenessState.Engaged)
        {
            return null;
        }

        List<StimulusComponent> supportedStimuli = source.Template.StimulusComponents
            .Where(component => component != null && component.StimulusType == StimulusType.AudibleMimic)
            .ToList();

        if (supportedStimuli.Count == 0)
        {
            return null;
        }

        StimulusComponent selectedComponent = supportedStimuli[_rng.RandiRange(0, supportedStimuli.Count - 1)];
        AICoreArchitectureGuard.ValidateStimulusComponentTags(source.Name, selectedComponent.TacticalTags);

        Vector3 chosenTile = ChooseBestAudibleStimulusTile(source, partyCenter, selectedComponent.Range);
        float tacticalScore = EvaluateStimulusTacticalScore(encounter, source, partyCenter, chosenTile, selectedComponent);
        if (tacticalScore <= 0f)
        {
            return null;
        }

        TravelSensoryStimulus stimulus = new TravelSensoryStimulus
        {
            StimulusId = Guid.NewGuid().ToString("N"),
            SourceKind = TravelSensorySourceKind.MimicCreature,
            Channel = TravelSensoryChannel.Sound,
            EventType = TravelSensoryEventType.AudibleMimic,
            Description = "A believable sound pattern appears at an uncertain distance, encouraging a cautious reaction.",
            Position = chosenTile,
            EncounterId = encounter.EncounterId,
            SourceCreature = source,
            CreatedAtSeconds = _worldClockSeconds,
            ExpiresAtSeconds = _worldClockSeconds + 10d,
            IsAuthentic = false,
            IsScareEvent = false,
            SupportsOptionalInvestigation = true,
            AwarenessModifier = selectedComponent.AwarenessModifier,
            TacticalScore = tacticalScore,
            CooldownSeconds = selectedComponent.CooldownSeconds
        };

        RecordLureStimulus(encounter, stimulus);
        return stimulus;
    }

    /// <summary>
    /// Picks a plausible tile for projected audible mimicry based on tactical opportunity.
    ///
    /// Expected output:
    /// - Candidate positions stay near the source and inside configured projection range.
    /// - Higher-hazard enemy approach lanes naturally rise in priority.
    /// </summary>
    private Vector3 ChooseBestAudibleStimulusTile(CreatureStats source, Vector3 partyCenter, float maxRange)
    {
        GridNode sourceNode = GridManager.Instance?.NodeFromWorldPoint(source.GlobalPosition);
        if (sourceNode == null)
        {
            return source.GlobalPosition;
        }

        List<GridNode> candidates = new();
        candidates.Add(sourceNode);
        candidates.AddRange(GridManager.Instance.GetNeighbours(sourceNode));

        GridNode bestNode = sourceNode;
        float bestScore = float.MinValue;
        foreach (GridNode candidate in candidates.Where(n => n != null))
        {
            float distanceFromSource = candidate.worldPosition.DistanceTo(source.GlobalPosition);
            if (distanceFromSource > maxRange)
            {
                continue;
            }

            float hazard = candidate.TerrainProperties?.LethalityScore ?? 0f;
            float partyProximity = Mathf.Clamp(1f - (candidate.worldPosition.DistanceTo(partyCenter) / Mathf.Max(1f, maxRange)), 0f, 1f);
            float score = (hazard * 2.2f) + partyProximity;
            if (score > bestScore)
            {
                bestScore = score;
                bestNode = candidate;
            }
        }

        return bestNode.worldPosition;
    }

    /// <summary>
    /// Central tactical equation for stimulus actions.
    ///
    /// Expected output:
    /// - Mimicry rises when enemy hazard exposure improves while self hazard stays manageable.
    /// - Awareness advantage and morale pressure influence score without identity-based branches.
    /// </summary>
    private float EvaluateStimulusTacticalScore(TravelActiveEncounter encounter, CreatureStats source, Vector3 partyCenter, Vector3 stimulusPosition, StimulusComponent component)
    {
        GridNode sourceNode = GridManager.Instance?.NodeFromWorldPoint(source.GlobalPosition);
        GridNode stimulusNode = GridManager.Instance?.NodeFromWorldPoint(stimulusPosition);

        float positionalAdvantage = Mathf.Clamp(1f - (source.GlobalPosition.DistanceTo(partyCenter) / Mathf.Max(1f, component.Range)), 0f, 1f);
        float enemyHazardExposure = stimulusNode?.TerrainProperties?.LethalityScore ?? 0f;
        float selfHazardExposure = sourceNode?.TerrainProperties?.LethalityScore ?? 0f;
        float awarenessEdge = Mathf.Clamp(encounter.AwarenessValue / Mathf.Max(1f, _config.AwareThreshold), 0f, 1.5f) * component.AwarenessModifier;
        float moraleImpact = encounter.GroupMoraleProfile == null ? 0f : 0.2f;

        bool lineOfSightSatisfied = !component.RequiresLineOfSight || ResolveAmbushLineOfSightStub(source.GlobalPosition, stimulusPosition);
        float perceptionChance = CanPartyPerceiveStimulus(stimulusPosition, TravelSensoryChannel.Sound, isAuthenticEvent: false) ? 1f : 0f;
        float actionSuccessProbability = (lineOfSightSatisfied ? 1f : 0.2f) * perceptionChance;

        return AIScoringEngine.ScoreTacticalAction(
            positionalAdvantage,
            enemyHazardExposure,
            selfHazardExposure,
            awarenessEdge,
            moraleImpact,
            actionSuccessProbability);
    }

    /// <summary>
    /// Checks whether a creature can plausibly attempt mimic behavior in the travel layer.
    ///
    /// Expected output:
    /// - Returns false when the creature is too far, disengaged, in combat, dead, or still on cooldown.
    /// - Returns true only when tactical mimic output is ecologically believable for travel play.
    /// </summary>
    private bool CanCreatureAttemptMimic(TravelActiveEncounter encounter, CreatureStats creature)
    {
        if (encounter == null || creature == null || !GodotObject.IsInstanceValid(creature) || creature.IsDead)
        {
            return false;
        }

        if (encounter.IsDisengaged || encounter.IsDisengaging || encounter.IsLockedForCombat || encounter.AwarenessState == TravelEncounterAwarenessState.Engaged)
        {
            return false;
        }

        if (encounter.AwarenessState == TravelEncounterAwarenessState.Unaware)
        {
            return false;
        }

        if (IsCreatureInCombat(creature))
        {
            return false;
        }

        bool hasAudibleMimic = creature.Template?.StimulusComponents?.Any(component => component != null && component.StimulusType == StimulusType.AudibleMimic) == true;
        if (!hasAudibleMimic)
        {
            return false;
        }

        Vector3 partyCenter = ResolvePartyCenter();
        if (creature.GlobalPosition.DistanceTo(partyCenter) > _config.MaxMimicAttemptRange)
        {
            return false;
        }

        ulong creatureId = creature.GetInstanceId();
        if (_creatureNextMimicAtSeconds.TryGetValue(creatureId, out double nextAllowedAt) && _worldClockSeconds < nextAllowedAt)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks whether the party can plausibly perceive a stimulus at the provided position.
    ///
    /// Expected output:
    /// - Returns false when a stimulus is beyond hearing/vision distance expectations.
    /// - Returns true when the sensory clue fits believable travel-space perception.
    /// </summary>
    private bool CanPartyPerceiveStimulus(Vector3 position, TravelSensoryChannel channel, bool isAuthenticEvent)
    {
        Vector3 partyCenter = ResolvePartyCenter();
        float distance = position.DistanceTo(partyCenter);

        if (channel == TravelSensoryChannel.Sound)
        {
            bool anyoneCanHear = false;
            var partyMembers = _context?.CreaturePersistence?.PersistentCreatures?.Where(c => c != null && GodotObject.IsInstanceValid(c) && !c.IsDead && (c.IsInGroup("Player") || c.IsInGroup("Ally")));
            if (partyMembers != null)
            {
                foreach (var member in partyMembers)
                {
                    if (!SoundSystem.IsDeafened(member))
                    {
                        anyoneCanHear = true;
                        break;
                    }
                }
            }
            if (!anyoneCanHear) return false;
        }

        float channelRange = channel == TravelSensoryChannel.Sound
            ? _config.MaxSoundPerceptionRange
            : _config.MaxVisionPerceptionRange;

        // Future-ready ecological dampening hook so terrain/weather can later reduce practical range.
        float dampeningMultiplier = ResolveTerrainPerceptionDampening(position, channel);
        float perceivedRange = channelRange * dampeningMultiplier;

        if (isAuthenticEvent)
        {
            perceivedRange = Mathf.Min(perceivedRange, _config.AuthenticEventPerceptionRange);
        }

        return distance <= perceivedRange;
    }

    /// <summary>
    /// Returns a multiplier that can eventually reflect fog, rain, reeds, caves, and other dampening factors.
    ///
    /// Expected output:
    /// - Currently returns 1.0 so existing behavior remains stable.
    /// - Future systems can lower this value to make perception more local and terrain-sensitive.
    /// </summary>
    private float ResolveTerrainPerceptionDampening(Vector3 _position, TravelSensoryChannel _channel)
    {
        return 1f;
    }

    /// <summary>
    /// Determines whether a creature is currently participating in active combat.
    ///
    /// Expected output:
    /// - Returns true for combatants already engaged in the turn manager roster.
    /// - Returns false for creatures still only participating in travel movement.
    /// </summary>
    private static bool IsCreatureInCombat(CreatureStats creature)
    {
        if (creature == null || !GodotObject.IsInstanceValid(creature))
        {
            return false;
        }

        TurnManager turnManager = TurnManager.Instance;
        if (turnManager == null)
        {
            return false;
        }

        return turnManager.GetAllCombatants().Any(c => c == creature);
    }

    private float ResolveStubPartyNoiseLevel()
    {
        List<CreatureStats> partyMembers = _context?.CreaturePersistence?.PersistentCreatures?
            .Where(c => c != null && GodotObject.IsInstanceValid(c) && !c.IsDead && (c.IsInGroup("Player") || c.IsInGroup("Ally")))
            .ToList();

        if (partyMembers == null || partyMembers.Count == 0)
        {
            return 0f;
        }

        int movingMembers = partyMembers.Count(c => c.Velocity.Length() > 0.2f);
        float movementRatio = movingMembers / (float)partyMembers.Count;

        // Lightweight travel-only noise proxy that scales from cautious movement to hurried motion.
        return Mathf.Lerp(0.2f, 1f, movementRatio);
    }

    private void CleanupExpiredStimuli()
    {
        List<string> expired = _activeStimuli.Values
            .Where(s => s.IsResolved || _worldClockSeconds >= s.ExpiresAtSeconds)
            .Select(s => s.StimulusId)
            .ToList();

        foreach (string id in expired)
        {
            _activeStimuli.Remove(id);
            if (_spawnedVisualActors.TryGetValue(id, out Node3D actor) && actor != null && GodotObject.IsInstanceValid(actor))
            {
                actor.QueueFree();
            }

            _spawnedVisualActors.Remove(id);
        }
    }

    private void SpawnVisualActor(TravelSensoryStimulus stimulus, string actorPrefix)
    {
        if (stimulus == null || _runtime?.MapRoot == null || stimulus.Channel != TravelSensoryChannel.Vision)
        {
            return;
        }

        // A lightweight Node3D stand-in keeps this system phase-local and avoids forcing combat actors.
        Node3D standIn = new Node3D
        {
            Name = $"{actorPrefix}_{stimulus.StimulusId[..6]}"
        };

        _runtime.MapRoot.AddChild(standIn);
        standIn.GlobalPosition = stimulus.Position;
        _spawnedVisualActors[stimulus.StimulusId] = standIn;
    }

    private Vector3 ResolveEventCenterNearParty()
    {
        Vector3 origin = ResolvePartyCenter();

        return origin + new Vector3(_rng.RandfRange(-12f, 12f), 0f, _rng.RandfRange(-12f, 12f));
    }

    /// <summary>
    /// Resolves a practical center point for the current traveling party.
    ///
    /// Expected output:
    /// - Uses alive player/allied creatures when available.
    /// - Falls back to travel spawn point when no party actors are discoverable.
    /// </summary>
    private Vector3 ResolvePartyCenter()
    {
        List<CreatureStats> partyMembers = _context?.CreaturePersistence?.PersistentCreatures?
            .Where(c => c != null && GodotObject.IsInstanceValid(c) && !c.IsDead && (c.IsInGroup("Player") || c.IsInGroup("Ally")))
            .ToList();

        if (partyMembers != null && partyMembers.Count > 0)
        {
            Vector3 sum = Vector3.Zero;
            foreach (CreatureStats member in partyMembers)
            {
                sum += member.GlobalPosition;
            }

            return sum / partyMembers.Count;
        }

        return _runtime?.PlayerSpawnPoint ?? Vector3.Zero;
    }

    private static string BuildAuthenticDescription(TravelSensoryEventType type)
    {
        return type switch
        {
            TravelSensoryEventType.GenuineTraveler => "A tired traveler raises an open hand and asks if the road ahead is safe.",
            TravelSensoryEventType.InjuredWanderer => "An injured wanderer leans against a rock and signals for assistance.",
            TravelSensoryEventType.WanderingBeast => "A massive creature crosses the route and continues on without aggression.",
            TravelSensoryEventType.MerchantCaravan => "A merchant caravan moves carefully through the biome, offering cautious trade.",
            TravelSensoryEventType.HunterGroup => "A disciplined hunter group tracks prints and warns you of danger ahead.",
            TravelSensoryEventType.AnimalHerd => "A herd migrates across your path, changing predator pressure behind it.",
            TravelSensoryEventType.AbandonedChest => "A weathered chest lies unattended near a trampled campsite.",
            TravelSensoryEventType.RealPleasForHelp => "You hear a believable cry for help carried by wind and distance.",
            TravelSensoryEventType.RealConversation => "A real conversation drifts through the trees between unseen travelers.",
            TravelSensoryEventType.RealCreatureMovement => "Branches snap in a rhythm that suggests a heavy creature moving away.",
            TravelSensoryEventType.FallingTree => "A tree crashes in the distance, opening a possible path but startling wildlife.",
            TravelSensoryEventType.DistantRoar => "A distant roar rolls over the biome and quiets nearby movement.",
            _ => "Something in the biome catches your senses and invites a choice."
        };
    }

    private static TravelSensoryRewardOpportunity BuildRewardOpportunity(TravelSensoryStimulus stimulus, TravelSensoryInteractionChoice choice, bool succeeded)
    {
        if (choice == TravelSensoryInteractionChoice.Ignore)
        {
            return new TravelSensoryRewardOpportunity
            {
                StimulusId = stimulus.StimulusId,
                RewardProfile = TravelSensoryRewardProfile.SafeLowGain,
                Summary = "You chose caution and moved on safely, but gained little beyond certainty."
            };
        }

        if (!succeeded)
        {
            return new TravelSensoryRewardOpportunity
            {
                StimulusId = stimulus.StimulusId,
                RewardProfile = TravelSensoryRewardProfile.RiskWithoutGain,
                Summary = "The investigation brought risk without immediate reward."
            };
        }

        if (stimulus.IsAuthentic)
        {
            return new TravelSensoryRewardOpportunity
            {
                StimulusId = stimulus.StimulusId,
                RewardProfile = TravelSensoryRewardProfile.AuthenticBenefit,
                Summary = "Authentic encounter resolved: grant XP, materials, morale, information, or recruitment chance."
            };
        }

        return new TravelSensoryRewardOpportunity
        {
            StimulusId = stimulus.StimulusId,
            RewardProfile = TravelSensoryRewardProfile.MimicDefeat,
            Summary = "Mimic threat overcome: grant rare component, predator trophy, or unique trait progress."
        };
    }
}

/// <summary>
/// Normalized sensory payload shared by authentic and mimic events.
/// </summary>
public sealed class TravelSensoryStimulus
{
    public string StimulusId;
    public TravelSensorySourceKind SourceKind;
    public TravelSensoryChannel Channel;
    public TravelSensoryEventType EventType;
    public string Description;

    public Vector3 Position;
    public string EncounterId;
    public CreatureStats SourceCreature;

    public bool IsAuthentic;
    public bool IsScareEvent;
    public bool SupportsOptionalInvestigation;
    public bool IsResolved;
    public float AwarenessModifier = 1f;
    public float TacticalScore;
    public float CooldownSeconds = 8f;

    public double CreatedAtSeconds;
    public double ExpiresAtSeconds;
}

public enum TravelSensorySourceKind
{
    AuthenticBiome,
    MimicCreature
}

public enum TravelSensoryChannel
{
    Vision,
    Sound
}

public enum TravelSensoryEventType
{
    GenuineTraveler,
    InjuredWanderer,
    WanderingBeast,
    MerchantCaravan,
    HunterGroup,
    AnimalHerd,
    AbandonedChest,

    RealPleasForHelp,
    RealConversation,
    RealCreatureMovement,
    FallingTree,
    DistantRoar,

    AudibleMimic,
    ScareBlast,
    FalseConversation,
    FalseVisualLure
}

public enum TravelSensoryInteractionChoice
{
    Ignore,
    Investigate,
    Assist,
    Engage
}

public sealed class TravelSensoryRewardOpportunity
{
    public string StimulusId;
    public TravelSensoryRewardProfile RewardProfile;
    public string Summary;
}

public enum TravelSensoryRewardProfile
{
    SafeLowGain,
    AuthenticBenefit,
    MimicDefeat,
    RiskWithoutGain
}

public sealed class TravelAmbushEscalationRequest
{
    public string EncounterId;
    public CreatureStats TriggerCreature;
    public TravelAmbushEscalationReason TriggerReason;
    public string TriggerStimulusId;
    public double TimestampSeconds;
    public bool IsSurpriseAttack;
}

public enum TravelAmbushEscalationReason
{
    PlayerInvestigatedStimulus,
    AwareProximity,
    DelayedStrike
}

/// <summary>
/// Tunable ecological ranges and cooldowns for travel sensory behavior.
///
/// Expected output:
/// - Designers can quickly tighten or loosen mimic realism and perception spread.
/// - Default values preserve readability while preventing map-wide phantom events.
/// </summary>
public sealed class TravelSensoryConfig
{
    public float SuspiciousThreshold = 5f;
    public float AwareThreshold = 12f;
    public float EngagedThreshold = 20f;
    public float AwarenessDecayRate = 1.8f;

    public float AlertRadius = 24f;
    public float DangerRadius = 12f;
    public float AlertRadiusAwarenessGain = 1.2f;
    public float DangerRadiusAwarenessGain = 3f;
    public float PartyNoiseThreshold = 0.45f;
    public float NoiseAwarenessGainPerSecond = 0.9f;
    public float InvestigationClosingDistanceThreshold = 0.1f;
    public float InvestigationAwarenessGain = 2.2f;
    public float OutOfSightAwarenessMultiplier = 0.45f;
    public float StubLineOfSightRange = 28f;
    public float AwarenessOverflowBuffer = 6f;

    public float IgnoreMimicAwarenessDrop = 1.1f;
    public float InvestigateMimicAwarenessGain = 2.8f;
    public float EngageMimicAwarenessGain = 3.6f;

    public float MaxMimicAttemptRange = 32f;
    public float MaxSoundPerceptionRange = 42f;
    public float MaxVisionPerceptionRange = 34f;
    public float AuthenticEventPerceptionRange = 36f;

    public float AmbushRange = 8f;
    public float AmbushDelaySeconds = 0.65f;
    public float AmbushChanceWhenAware = 0.28f;
    public float PartyPredatorDetectionRange = 6f;

}
