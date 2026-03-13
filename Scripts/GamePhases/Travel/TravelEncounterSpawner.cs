using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Travel-only encounter orchestration system.
///
/// Design intent:
/// - This class decides WHEN and WHERE travel encounters appear.
/// - This class decides WHICH creature templates and archetypes are selected.
/// - This class reports encounter lifecycle events to phase-local overlay systems.
///
/// Boundaries:
/// - It does NOT change arena spawn logic.
/// - It does NOT alter combat resolution internals.
/// - It does NOT duplicate creature template data.
/// - It does NOT inject AI combat decision changes.
/// </summary>
public sealed class TravelEncounterSpawner
{
    // Biome snapshot for the current travel run.
    private TravelBiomeQuerySnapshot _snapshot;

    // Runtime map data for zone coordinates and parent nodes.
    private TravelBiomeMapRuntime _runtime;

    // Shared phase services, including persistent creatures.
    private GamePhaseContext _context;

    // RNG scoped to one travel run for deterministic behavior when biome seed is fixed.
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    // Zone plans built during initialization.
    private readonly List<TravelEncounterZonePlan> _zonePlans = new();

    // Active encounter instances currently in the travel world.
    private readonly List<TravelActiveEncounter> _activeEncounters = new();

    // Runtime-spawned travel enemies so cleanup is explicit and phase-local.
    private readonly List<CreatureStats> _spawnedTravelCreatures = new();

    // Time accumulator used by density-based random spawn checks.
    private double _densityAccumulator;

    // Optional signal for UI or telemetry when an encounter appears.
    public event Action<TravelActiveEncounter> EncounterSpawned;

    // Optional signal when TravelPhase finalizes a retreat/disengagement.
    public event Action<TravelActiveEncounter> EncounterDisengaged;

    /// <summary>
    /// Initializes the travel encounter spawner with biome and map data.
    ///
    /// Expected output:
    /// - Internal zone plans are prepared from procedural encounter zones.
    /// - Ambush-capable zones are tagged by archetype preferences.
    /// - The spawner is ready for per-frame Update calls from TravelPhase.
    /// </summary>
    public void Initialize(TravelBiomeQuerySnapshot snapshot, TravelBiomeMapRuntime runtime, GamePhaseContext context)
    {
        _snapshot = snapshot;
        _runtime = runtime;
        _context = context;
        _densityAccumulator = 0d;

        _zonePlans.Clear();
        _activeEncounters.Clear();
        _spawnedTravelCreatures.Clear();

        if (_snapshot?.Procedural != null && _snapshot.Procedural.Seed != 0)
        {
            _rng.Seed = (ulong)_snapshot.Procedural.Seed + 99u;
        }
        else
        {
            _rng.Randomize();
        }

        BuildZonePlans();
    }

    /// <summary>
    /// Per-frame travel update.
    ///
    /// Expected output:
    /// - Triggered zone encounters can spawn when player is near.
    /// - Density-based random encounter opportunities are evaluated.
    /// - Encounter lifecycle state is exposed for overlay systems such as morale resolvers.
    /// </summary>
    /// <summary>
    /// Returns true when at least one encounter is still active and not disengaged.
    /// </summary>
    public bool HasAnyHostileEncounter()
    {
        return _activeEncounters.Any(e => e != null && !e.IsDisengaged && e.Members != null && e.Members.Any(c => c != null && GodotObject.IsInstanceValid(c) && !c.IsDead));
    }

    public void Update(double deltaSeconds)
    {
        // Random proximity and density spawning has been entirely removed.
        // The Spawner now ONLY reacts when instructed by the StrategicEncounterResolver.
    }

    /// <summary>
    /// Shutdown hook for phase exit.
    ///
    /// Expected output:
    /// - Removes all travel-spawned encounter creatures from scene safely.
    /// - Clears internal state so next travel run starts cleanly.
    /// </summary>
    public void Shutdown()
    {
        foreach (CreatureStats creature in _spawnedTravelCreatures)
        {
            if (!GodotObject.IsInstanceValid(creature))
            {
                continue;
            }

            creature.QueueFree();
        }

        _spawnedTravelCreatures.Clear();
        _zonePlans.Clear();
        _activeEncounters.Clear();

        _snapshot = null;
        _runtime = null;
        _context = null;
    }

    /// <summary>
    /// Builds encounter zone plans from map metadata.
    ///
    /// Expected output:
    /// - Every predefined zone from the map runtime receives a plan entry.
    /// - Each plan stores trigger radius and ambush capability.
    /// - No hardcoded biome or creature IDs are used.
    /// </summary>
    private void BuildZonePlans()
    {
        if (_runtime?.EncounterSpawnZones == null)
        {
            return;
        }

        for (int i = 0; i < _runtime.EncounterSpawnZones.Count; i++)
        {
            Aabb zone = _runtime.EncounterSpawnZones[i];
            Vector3 zoneCenter = zone.Position + (zone.Size * 0.5f);
            float triggerRadius = Mathf.Max(2f, Mathf.Max(zone.Size.X, zone.Size.Z) * 0.6f);

            // Ambush chance is informed by archetype table, making this extensible and data-driven.
            float ambushAffinity = ComputeAmbushAffinity();
            bool isAmbushCapable = _rng.Randf() < ambushAffinity;

            _zonePlans.Add(new TravelEncounterZonePlan
            {
                ZoneIndex = i,
                ZoneBounds = zone,
                ZoneCenter = zoneCenter,
                TriggerRadius = triggerRadius,
                IsAmbushCapable = isAmbushCapable,
                Spawned = false
            });
        }
    }

    /// <summary>
    /// Handles proximity-triggered encounter spawns.
    ///
    /// Expected output:
    /// - Encounter spawns when player enters a zone trigger radius.
    /// - Spawn occurs once per zone plan unless replay is explicitly added later.
    /// </summary>
    private void ProcessTriggerBasedSpawns(Vector3 playerPosition)
    {
        foreach (TravelEncounterZonePlan plan in _zonePlans)
        {
            if (plan.Spawned)
            {
                continue;
            }

            float distanceToPlayer = plan.ZoneCenter.DistanceTo(playerPosition);
            if (distanceToPlayer <= plan.TriggerRadius)
            {
                SpawnEncounterFromPlan(plan, isDensityDriven: false);
            }
        }
    }

    /// <summary>
    /// Handles density-driven random spawning.
    ///
    /// Expected output:
    /// - Uses biome EncounterDensity to control pressure over time.
    /// - Picks among remaining eligible zones so map coordinates remain meaningful.
    /// </summary>
    private void ProcessDensityBasedSpawns(double deltaSeconds, Vector3 playerPosition)
    {
        float density = Mathf.Max(0f, _snapshot.EncounterDensity);
        if (density <= 0f)
        {
            return;
        }

        _densityAccumulator += deltaSeconds;

        // Evaluate once each second to keep logic predictable and easy to tune.
        if (_densityAccumulator < 1d)
        {
            return;
        }

        _densityAccumulator = 0d;

        float spawnChance = Mathf.Clamp(0.15f * density, 0f, 0.9f);
        if (_rng.Randf() > spawnChance)
        {
            return;
        }

        List<TravelEncounterZonePlan> candidates = _zonePlans
            .Where(p => !p.Spawned)
            .OrderBy(p => p.ZoneCenter.DistanceTo(playerPosition))
            .Take(3)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        TravelEncounterZonePlan selected = candidates[_rng.RandiRange(0, candidates.Count - 1)];
        SpawnEncounterFromPlan(selected, isDensityDriven: true);
    }

    /// <summary>
    /// Spawns one encounter group from a zone plan.
    ///
    /// Expected output:
    /// - Group composition comes from ecology-validated biome pool.
    /// - Archetype comes from data-weighted selection.
    /// - Spawn state (hidden/aggression/morale threshold) comes from archetype profile.
    /// </summary>
    private void SpawnEncounterFromPlan(TravelEncounterZonePlan plan, bool isDensityDriven)
    {
        if (plan == null || plan.Spawned)
        {
            return;
        }

        Godot.Collections.Array<CreatureTemplate_SO> pool = _snapshot.EcologyValidatedEncounterPool;
        if (pool == null || pool.Count == 0)
        {
            GD.PrintErr("TravelEncounterSpawner skipped spawn: ecology-validated encounter pool is empty.");
            return;
        }

        TravelEncounterArchetypeDefinition archetype = ChooseArchetype(plan.IsAmbushCapable);
        int groupSize = RollGroupSize();

        var members = new List<CreatureStats>();
        for (int i = 0; i < groupSize; i++)
        {
            CreatureTemplate_SO template = pool[_rng.RandiRange(0, pool.Count - 1)];
            CreatureStats spawned = SpawnOneCreature(template, plan.ZoneCenter, i, archetype);
            if (spawned != null)
            {
                members.Add(spawned);
            }
        }

        if (members.Count == 0)
        {
            return;
        }

        var encounter = new TravelActiveEncounter
        {
            EncounterId = Guid.NewGuid().ToString("N"),
            ZoneIndex = plan.ZoneIndex,
            SpawnCenter = plan.ZoneCenter,
            Archetype = archetype,
            Members = members,
            IsAmbushEncounter = plan.IsAmbushCapable,
            IsDensityDriven = isDensityDriven,
            IsDisengaged = false,
            MoraleRetreatThreshold = Mathf.Clamp(archetype?.MoraleRetreatThreshold ?? 0.3f, 0f, 1f),
            GroupMoraleProfile = archetype?.MoraleProfileOverride,
            SpawnTimestampSeconds = Time.GetTicksMsec() / 1000d
        };

        _activeEncounters.Add(encounter);
        plan.Spawned = true;
        EncounterSpawned?.Invoke(encounter);
    }

    /// <summary>
    /// Spawns one creature from an existing creature template reference.
    ///
    /// Expected output:
    /// - The scene is instantiated from template.CharacterPrefab.
    /// - The instance is marked as TravelEncounter and Enemy for travel orchestration.
    /// - No template data is copied or duplicated.
    /// </summary>
    private CreatureStats SpawnOneCreature(CreatureTemplate_SO template, Vector3 zoneCenter, int index, TravelEncounterArchetypeDefinition archetype)
    {
        if (template?.CharacterPrefab == null || _runtime?.MapRoot == null)
        {
            return null;
        }

        Node3D spawnedNode = template.CharacterPrefab.Instantiate<Node3D>();
        _runtime.MapRoot.AddChild(spawnedNode);

        Vector3 radialOffset = BuildFormationOffset(index);
        spawnedNode.GlobalPosition = zoneCenter + radialOffset;

        CreatureStats stats = spawnedNode as CreatureStats ?? spawnedNode.GetNodeOrNull<CreatureStats>("CreatureStats");
        if (stats == null)
        {
            spawnedNode.QueueFree();
            return null;
        }

        stats.AddToGroup("Enemy");
        stats.AddToGroup("TravelEncounter");

        if (archetype?.StartsHidden == true)
        {
            stats.AddToGroup("TravelHidden");
        }

        switch (archetype?.InitialAggression ?? TravelInitialAggressionState.Hostile)
        {
            case TravelInitialAggressionState.Passive:
                stats.AddToGroup("TravelPassive");
                break;
            case TravelInitialAggressionState.Defensive:
                stats.AddToGroup("TravelDefensive");
                break;
            default:
                stats.AddToGroup("TravelHostile");
                break;
        }

        _spawnedTravelCreatures.Add(stats);
        return stats;
    }

    /// <summary>
    /// Executes a disengagement order created by TravelMoraleResolver.
    ///
    /// Expected output:
    /// - Encounter members are marked as disengaging and removed from enemy pressure.
    /// - Remaining members are moved toward an escape vector before cleanup.
    /// - Combat internals are untouched because this operates entirely as travel orchestration.
    /// </summary>
    public void ExecuteRetreatOrder(TravelRetreatOrder retreatOrder)
    {
        if (retreatOrder?.Encounter == null)
        {
            return;
        }

        TravelActiveEncounter encounter = retreatOrder.Encounter;
        if (!_activeEncounters.Contains(encounter) || encounter.IsDisengaged)
        {
            return;
        }

        encounter.IsDisengaged = true;
        encounter.IsDisengaging = true;
        encounter.LastRetreatReason = retreatOrder.BreakReason;

        foreach (CreatureStats member in encounter.Members)
        {
            if (!GodotObject.IsInstanceValid(member) || member.IsDead)
            {
                continue;
            }

            member.RemoveFromGroup("Enemy");
            member.RemoveFromGroup("TravelEncounter");
            member.RemoveFromGroup("TravelHostile");
            member.RemoveFromGroup("TravelDefensive");
            member.RemoveFromGroup("TravelPassive");
            member.AddToGroup("TravelDisengaged");

            // Retreat direction is authored by the morale resolver and interpreted as a destination hint.
            // We place the creature on that path and then release it from the encounter world.
            member.GlobalPosition = retreatOrder.EscapeDestination;

            // Removing travel encounter actors after issuing retreat keeps combat code unchanged,
            // while still producing visible non-lethal encounter outcomes during TravelPhase.
            member.QueueFree();
        }

        _activeEncounters.Remove(encounter);
        EncounterDisengaged?.Invoke(encounter);
    }

    /// <summary>
    /// Chooses an archetype using weighted biome data.
    ///
    /// Expected output:
    /// - Selection is based on BiomeTravelDefinition archetype weight entries.
    /// - Ambush-capable zones gently boost archetypes that prefer ambush behavior.
    /// </summary>
    private TravelEncounterArchetypeDefinition ChooseArchetype(bool zoneIsAmbushCapable)
    {
        Godot.Collections.Array<TravelEncounterArchetypeWeightEntry> entries = _snapshot.ArchetypeWeightEntries;
        if (entries == null || entries.Count == 0)
        {
            return TravelEncounterArchetypeDefinition.CreateDefaultTerritorial();
        }

        float total = 0f;
        var weighted = new List<(TravelEncounterArchetypeDefinition archetype, float weight)>();

        foreach (TravelEncounterArchetypeWeightEntry entry in entries)
        {
            if (entry?.ArchetypeDefinition == null || entry.Weight <= 0f)
            {
                continue;
            }

            float weight = entry.Weight;
            if (zoneIsAmbushCapable)
            {
                weight *= (1f + Mathf.Clamp(entry.ArchetypeDefinition.AmbushPreference, 0f, 1f));
            }

            weighted.Add((entry.ArchetypeDefinition, weight));
            total += weight;
        }

        if (weighted.Count == 0 || total <= 0f)
        {
            return TravelEncounterArchetypeDefinition.CreateDefaultTerritorial();
        }

        float roll = _rng.Randf() * total;
        float cursor = 0f;

        foreach ((TravelEncounterArchetypeDefinition archetype, float weight) in weighted)
        {
            cursor += weight;
            if (roll <= cursor)
            {
                return archetype;
            }
        }

        return weighted[weighted.Count - 1].archetype;
    }

    /// <summary>
    /// Rolls final group size using group envelope and biome difficulty scaling.
    ///
    /// Expected output:
    /// - Returns at least one member.
    /// - Uses GroupSpawnSettings boundaries from biome data.
    /// - Applies controlled difficulty scaling without hardcoded creature logic.
    /// </summary>
    private int RollGroupSize()
    {
        TravelGroupSpawnSettings settings = _snapshot.GroupSpawns ?? new TravelGroupSpawnSettings();

        int min = Mathf.Max(1, settings.MinGroupSize);
        int max = Mathf.Max(min, settings.MaxGroupSize);

        int rolled = _rng.RandiRange(min, max);

        int biomeDifficulty = Mathf.Max(1, _snapshot.Definition?.BiomeDifficultyRating ?? 1);
        int extraDifficulty = Mathf.Max(0, biomeDifficulty - settings.BaselineBiomeDifficulty);
        int scaledExtra = Mathf.FloorToInt(extraDifficulty * Mathf.Max(0f, settings.DifficultyScalingFactor));

        return Mathf.Clamp(rolled + scaledExtra, min, 20);
    }

    /// <summary>
    /// Returns player's current world position for trigger checks.
    ///
    /// Expected output:
    /// - If player creature exists, returns the player's actual position.
    /// - Otherwise returns runtime player spawn as a safe fallback.
    /// </summary>
    private Vector3 ResolvePlayerPosition()
    {
        CreatureStats player = _context?.CreaturePersistence?.PersistentCreatures?.FirstOrDefault(c => c != null && c.IsInGroup("Player"));
        if (player != null)
        {
            return player.GlobalPosition;
        }

        return _runtime?.PlayerSpawnPoint ?? Vector3.Zero;
    }

    /// <summary>
    /// Average ambush preference used to assign ambush-capable zones.
    ///
    /// Expected output:
    /// - Returns a stable 0..1 value derived from configured archetypes.
    /// </summary>
    private float ComputeAmbushAffinity()
    {
        Godot.Collections.Array<TravelEncounterArchetypeWeightEntry> entries = _snapshot.ArchetypeWeightEntries;
        if (entries == null || entries.Count == 0)
        {
            return 0.35f;
        }

        float weightedTotal = 0f;
        float weightSum = 0f;

        foreach (TravelEncounterArchetypeWeightEntry entry in entries)
        {
            if (entry?.ArchetypeDefinition == null || entry.Weight <= 0f)
            {
                continue;
            }

            weightedTotal += Mathf.Clamp(entry.ArchetypeDefinition.AmbushPreference, 0f, 1f) * entry.Weight;
            weightSum += entry.Weight;
        }

        if (weightSum <= 0f)
        {
            return 0.35f;
        }

        return Mathf.Clamp(weightedTotal / weightSum, 0f, 1f);
    }

    /// <summary>
    /// Creates simple radial formation offsets for group placement.
    ///
    /// Expected output:
    /// - Member 0 spawns near zone center.
    /// - Additional members spread in a readable ring around the center.
    /// </summary>
    private static Vector3 BuildFormationOffset(int index)
    {
        if (index == 0)
        {
            return Vector3.Zero;
        }

        float angle = index * 0.9f;
        float radius = 1.4f + (index * 0.25f);
        return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
    }

    /// <summary>
    /// Spawns the physical 3D representations of Strategic AI entities that caught the player.
    /// </summary>
    public void SpawnFromStrategicEntities(List<StrategicEntity> entities)
    {
        if (entities == null || entities.Count == 0 || _zonePlans.Count == 0) return;

        // Pick a random tactical zone near the player to spawn them into
        TravelEncounterZonePlan targetZone = _zonePlans[_rng.RandiRange(0, _zonePlans.Count - 1)];
        TravelEncounterArchetypeDefinition archetype = ChooseArchetype(targetZone.IsAmbushCapable);
        
        var members = new List<CreatureStats>();
        int index = 0;

         foreach(var strategicEntity in entities)
        {
            if (strategicEntity.CreatureDefinition == null) continue;

            // Spawn the EXACT number of creatures that the Strategic AI tracked in this tribe!
             int groupSize = strategicEntity.GroupSize;

            CreatureStats troopLeader = null;

            for (int i = 0; i < groupSize; i++)
            {
                CreatureStats spawned = SpawnOneCreature(strategicEntity.CreatureDefinition, targetZone.ZoneCenter, index, archetype);
                if (spawned != null) 
                {
                    if (i == 0) 
                    {
                        troopLeader = spawned;
                    }
                    else if (troopLeader != null)
                    {
                        // Pack subordinates into the troop leader!
                        spawned.Visible = false;
                        var col = spawned.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
                        if (col != null) col.Disabled = true;
                        
                        spawned.GetParent().RemoveChild(spawned);
                        troopLeader.AddChild(spawned);
                        spawned.Position = Vector3.Zero;
                    }

                    // 1. Apply Injuries (Silent HP Reduction)
                    if (strategicEntity.InjuryState > 0)
                    {
                        int hpLost = Mathf.FloorToInt(spawned.Template.MaxHP * strategicEntity.InjuryState);
                        if (hpLost > 0) spawned.TakeDamage(hpLost, "Untyped", null, null, null, null, true);
                    }

                    // 2. Apply Hunger Buffs/Debuffs
                    if (strategicEntity.Hunger >= 0.85f)
                    {
                        // Starving: Apply Exhausted condition
                        var exhausted = new StatusEffect_SO { EffectName = "Starving", ConditionApplied = Condition.Exhausted, DurationInRounds = 0 };
                        spawned.MyEffects.AddEffect(exhausted, null);
                        GD.Print($"[Spawn] A starving {spawned.Name} appears!");
                    }
                    else if (strategicEntity.Hunger >= 0.5f)
                    {
                        // Desperate: High Attack/Damage, Low Defense
                        var desperate = new StatusEffect_SO { EffectName = "Desperate Hunger", DurationInRounds = 0 };
                        desperate.Modifications.Add(new StatModification { StatToModify = StatToModify.AttackRoll, ModifierValue = 2, BonusType = BonusType.Morale });
                        desperate.Modifications.Add(new StatModification { StatToModify = StatToModify.MeleeDamage, ModifierValue = 2, BonusType = BonusType.Morale });
                        desperate.Modifications.Add(new StatModification { StatToModify = StatToModify.ArmorClass, ModifierValue = -2, BonusType = BonusType.Untyped });
                        spawned.MyEffects.AddEffect(desperate, null);
                        GD.Print($"[Spawn] A desperate, hungry {spawned.Name} appears!");
                    }

                    members.Add(spawned);
                }
                index++;
            }
        }

        if (members.Count == 0) return;

        var encounter = new TravelActiveEncounter
        {
            EncounterId = Guid.NewGuid().ToString("N"),
            ZoneIndex = targetZone.ZoneIndex,
            SpawnCenter = targetZone.ZoneCenter,
            Archetype = archetype,
            Members = members,
            IsAmbushEncounter = targetZone.IsAmbushCapable,
            IsDensityDriven = false, // It's ecologically driven now!
            IsDisengaged = false,
            MoraleRetreatThreshold = Mathf.Clamp(archetype?.MoraleRetreatThreshold ?? 0.3f, 0f, 1f),
            GroupMoraleProfile = archetype?.MoraleProfileOverride,
            SpawnTimestampSeconds = Time.GetTicksMsec() / 1000d
        };

        _activeEncounters.Add(encounter);
        targetZone.Spawned = true;
        EncounterSpawned?.Invoke(encounter);
    }
}

/// <summary>
/// Internal zone plan data used by TravelEncounterSpawner.
/// </summary>
public sealed class TravelEncounterZonePlan
{
    public int ZoneIndex;
    public Aabb ZoneBounds;
    public Vector3 ZoneCenter;
    public float TriggerRadius;
    public bool IsAmbushCapable;
    public bool Spawned;
}

/// <summary>
/// Runtime encounter descriptor produced by TravelEncounterSpawner.
/// TravelPhase can inspect this for UI, objectives, and disengagement outcomes.
/// </summary>
public sealed class TravelActiveEncounter
{
    public string EncounterId;
    public int ZoneIndex;
    public Vector3 SpawnCenter;
    public TravelEncounterArchetypeDefinition Archetype;
    public List<CreatureStats> Members = new();
    public float MoraleRetreatThreshold;
    public bool IsAmbushEncounter;
    public bool IsDensityDriven;
    public bool IsDisengaged;
    public bool IsDisengaging;
    public TravelEncounterAwarenessState AwarenessState = TravelEncounterAwarenessState.Unaware;
    public float AwarenessValue;
    public double LastAwarenessUpdateTime;
    public Vector3 LastKnownPartyPosition = new Vector3(float.NaN, float.NaN, float.NaN);
    public bool HasLineOfSightToParty;
    public float AwarenessDecayTimer;
    public double SpawnTimestampSeconds;
    public TravelMoraleProfile GroupMoraleProfile;
    public TravelMoraleBreakReason LastRetreatReason;
    public bool IsLockedForCombat;
    public bool SurpriseAttackTriggered;

    // Timestamp and position memory used by generic stimulus-to-ambush timing memory.
    public double LastLureStimulusAtSeconds;
    public Vector3 LastLureStimulusPosition = new Vector3(float.NaN, float.NaN, float.NaN);

    /// <summary>
    /// Applies travel-only ecological awareness progression for this encounter.
    ///
    /// Expected output:
    /// - Awareness grows when the party is close, loud, or moving in a way that appears investigative.
    /// - Awareness softens over time when pressure drops so creatures can calm back down naturally.
    /// - State transitions remain lightweight and never start combat directly.
    /// </summary>
    public void UpdateAwareness(float delta, Vector3 partyCenter, float partyNoiseLevel, TravelSensoryConfig config, double worldTimeSeconds, IntelligenceSystemManager intelligenceManager, CreatureStats playerLeader)
    {
        if (config == null)
        {
            return;
        }

        delta = Mathf.Max(delta, 0f);
        float awarenessBefore = AwarenessValue;
        TravelEncounterAwarenessState stateBefore = AwarenessState;

        float distance = SpawnCenter.DistanceTo(partyCenter);
        float awarenessDelta = 0f;

        // Predators are most reactive when the party is inside their danger ring.
        if (distance <= config.DangerRadius)
        {
            awarenessDelta += config.DangerRadiusAwarenessGain * delta;
        }
        else if (distance <= config.AlertRadius)
        {
            awarenessDelta += config.AlertRadiusAwarenessGain * delta;
        }

        // Loud travel choices create ecological pressure even without line-of-sight certainty.
        if (partyNoiseLevel > config.PartyNoiseThreshold)
        {
            awarenessDelta += config.NoiseAwarenessGainPerSecond * delta;
        }

        // Moving toward where the encounter expects the party to be can feel like active investigation.
        if (!float.IsNaN(LastKnownPartyPosition.X))
        {
            float previousDistance = SpawnCenter.DistanceTo(LastKnownPartyPosition);
            float closingDistance = previousDistance - distance;
            if (closingDistance > config.InvestigationClosingDistanceThreshold)
            {
                awarenessDelta += config.InvestigationAwarenessGain * delta;
            }
        }

        // Intelligence-aware sensitivity keeps detection and suspicion growth tied to cognition rather than pure distance/noise.
        int threatInt = ResolveThreatIntelligence();
        if (intelligenceManager != null && playerLeader != null)
        {
            float detectionModifier = intelligenceManager.ComputeDetectionModifier(playerLeader, threatInt);
            awarenessDelta *= Mathf.Clamp(1f - detectionModifier, 0.35f, 1.65f);

            float escalationModifier = intelligenceManager.ComputeEscalationModifier(playerLeader, threatInt);
            awarenessDelta *= Mathf.Clamp(1f + escalationModifier, 0.35f, 1.8f);
        }

        // LOS remains lightweight for travel but now traverses tiles instead of pure distance-only checks.
        HasLineOfSightToParty = ResolveLineOfSightStub(SpawnCenter, partyCenter, distance, config);
        if (!HasLineOfSightToParty)
        {
            awarenessDelta *= config.OutOfSightAwarenessMultiplier;
        }

        bool isBuildingPressure = awarenessDelta > 0f;
        if (isBuildingPressure)
        {
            AwarenessDecayTimer = 0f;
            AwarenessValue += awarenessDelta;
        }
        else
        {
            float investigationDelayMultiplier = (intelligenceManager != null && playerLeader != null)
                ? intelligenceManager.ComputeInvestigationDelayMultiplier(playerLeader)
                : 1f;
            AwarenessDecayTimer += delta * investigationDelayMultiplier;
            float decay = config.AwarenessDecayRate * delta;
            AwarenessValue = Mathf.Max(0f, AwarenessValue - decay);

            if (decay > 0f)
            {
                GD.Print($"[TravelAwareness] Decay active for {EncounterId}: -{decay:0.00} (timer={AwarenessDecayTimer:0.00}s)");
            }
        }

        AwarenessValue = Mathf.Clamp(AwarenessValue, 0f, config.EngagedThreshold + config.AwarenessOverflowBuffer);
        AwarenessState = ResolveAwarenessStateFromValue(AwarenessValue, config);

        if (!Mathf.IsEqualApprox(awarenessBefore, AwarenessValue))
        {
            GD.Print($"[TravelAwareness] {EncounterId}: {awarenessBefore:0.00} -> {AwarenessValue:0.00} (distance={distance:0.00}, noise={partyNoiseLevel:0.00}, los={HasLineOfSightToParty})");
        }

        if (stateBefore != AwarenessState)
        {
            GD.Print($"[TravelAwareness] {EncounterId} transitioned {stateBefore} -> {AwarenessState}");
        }

        LastAwarenessUpdateTime = worldTimeSeconds;
        LastKnownPartyPosition = partyCenter;
    }

    /// <summary>
    /// Applies lightweight feedback after the party reacts to a sensory bait signal.
    ///
    /// Expected output:
    /// - Ignoring mimic bait makes uncertain predators second-guess and calm slightly.
    /// - Investigation or direct engagement increases certainty and raises pressure.
    /// - State downgrades and upgrades are handled by shared threshold rules.
    /// </summary>
    public void ApplyStimulusFeedback(TravelSensoryInteractionChoice choice, bool succeeded, TravelSensoryConfig config, float awarenessModifier = 1f)
    {
        if (config == null)
        {
            return;
        }

        float awarenessBefore = AwarenessValue;
        TravelEncounterAwarenessState stateBefore = AwarenessState;

        if (choice == TravelSensoryInteractionChoice.Ignore)
        {
            AwarenessValue = Mathf.Max(0f, AwarenessValue - (config.IgnoreMimicAwarenessDrop * Mathf.Max(0.1f, awarenessModifier)));
        }
        else if (choice == TravelSensoryInteractionChoice.Investigate || choice == TravelSensoryInteractionChoice.Assist)
        {
            AwarenessValue += config.InvestigateMimicAwarenessGain * Mathf.Max(0.1f, awarenessModifier);
        }
        else if (choice == TravelSensoryInteractionChoice.Engage && succeeded)
        {
            AwarenessValue += config.EngageMimicAwarenessGain * Mathf.Max(0.1f, awarenessModifier);
        }

        AwarenessState = ResolveAwarenessStateFromValue(AwarenessValue, config);

        if (!Mathf.IsEqualApprox(awarenessBefore, AwarenessValue))
        {
            GD.Print($"[TravelAwareness] Stimulus feedback for {EncounterId}: {choice} changed awareness {awarenessBefore:0.00} -> {AwarenessValue:0.00}");
        }

        if (stateBefore != AwarenessState)
        {
            GD.Print($"[TravelAwareness] {EncounterId} transitioned {stateBefore} -> {AwarenessState} via stimulus feedback");
        }
    }

    /// <summary>
    /// Checks whether this encounter can escalate from travel tension into an ambush handoff.
    ///
    /// Expected output:
    /// - Returns true only when awareness is already high and the predator is close enough to strike.
    /// - Returns false when combat is already active, retreat has started, or travel has stopped being the active space.
    /// </summary>
    public bool CanEscalateToAmbush(TravelActiveEncounter encounter, Vector3 partyCenter, TravelSensoryConfig config, bool isTravelPhaseActive, Func<CreatureStats, bool> isCreatureInCombat)
    {
        if (encounter == null || config == null || !isTravelPhaseActive)
        {
            return false;
        }

        if (encounter.AwarenessState != TravelEncounterAwarenessState.Aware)
        {
            return false;
        }

        if (encounter.IsDisengaged || encounter.IsDisengaging || encounter.IsLockedForCombat || encounter.SurpriseAttackTriggered)
        {
            return false;
        }

        if (encounter.Members == null || encounter.Members.Count == 0)
        {
            return false;
        }

        bool anyAvailablePredator = encounter.Members.Any(member =>
            member != null &&
            GodotObject.IsInstanceValid(member) &&
            !member.IsDead &&
            (isCreatureInCombat == null || !isCreatureInCombat(member)));

        if (!anyAvailablePredator)
        {
            return false;
        }

        float distanceToParty = encounter.SpawnCenter.DistanceTo(partyCenter);
        return distanceToParty < config.AmbushRange;
    }

    private static bool ResolveLineOfSightStub(Vector3 observerPosition, Vector3 targetPosition, float distance, TravelSensoryConfig config)
    {
        if (distance > config.StubLineOfSightRange)
        {
            return false;
        }

        if (GridManager.Instance == null)
        {
            return true;
        }

        return HasTileTraversalLineOfSight(observerPosition, targetPosition);
    }

    /// <summary>
    /// Lightweight tile traversal helper for travel awareness LoS.
    /// This intentionally avoids raycasting and uses existing grid node metadata.
    /// </summary>
    private static bool HasTileTraversalLineOfSight(Vector3 observerPosition, Vector3 targetPosition)
    {
        float distance = observerPosition.DistanceTo(targetPosition);
        float step = Mathf.Max(GridManager.Instance.nodeDiameter, 1f);
        int steps = Mathf.Clamp(Mathf.CeilToInt(distance / step), 1, 96);

        int denseCoverCount = 0;
        for (int i = 1; i < steps; i++)
        {
            float t = i / (float)steps;
            Vector3 sample = observerPosition.Lerp(targetPosition, t);
            GridNode node = GridManager.Instance.NodeFromWorldPoint(sample);
            if (node == null)
            {
                continue;
            }

            if (node.blocksLos || node.terrainType == TerrainType.Solid)
            {
                return false;
            }

            if (node.providesCover || node.environmentalTags.Contains("Fog") || node.environmentalTags.Contains("Smoke"))
            {
                denseCoverCount++;
            }
        }

        // If most sampled tiles are heavy concealment/cover, treat LoS as lost for travel-level awareness.
        return denseCoverCount < Mathf.Max(2, steps / 2);
    }


    private int ResolveThreatIntelligence()
    {
        if (Members == null || Members.Count == 0)
        {
            return 0;
        }

        int total = 0;
        int count = 0;
        foreach (CreatureStats member in Members)
        {
            if (member?.Template == null)
            {
                continue;
            }

            total += member.Template.Intelligence;
            count++;
        }

        return count <= 0 ? 0 : Mathf.RoundToInt((float)total / count);
    }

    private static TravelEncounterAwarenessState ResolveAwarenessStateFromValue(float awarenessValue, TravelSensoryConfig config)
    {
        if (awarenessValue >= config.EngagedThreshold)
        {
            return TravelEncounterAwarenessState.Engaged;
        }

        if (awarenessValue >= config.AwareThreshold)
        {
            return TravelEncounterAwarenessState.Aware;
        }

        if (awarenessValue >= config.SuspiciousThreshold)
        {
            return TravelEncounterAwarenessState.Suspicious;
        }

        return TravelEncounterAwarenessState.Unaware;
    }
}

/// <summary>
/// Lightweight awareness level used by the travel sensory layer.
///
/// Expected output:
/// - Unaware groups stay quiet and do not perform lure-style mimicry.
/// - Suspicious groups begin probing behavior with cautious signals.
/// - Aware groups can perform tactical mimic lures and pressure sounds.
/// - Engaged groups focus on immediate conflict and stop mimic broadcasts.
/// </summary>
public enum TravelEncounterAwarenessState
{
    Unaware,
    Suspicious,
    Aware,
    Engaged
}
