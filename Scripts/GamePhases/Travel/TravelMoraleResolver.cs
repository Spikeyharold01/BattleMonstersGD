using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Travel-only morale overlay that watches encounter combat state from the outside.
///
/// Design intent:
/// - This class exists only for TravelPhase ecology encounters.
/// - It never alters attack rolls, damage calculations, or combat turn resolution.
/// - It emits retreat orders that TravelPhase can choose to execute.
/// </summary>
public sealed class TravelMoraleResolver
{
    private TravelBiomeQuerySnapshot _snapshot;
    private TravelBiomeMapRuntime _runtime;
    private GamePhaseContext _context;

    // Runtime tracking state for every active travel encounter.
    private readonly Dictionary<string, TrackedEncounterMoraleState> _tracked = new();

    /// <summary>
    /// Raised when morale breaks and TravelPhase should issue a disengagement action.
    /// </summary>
    public event Action<TravelRetreatOrder> RetreatOrdered;

    /// <summary>
    /// Starts the resolver for one travel run.
    ///
    /// Expected output:
    /// - Internal encounter state is reset.
    /// - Resolver is ready to track spawned travel encounters.
    /// </summary>
    public void Initialize(TravelBiomeQuerySnapshot snapshot, TravelBiomeMapRuntime runtime, GamePhaseContext context)
    {
        _snapshot = snapshot;
        _runtime = runtime;
        _context = context;
        _tracked.Clear();
    }

    /// <summary>
    /// Registers a travel encounter so morale can be evaluated during updates.
    ///
    /// Expected output:
    /// - Encounter receives a resolved morale profile.
    /// - Leader reference and initial headcount are captured for later trigger checks.
    /// </summary>
    public void RegisterEncounter(TravelActiveEncounter encounter)
    {
        if (encounter == null || string.IsNullOrWhiteSpace(encounter.EncounterId))
        {
            return;
        }

        if (_tracked.ContainsKey(encounter.EncounterId))
        {
            return;
        }

        TravelMoraleProfile profile = ResolveMoraleProfile(encounter);
        CreatureStats leader = ResolveLeader(encounter, profile);

        _tracked[encounter.EncounterId] = new TrackedEncounterMoraleState
        {
            Encounter = encounter,
            Profile = profile,
            InitialMemberCount = Math.Max(1, encounter.Members.Count),
            Leader = leader,
            TimeInCombatSeconds = 0d
        };
    }

    /// <summary>
    /// Stops tracking an encounter after disengagement or cleanup.
    /// </summary>
    public void UnregisterEncounter(string encounterId)
    {
        if (string.IsNullOrWhiteSpace(encounterId))
        {
            return;
        }

        _tracked.Remove(encounterId);
    }

    /// <summary>
    /// Per-frame morale evaluation pass.
    ///
    /// Expected output:
    /// - Combat pressure metrics are sampled externally.
    /// - Retreat orders are emitted only when configured thresholds are crossed.
    /// </summary>
    public void Update(double deltaSeconds)
    {
        if (_snapshot == null || _runtime == null || _context == null)
        {
            return;
        }

        foreach (TrackedEncounterMoraleState tracked in _tracked.Values.ToList())
        {
            if (tracked.Encounter == null || tracked.Encounter.IsDisengaged || tracked.Encounter.IsDisengaging)
            {
                continue;
            }

            if (!HasLivingMembers(tracked.Encounter))
            {
                continue;
            }

            bool currentlyInCombat = IsEncounterInCombatWithParty(tracked.Encounter);
            if (currentlyInCombat)
            {
                tracked.TimeInCombatSeconds += deltaSeconds;
            }

            TravelMoraleBreakReason reason = EvaluateBreakReason(tracked);
            if (reason == TravelMoraleBreakReason.None)
            {
                continue;
            }

            var retreatOrder = new TravelRetreatOrder
            {
                Encounter = tracked.Encounter,
                BreakReason = reason,
                EscapeDestination = BuildEscapeDestination(tracked.Encounter),
                GrantXp = tracked.Profile.GrantXpOnDisengage
            };

            tracked.Encounter.IsDisengaging = true;
            RetreatOrdered?.Invoke(retreatOrder);
        }
    }

    /// <summary>
    /// Stops the resolver and clears all tracked encounter state.
    /// </summary>
    public void Shutdown()
    {
        _tracked.Clear();
        _snapshot = null;
        _runtime = null;
        _context = null;
    }

    private TravelMoraleBreakReason EvaluateBreakReason(TrackedEncounterMoraleState tracked)
    {
        float healthRatio = ComputeHealthRatio(tracked.Encounter);
        float casualtyRatio = ComputeCasualtyRatio(tracked.Encounter, tracked.InitialMemberCount);
        bool leaderLost = tracked.Profile.LeaderDeathTrigger && (tracked.Leader == null || !GodotObject.IsInstanceValid(tracked.Leader) || tracked.Leader.IsDead);
        float relativeForceRatio = ComputeRelativeForceRatio(tracked.Encounter);

        float healthThreshold = Mathf.Clamp(tracked.Profile.MoraleThreshold / Mathf.Max(0.25f, tracked.Profile.ArchetypeModifier), 0f, 1f);
        float casualtyThreshold = Mathf.Clamp(tracked.Profile.CasualtyRatioThreshold * tracked.Profile.ArchetypeModifier, 0f, 1f);
        float forceThreshold = Mathf.Max(0f, tracked.Profile.RelativeForceDisadvantageThreshold * tracked.Profile.ArchetypeModifier);
        float timeThreshold = Mathf.Max(0f, tracked.Profile.TimeInCombatThreshold * tracked.Profile.ArchetypeModifier);

        if (healthThreshold > 0f && healthRatio <= healthThreshold)
        {
            return TravelMoraleBreakReason.HealthThreshold;
        }

        if (casualtyThreshold > 0f && casualtyRatio >= casualtyThreshold)
        {
            return TravelMoraleBreakReason.CasualtyRatio;
        }

        if (leaderLost)
        {
            return TravelMoraleBreakReason.LeaderDown;
        }

        if (timeThreshold > 0f && tracked.TimeInCombatSeconds >= timeThreshold)
        {
            return TravelMoraleBreakReason.CombatDuration;
        }

        if (forceThreshold > 0f && relativeForceRatio >= forceThreshold)
        {
            return TravelMoraleBreakReason.RelativeForceDisadvantage;
        }

        return TravelMoraleBreakReason.None;
    }

    private TravelMoraleProfile ResolveMoraleProfile(TravelActiveEncounter encounter)
    {
        if (encounter?.GroupMoraleProfile != null)
        {
            return encounter.GroupMoraleProfile;
        }

        Godot.Collections.Array<TravelArchetypeMoraleModifier> modifiers = _snapshot?.ArchetypeMoraleModifiers;
        if (modifiers != null && encounter?.Archetype != null)
        {
            foreach (TravelArchetypeMoraleModifier entry in modifiers)
            {
                if (entry?.ProfileOverride == null)
                {
                    continue;
                }

                if (string.Equals(entry.ArchetypeId, encounter.Archetype.ArchetypeId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.ProfileOverride;
                }
            }
        }

        if (_snapshot?.DefaultMorale != null)
        {
            return _snapshot.DefaultMorale;
        }

        return new TravelMoraleProfile
        {
            MoraleThreshold = Mathf.Clamp(encounter?.MoraleRetreatThreshold ?? 0.3f, 0f, 1f),
            CasualtyRatioThreshold = 0.5f,
            LeaderDeathTrigger = true,
            LeaderMemberIndex = 0,
            TimeInCombatThreshold = 45f,
            RelativeForceDisadvantageThreshold = 1.5f,
            ArchetypeModifier = 1f,
            GrantXpOnDisengage = false
        };
    }

    private static CreatureStats ResolveLeader(TravelActiveEncounter encounter, TravelMoraleProfile profile)
    {
        if (encounter?.Members == null || encounter.Members.Count == 0)
        {
            return null;
        }

        int index = Mathf.Clamp(profile?.LeaderMemberIndex ?? 0, 0, encounter.Members.Count - 1);
        return encounter.Members[index];
    }

    private bool IsEncounterInCombatWithParty(TravelActiveEncounter encounter)
    {
        TurnManager turnManager = TurnManager.Instance;
        if (turnManager == null)
        {
            return false;
        }

        List<CreatureStats> activeCombatants = turnManager.GetAllCombatants()
            .Where(c =>
                c != null &&
                GodotObject.IsInstanceValid(c) &&
                c.Template != null &&
                !c.IsDead &&
                c.CurrentHP > -c.Template.Constitution)
            .ToList();

        bool anyParty = activeCombatants.Any(c => c.IsInGroup("Player") || c.IsInGroup("Ally"));
        if (!anyParty)
        {
            return false;
        }

        HashSet<CreatureStats> members = encounter.Members.Where(m => m != null).ToHashSet();
        return activeCombatants.Any(c => members.Contains(c));
    }

    private Vector3 BuildEscapeDestination(TravelActiveEncounter encounter)
    {
        Vector3 playerPosition = ResolvePlayerPosition();
        Vector3 fromCenter = encounter?.SpawnCenter ?? playerPosition;

        Vector3 away = fromCenter - playerPosition;
        if (away.LengthSquared() < 0.0001f)
        {
            away = new Vector3(1f, 0f, 0f);
        }

        Vector3 direction = away.Normalized();
        Vector3 destination = fromCenter + (direction * 10f);

        Aabb mapBounds = BuildMapBounds();
        float x = Mathf.Clamp(destination.X, mapBounds.Position.X, mapBounds.Position.X + mapBounds.Size.X);
        float y = destination.Y;
        float z = Mathf.Clamp(destination.Z, mapBounds.Position.Z, mapBounds.Position.Z + mapBounds.Size.Z);
        return new Vector3(x, y, z);
    }

    private Aabb BuildMapBounds()
    {
        if (_runtime?.EncounterSpawnZones == null || _runtime.EncounterSpawnZones.Count == 0)
        {
            return new Aabb(Vector3.Zero, new Vector3(100f, 10f, 100f));
        }

        Aabb bounds = _runtime.EncounterSpawnZones[0];
        for (int i = 1; i < _runtime.EncounterSpawnZones.Count; i++)
        {
            bounds = bounds.Merge(_runtime.EncounterSpawnZones[i]);
        }

        return bounds;
    }

    private Vector3 ResolvePlayerPosition()
    {
        CreatureStats player = _context?.CreaturePersistence?.PersistentCreatures?.FirstOrDefault(c => c != null && c.IsInGroup("Player"));
        if (player != null)
        {
            return player.GlobalPosition;
        }

        return _runtime?.PlayerSpawnPoint ?? Vector3.Zero;
    }

    private static bool HasLivingMembers(TravelActiveEncounter encounter)
    {
        return encounter?.Members?.Any(m => m != null && GodotObject.IsInstanceValid(m) && !m.IsDead) == true;
    }

    private static float ComputeHealthRatio(TravelActiveEncounter encounter)
    {
        float current = 0f;
        float maximum = 0f;

        foreach (CreatureStats member in encounter.Members)
        {
            if (!GodotObject.IsInstanceValid(member) || member.Template == null)
            {
                continue;
            }

            maximum += Mathf.Max(1f, member.Template.MaxHP);
            current += Mathf.Clamp(member.CurrentHP, 0, member.Template.MaxHP);
        }

        if (maximum <= 0f)
        {
            return 0f;
        }

        return current / maximum;
    }

    private static float ComputeCasualtyRatio(TravelActiveEncounter encounter, int initialCount)
    {
        int alive = 0;
        foreach (CreatureStats member in encounter.Members)
        {
            if (member != null && GodotObject.IsInstanceValid(member) && !member.IsDead)
            {
                alive++;
            }
        }

        int down = Mathf.Max(0, initialCount - alive);
        return initialCount <= 0 ? 0f : (float)down / initialCount;
    }

    private float ComputeRelativeForceRatio(TravelActiveEncounter encounter)
    {
        float enemyPower = 0f;
        foreach (CreatureStats member in encounter.Members)
        {
            if (member == null || !GodotObject.IsInstanceValid(member) || member.IsDead || member.Template == null)
            {
                continue;
            }

            enemyPower += Mathf.Max(1f, member.CurrentHP);
        }

        float partyPower = 0f;
        IEnumerable<CreatureStats> party = _context?.CreaturePersistence?.PersistentCreatures ?? Enumerable.Empty<CreatureStats>();
        foreach (CreatureStats member in party)
        {
            if (member == null || !GodotObject.IsInstanceValid(member) || member.IsDead || member.Template == null)
            {
                continue;
            }

            if (!member.IsInGroup("Player") && !member.IsInGroup("Ally"))
            {
                continue;
            }

            partyPower += Mathf.Max(1f, member.CurrentHP);
        }

        if (enemyPower <= 0f)
        {
            return float.PositiveInfinity;
        }

        return partyPower / enemyPower;
    }
}

/// <summary>
/// Travel disengagement command produced by TravelMoraleResolver and executed by TravelPhase orchestration.
/// </summary>
public sealed class TravelRetreatOrder
{
    public TravelActiveEncounter Encounter;
    public TravelMoraleBreakReason BreakReason;
    public Vector3 EscapeDestination;
    public bool GrantXp;
}

/// <summary>
/// Human-readable morale break causes used for telemetry, quest hooks, or UI feedback.
/// </summary>
public enum TravelMoraleBreakReason
{
    None,
    HealthThreshold,
    CasualtyRatio,
    LeaderDown,
    CombatDuration,
    RelativeForceDisadvantage
}

internal sealed class TrackedEncounterMoraleState
{
    public TravelActiveEncounter Encounter;
    public TravelMoraleProfile Profile;
    public int InitialMemberCount;
    public CreatureStats Leader;
    public double TimeInCombatSeconds;
}
