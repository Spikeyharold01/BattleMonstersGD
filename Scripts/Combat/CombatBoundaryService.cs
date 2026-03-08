using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Central boundary policy service for combat movement and escape handling.
///
/// Expected output:
/// - Arena fights become fully sealed with no silent clamping and no implicit exits.
/// - Travel fights can intentionally break out of combat bounds and continue at the higher travel layer.
/// - AI and movement systems can ask one source of truth for boundary semantics.
/// </summary>
public static class CombatBoundaryService
{
    /// <summary>
    /// Describes how close the player is to the current engagement.
    ///
    /// Expected output:
    /// - PlayerInvolved means someone on the player side is directly in the fight.
    /// - PlayerAdjacentSight means the player is not a combatant but can still witness the clash nearby.
    /// - OffscreenAIOnly means the entire event can be resolved abstractly without tactical chase simulation.
    /// </summary>
    public enum EngagementContext
    {
        PlayerInvolved,
        PlayerAdjacentSight,
        OffscreenAIOnly
    }

    private static readonly Dictionary<CreatureStats, int> _edgePressureRounds = new Dictionary<CreatureStats, int>();
    private static readonly Dictionary<CreatureStats, int> _lastKnownHp = new Dictionary<CreatureStats, int>();
    private const float AdjacentSightRangeFeet = 240f;

    public static CombatBoundaryMode CurrentMode { get; private set; } = CombatBoundaryMode.ArenaLocked;
    public static EngagementContext CurrentEngagementContext { get; private set; } = EngagementContext.PlayerInvolved;

    /// <summary>
    /// Soft inward-bias multiplier raised when edge-loop behavior is detected.
    ///
    /// Expected output:
    /// - Starts at 1.0 and rises gently only when repeated non-damaging edge loops are observed.
    /// - Never opens arena exits and never changes map size formulas.
    /// </summary>
    public static float InwardBiasMultiplier { get; private set; } = 1.0f;

    public static event Action<CreatureStats> CombatBoundaryCrossed;

    /// <summary>
    /// Sets the boundary mode at combat initialization.
    /// </summary>
    public static void InitializeForCombat(bool isTravelCombat)
    {
        CurrentMode = isTravelCombat ? CombatBoundaryMode.TravelEscapable : CombatBoundaryMode.ArenaLocked;
        CurrentEngagementContext = isTravelCombat ? DetermineEngagementContext() : EngagementContext.PlayerInvolved;
        InwardBiasMultiplier = 1.0f;
        _edgePressureRounds.Clear();
        _lastKnownHp.Clear();

        GD.Print($"[CombatBoundary] Initialized mode: {CurrentMode}. EngagementContext: {CurrentEngagementContext}.");
    }

    /// <summary>
    /// Checks if a world position is still inside the live combat grid envelope.
    /// </summary>
    public static bool IsInsideCombatBounds(Vector3 worldPosition)
    {
        return GridManager.Instance != null && GridManager.Instance.IsWithinWorldBounds(worldPosition);
    }

    /// <summary>
    /// Handles a requested movement step that may cross a combat border.
    ///
    /// Expected output:
    /// - Returns true when movement should stop at the current waypoint.
    /// - Returns false when movement is valid (or when travel-mode escape was processed).
    /// </summary>
    public static bool ShouldBlockMovementStep(CreatureStats creature, Vector3 currentPosition, Vector3 requestedWaypoint, bool allowsEscape)
    {
        if (creature == null || GridManager.Instance == null)
        {
            return false;
        }

        bool outOfBounds = !IsInsideCombatBounds(requestedWaypoint);
        if (!outOfBounds)
        {
            return false;
        }

        if (CurrentMode == CombatBoundaryMode.ArenaLocked)
        {
            GD.Print($"[CombatBoundary] Arena locked edge rejected movement for {creature.Name}.");
            return true;
        }

        if (!allowsEscape)
        {
            GD.Print($"[CombatBoundary] Travel edge crossing denied for {creature.Name} because no flee/disengage condition is active.");
            return true;
        }

        OnCombatBoundaryCross(creature, currentPosition, requestedWaypoint);
        return false;
    }

    /// <summary>
    /// Small deadlock detector for edge circling.
    /// </summary>
    public static void RegisterEdgePressure(CreatureStats creature, float edgePressureScore)
    {
        if (creature == null)
        {
            return;
        }

        int hpNow = creature.CurrentHP;
        if (!_lastKnownHp.TryGetValue(creature, out int lastHp))
        {
            _lastKnownHp[creature] = hpNow;
            _edgePressureRounds[creature] = 0;
            return;
        }

        bool nearEdge = edgePressureScore > 0.7f;
        bool noMeaningfulDamage = Mathf.Abs(lastHp - hpNow) < 2;
        int rounds = _edgePressureRounds.TryGetValue(creature, out int current) ? current : 0;

        rounds = (nearEdge && noMeaningfulDamage) ? rounds + 1 : 0;
        _edgePressureRounds[creature] = rounds;
        _lastKnownHp[creature] = hpNow;

        if (rounds >= 3)
        {
            InwardBiasMultiplier = Mathf.Clamp(InwardBiasMultiplier + 0.05f, 1.0f, 1.35f);
        }
    }

    private static void OnCombatBoundaryCross(CreatureStats creature, Vector3 previousPosition, Vector3 requestedWaypoint)
    {
        GD.Print($"[CombatBoundary] {creature.Name} crossed travel combat boundary.");

        // Step 2: remove creature from active combat-facing groups while keeping the world entity alive.
        creature.RemoveFromGroup("Enemy");
        creature.RemoveFromGroup("TravelEncounter");
        creature.RemoveFromGroup("TravelHostile");
        creature.RemoveFromGroup("TravelDefensive");
        creature.RemoveFromGroup("TravelPassive");
        creature.AddToGroup("TravelDisengaged");

        Vector3 exitVector = (requestedWaypoint - previousPosition).Normalized();
        if (exitVector == Vector3.Zero)
        {
            exitVector = (requestedWaypoint - GridManager.Instance.GlobalPosition).Normalized();
        }

        creature.SetMeta("TravelEscapeExitVector", exitVector);
        creature.SetMeta("TravelEscapeFinalCombatCoordinate", previousPosition);
        creature.GlobalPosition = requestedWaypoint;

        // Offscreen AI-only scenes and adjacent witness scenes never allocate hierarchical chase state.
        // They resolve consequences at an abstract level so no hidden tactical simulation keeps running.
        if (CurrentEngagementContext != EngagementContext.PlayerInvolved)
        {
            ResolveAbstractDisengagement(creature);
            CombatBoundaryCrossed?.Invoke(creature);
            return;
        }

        ContinueTravelTrackingAfterEscape(creature, requestedWaypoint, exitVector);

        // Register this crossing with the hierarchy tracker so the same living entity can continue
        // to exist across combat, tactical, and strategic layers without any delete/recreate cycle.
        Vector3 tacticalFeet = creature.HasMeta("TravelEscapeTacticalFeet")
            ? new Vector3(((Vector2)creature.GetMeta("TravelEscapeTacticalFeet")).X, 0f, ((Vector2)creature.GetMeta("TravelEscapeTacticalFeet")).Y)
            : Vector3.Zero;
        bool threatState = creature.IsInGroup("Enemy") || creature.IsInGroup("TravelHostile");
        TravelPursuitSystem.RegisterCombatEscape(creature, tacticalFeet, exitVector, threatState);

        // Every boundary crosser is also a possible chaser depending on faction and delay.
        // The pursuit system evaluates tactical timing windows internally.
        TravelPursuitSystem.RegisterPotentialPursuer(creature);

        CombatBoundaryCrossed?.Invoke(creature);

        // Step 3: if no hostiles remain in combat groups, trigger disengagement resolution event.
        bool anyHostiles = TurnManager.Instance?
            .GetAllCombatants()
            .Any(c => c != null && GodotObject.IsInstanceValid(c) && !c.IsDead && c.IsInGroup("Enemy")) == true;

        if (!anyHostiles)
        {
            TurnManager.Instance?.RegisterTravelResolutionEvent(TravelResolutionEvent.CombatEnded);
            GD.Print("[CombatBoundary] CombatEndsDueToDisengagement fired. Travel returns to TacticalMinute resolution.");
        }
    }

    /// <summary>
    /// Picks engagement context right at combat start so every later flee/pursuit decision follows one shared label.
    /// </summary>
    private static EngagementContext DetermineEngagementContext()
    {
        List<CreatureStats> combatants = TurnManager.Instance?.GetAllCombatants()
            ?.Where(c => c != null && GodotObject.IsInstanceValid(c) && !c.IsDead)
            .ToList() ?? new List<CreatureStats>();

        bool playerInCombat = combatants.Any(c => c.IsInGroup("Player") || c.IsInGroup("Ally"));
        if (playerInCombat)
        {
            return EngagementContext.PlayerInvolved;
        }

        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        List<CreatureStats> playerSideObservers = tree?
            .GetNodesInGroup("Player")
            .OfType<CreatureStats>()
            .Where(c => c != null && GodotObject.IsInstanceValid(c) && !c.IsDead)
            .ToList() ?? new List<CreatureStats>();

        foreach (CreatureStats observer in playerSideObservers)
        {
            foreach (CreatureStats combatant in combatants)
            {
                float distanceFeet = ConvertWorldDistanceToFeet(observer.GlobalPosition.DistanceTo(combatant.GlobalPosition));
                bool canSeeFight = LineOfSightManager.GetVisibility(observer, combatant).HasLineOfSight;
                if (distanceFeet <= AdjacentSightRangeFeet && canSeeFight)
                {
                    return EngagementContext.PlayerAdjacentSight;
                }
            }
        }

        return EngagementContext.OffscreenAIOnly;
    }

    /// <summary>
    /// Resolves non-player chase events without tactical tracking, grid updates, or path requests.
    /// </summary>
    private static void ResolveAbstractDisengagement(CreatureStats escaper)
    {
        List<CreatureStats> opponents = TurnManager.Instance?.GetAllCombatants()
            ?.Where(c => c != null
                && GodotObject.IsInstanceValid(c)
                && !c.IsDead
                && c != escaper
                && AreOpposingSides(c, escaper))
            .ToList() ?? new List<CreatureStats>();

        bool allyFleeingUnseen = (escaper.IsInGroup("Ally") || escaper.IsInGroup("Player"))
            && !TravelPursuitSystem.IsPlayerPursuing(escaper)
            && !TravelPursuitSystem.IsWithinPlayerSight(escaper);

        if (allyFleeingUnseen)
        {
            escaper.SetMeta("TravelOffscreenPursuitOutcome", "Escaped");
            GD.Print($"[CombatBoundary] {escaper.Name} left as an unseen ally retreat. Outcome resolved abstractly.");
            return;
        }

        TravelPursuitSystem.ResolveOffscreenDisengagement(escaper, opponents);
    }

    private static bool AreOpposingSides(CreatureStats a, CreatureStats b)
    {
        bool aPlayerSide = a.IsInGroup("Player") || a.IsInGroup("Ally");
        bool bPlayerSide = b.IsInGroup("Player") || b.IsInGroup("Ally");
        return aPlayerSide != bPlayerSide;
    }

    private static float ConvertWorldDistanceToFeet(float worldUnits)
    {
        float nodeDiameter = Mathf.Max(0.01f, GridManager.Instance?.nodeDiameter ?? 1f);
        return worldUnits * TravelScaleDefinitions.TacticalSquareFeet / nodeDiameter;
    }

    private static void ContinueTravelTrackingAfterEscape(CreatureStats creature, Vector3 worldPosition, Vector3 exitVector)
    {
        // Tactical continuation marker.
        creature.AddToGroup("TravelTacticalTracked");

        // Convert local combat offset to tactical-feet style metadata.
        Vector3 local = worldPosition - GridManager.Instance.GlobalPosition;
        float tacticalFeetX = local.X * TravelScaleDefinitions.TacticalSquareFeet / Mathf.Max(0.01f, GridManager.Instance.nodeDiameter);
        float tacticalFeetZ = local.Z * TravelScaleDefinitions.TacticalSquareFeet / Mathf.Max(0.01f, GridManager.Instance.nodeDiameter);
        creature.SetMeta("TravelEscapeTacticalFeet", new Vector2(tacticalFeetX, tacticalFeetZ));
        creature.SetMeta("TravelEscapeDirection", exitVector);

        float tacticalHalfSpan = TravelScaleDefinitions.TacticalWindowFeetPerSide * 0.5f;
        bool leftTacticalWindow = Mathf.Abs(tacticalFeetX) > tacticalHalfSpan || Mathf.Abs(tacticalFeetZ) > tacticalHalfSpan;

        if (leftTacticalWindow)
        {
            creature.RemoveFromGroup("TravelTacticalTracked");
            creature.AddToGroup("TravelStrategicTracked");

            // Tactical-to-strategic conversion preserves direction and threat context,
            // then TravelPhase resolution can legally operate at StrategicHour.
            TravelPursuitSystem.RegisterTacticalBoundaryCross(creature);
            TurnManager.Instance?.SetTravelResolutionState(TravelResolutionState.StrategicHour);
        }
    }
}
