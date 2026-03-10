using Godot;

/// <summary>
/// Shared data passed into every phase method.
///
/// This avoids hard-wiring phases to one exact scene setup.
/// </summary>
public partial class ArenaStartContext : RefCounted
{
    /// <summary>
    /// Build a context object used during phase calls.
    /// </summary>
    public GamePhaseContext(Node owner, Node3D phaseRoot, CreaturePersistenceService creaturePersistence, RecruitmentManager recruitment, PartyRosterManager partyRoster, IntelligenceSystemManager intelligence)
    {
        Owner = owner;
        PhaseRoot = phaseRoot;
        CreaturePersistence = creaturePersistence;
        Recruitment = recruitment;
        PartyRoster = partyRoster;
        Intelligence = intelligence;
    }

    /// <summary>
    /// The node that owns and runs phase switching.
    /// </summary>
    public Node Owner { get; }

    /// <summary>
    /// Where phase-specific runtime objects should be added.
    /// </summary>
    public Node3D PhaseRoot { get; }

    /// <summary>
    /// Service that keeps creature instances alive between phases.
    /// </summary>
    public CreaturePersistenceService CreaturePersistence { get; }

    /// <summary>
    /// Shared lightweight recruitment + intelligence service.
    ///
    /// Expected output:
    /// - Travel, arena awareness, and simulation systems can read role synergy from one source.
    /// - No full CreatureStats cloning is required for persistence.
    /// </summary>
    public RecruitmentManager Recruitment { get; }

    /// <summary>
    /// Full entity-level ally roster used for recruitment-driven progression.
    /// </summary>
    public PartyRosterManager PartyRoster { get; }

    /// <summary>
    /// Unified intelligence system shared across Arena and Travel phases.
    /// </summary>
    public IntelligenceSystemManager Intelligence { get; }

    /// <summary>
    /// Optional handoff payload populated by travel systems before returning to Arena.
    ///
    /// Expected output:
    /// - Arena entry systems can inspect whether the next transition was a surprise attack.
    /// - Data is consumed once and then cleared so stale travel flags never leak into later transitions.
    /// </summary>
    public ArenaStartContext PendingArenaStartContext { get; private set; }

    /// <summary>
    /// Stores the next Arena start payload.
    /// </summary>
    public void SetArenaStartContext(ArenaStartContext context)
    {
        PendingArenaStartContext = context;
    }

    /// <summary>
    /// Returns and clears the pending Arena start payload.
    /// </summary>
    public ArenaStartContext ConsumeArenaStartContext()
    {
        ArenaStartContext result = PendingArenaStartContext;
        PendingArenaStartContext = null;
        return result;
    }
}

/// <summary>
/// Lightweight handshake payload passed from TravelPhase into Arena phase startup.
///
/// Expected output:
/// - Arena systems can apply surprise-round mechanics without travel owning combat rules.
/// - The source creature can be identified for telemetry, narration, and future counter-ambush extensions.
/// </summary>
public partial class ArenaStartContext : RefCounted
{
    public bool IsSurpriseAttack;
    public ulong SurpriseSourceCreatureId;

    /// <summary>
    /// True when this arena entry came from Travel escalation.
    /// Expected output: combat map sizing applies the Travel max clamp (1,200 ft) while still using unified formula math.
    /// </summary>
    public bool IsTravelCombat;
}
