using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Stateful pursuit data carried by a chaser that has moved beyond direct combat containment.
///
/// Expected output:
/// - The pursuer always remembers who is being chased.
/// - Direction and distance are kept as deterministic values until clarity forces probabilistic behavior.
/// - Clarity and fatigue evolve over time so long chases naturally degrade precision.
/// </summary>
public struct PursuitState
{
    public ulong TargetID;
    public Vector3 LastKnownDirection;
    public float DistanceEstimateFeet;
    public float PursuitClarity;
    public float FatigueLevel;
    public int HoursInChase;
}

/// <summary>
/// Travel-only hierarchy bridge for escape and pursuit transitions.
///
/// Expected output:
/// - Combat escapes transition to tactical metadata without deleting entities.
/// - Tactical boundary exits transition to strategic coordinates while preserving threat context.
/// - Pursuit runs deterministic at high clarity and probabilistic after enough uncertainty.
/// - Fatigue rises during active chase and recovers when the chase ends.
/// </summary>
public static class TravelPursuitSystem
{
    public enum OffscreenPursuitOutcome
    {
        Escaped,
        Caught,
        Stalemate
    }

    private sealed class EscapeRecord
    {
        public CreatureStats Escaper;
        public Vector3 TacticalFeetPosition;
        public Vector3 Direction;
        public bool ThreatState;
        public int TacticalTurnAtEscape;
    }

    private static readonly Dictionary<ulong, EscapeRecord> _recentEscapes = new();
    private static readonly Dictionary<ulong, PursuitState> _pursuitByPursuer = new();
    private static int _tacticalTurnCounter;

    /// <summary>
    /// Clears all tracking when a fresh travel run starts.
    /// </summary>
    public static void ResetForTravelPhase()
    {
        _recentEscapes.Clear();
        _pursuitByPursuer.Clear();
        _tacticalTurnCounter = 0;
    }

    /// <summary>
    /// Registers a combat-layer escape and stores tactical-layer continuation data.
    /// </summary>
    public static void RegisterCombatEscape(CreatureStats escaper, Vector3 tacticalFeetPosition, Vector3 direction, bool threatState)
    {
        if (CombatBoundaryService.CurrentEngagementContext != CombatBoundaryService.EngagementContext.PlayerInvolved)
        {
            return;
        }

        if (escaper == null || !GodotObject.IsInstanceValid(escaper))
        {
            return;
        }

        ulong escaperId = escaper.GetInstanceId();
        _recentEscapes[escaperId] = new EscapeRecord
        {
            Escaper = escaper,
            TacticalFeetPosition = tacticalFeetPosition,
            Direction = direction,
            ThreatState = threatState,
            TacticalTurnAtEscape = _tacticalTurnCounter
        };

        escaper.SetMeta("EscapingEntity", true);
        escaper.SetMeta("TravelEscapeTacticalFeet3", tacticalFeetPosition);
        escaper.SetMeta("TravelEscapeThreatState", threatState);
    }

    /// <summary>
    /// Registers a pursuer when it exits combat after a known target.
    /// </summary>
    public static void RegisterPotentialPursuer(CreatureStats pursuer)
    {
        if (CombatBoundaryService.CurrentEngagementContext != CombatBoundaryService.EngagementContext.PlayerInvolved)
        {
            return;
        }

        if (pursuer == null || !GodotObject.IsInstanceValid(pursuer))
        {
            return;
        }

        EscapeRecord bestTarget = ResolveBestTargetForPursuer(pursuer);
        if (bestTarget == null || bestTarget.Escaper == null)
        {
            return;
        }

        int delayTurns = Mathf.Max(0, _tacticalTurnCounter - bestTarget.TacticalTurnAtEscape);
        float clarity = delayTurns <= 1 ? 100f : delayTurns > 3 ? 58f : 82f;
        float uncertaintyFeet = delayTurns > 3 ? (delayTurns - 2) * 180f : 0f;

        Vector3 startingEstimate = bestTarget.TacticalFeetPosition;
        if (uncertaintyFeet > 0f)
        {
            // Deterministic pseudo-random offset from IDs so projections remain reproducible.
            float seed = (pursuer.GetInstanceId() % 97) * 0.123f;
            Vector3 side = new Vector3(bestTarget.Direction.Z, 0f, -bestTarget.Direction.X).Normalized();
            startingEstimate += side * (Mathf.Sin(seed) * uncertaintyFeet * 0.5f);
        }

        PursuitState state = new PursuitState
        {
            TargetID = bestTarget.Escaper.GetInstanceId(),
            LastKnownDirection = bestTarget.Direction,
            DistanceEstimateFeet = startingEstimate.DistanceTo(bestTarget.TacticalFeetPosition),
            PursuitClarity = clarity,
            FatigueLevel = 0f,
            HoursInChase = 0
        };

        _pursuitByPursuer[pursuer.GetInstanceId()] = state;
        AttachStateToEntityMeta(pursuer, state);
    }

    /// <summary>
    /// Converts tactical-layer escape coordinates into strategic-tile coordinates.
    /// </summary>
    public static void RegisterTacticalBoundaryCross(CreatureStats escaper)
    {
        if (CombatBoundaryService.CurrentEngagementContext != CombatBoundaryService.EngagementContext.PlayerInvolved)
        {
            return;
        }

        if (escaper == null || !GodotObject.IsInstanceValid(escaper))
        {
            return;
        }

        Vector3 tacticalFeet = escaper.HasMeta("TravelEscapeTacticalFeet3")
            ? (Vector3)escaper.GetMeta("TravelEscapeTacticalFeet3")
            : Vector3.Zero;

        int strategicX = Mathf.FloorToInt(tacticalFeet.X / TravelScaleDefinitions.StrategicTileFeet);
        int strategicZ = Mathf.FloorToInt(tacticalFeet.Z / TravelScaleDefinitions.StrategicTileFeet);
        escaper.SetMeta("TravelEscapeStrategicCoord", new Vector2I(strategicX, strategicZ));

        Vector3 direction = escaper.HasMeta("TravelEscapeDirection")
            ? (Vector3)escaper.GetMeta("TravelEscapeDirection")
            : Vector3.Zero;
        escaper.SetMeta("TravelEscapeStrategicDirection", direction);

        bool threatState = escaper.HasMeta("TravelEscapeThreatState") && (bool)escaper.GetMeta("TravelEscapeThreatState");
        escaper.SetMeta("TravelEscapeStrategicThreatState", threatState);
    }

    /// <summary>
    /// Runs tactical/strategic pursuit maintenance once per travel turn.
    /// </summary>
    public static void AdvanceTravelTurn(TravelResolutionState state, IEnumerable<CreatureStats> creatures)
    {
        if (CombatBoundaryService.CurrentEngagementContext != CombatBoundaryService.EngagementContext.PlayerInvolved)
        {
            // In non-player engagements we intentionally avoid background pursuit simulation.
            // Fatigue and trail clarity are player-chase features, so no hidden chase state is updated here.
            return;
        }

        if (state == TravelResolutionState.TacticalMinute)
        {
            _tacticalTurnCounter++;
        }

        if (creatures == null)
        {
            return;
        }

        foreach (CreatureStats creature in creatures)
        {
            if (creature == null || !GodotObject.IsInstanceValid(creature))
            {
                continue;
            }

            if (!_pursuitByPursuer.TryGetValue(creature.GetInstanceId(), out PursuitState pursuit))
            {
                RecoverFatigueWhenIdle(creature);
                continue;
            }

            CreatureStats target = _recentEscapes.TryGetValue(pursuit.TargetID, out EscapeRecord rec) ? rec.Escaper : null;
            if (target == null || !GodotObject.IsInstanceValid(target) || target.IsDead)
            {
                _pursuitByPursuer.Remove(creature.GetInstanceId());
                continue;
            }

            if (state == TravelResolutionState.TacticalMinute)
            {
                UpdateTacticalPursuit(creature, target, ref pursuit);
            }
            else if (state == TravelResolutionState.StrategicHour)
            {
                UpdateStrategicPursuit(creature, target, ref pursuit);
            }

            AttachStateToEntityMeta(creature, pursuit);
            _pursuitByPursuer[creature.GetInstanceId()] = pursuit;
        }
    }

    private static void UpdateTacticalPursuit(CreatureStats pursuer, CreatureStats target, ref PursuitState pursuit)
    {
        bool hasLos = LineOfSightManager.GetVisibility(pursuer, target).HasLineOfSight;
        if (!hasLos)
        {
            bool trackingSuccess = PerformTrackingCheck(pursuer, pursuit);
            if (!trackingSuccess)
            {
                pursuit.PursuitClarity = Mathf.Max(0f, pursuit.PursuitClarity - 12f);
            }
        }

        pursuit.DistanceEstimateFeet = Mathf.Max(0f, pursuer.GlobalPosition.DistanceTo(target.GlobalPosition) * TravelScaleDefinitions.TacticalSquareFeet / 6f);
    }

    private static void UpdateStrategicPursuit(CreatureStats pursuer, CreatureStats target, ref PursuitState pursuit)
    {
        float pursuerSpeed = Mathf.Max(5f, pursuer.Template?.Speed_Land ?? 30f);
        float targetSpeed = Mathf.Max(5f, target.Template?.Speed_Land ?? 30f);
        float speedDelta = targetSpeed - pursuerSpeed;
        pursuit.DistanceEstimateFeet = Mathf.Max(0f, pursuit.DistanceEstimateFeet + speedDelta * 600f);

        float terrainModifier = 6f;
        float weatherModifier = Mathf.Max(0f, WeatherManager.Instance?.CurrentWeather?.PerceptionPenalty ?? 0);
        float fatigueModifier = Mathf.Clamp(pursuit.FatigueLevel * 0.6f, 0f, 20f);
        pursuit.PursuitClarity = Mathf.Max(0f, pursuit.PursuitClarity - terrainModifier - weatherModifier - fatigueModifier);

        pursuit.HoursInChase += 1;
        float chaseIntensity = speedDelta < 0 ? 1.6f : 1.0f;
        pursuit.FatigueLevel += chaseIntensity;

        ApplyFatiguePenalties(pursuer, pursuit);

        if (pursuit.PursuitClarity < 10f)
        {
            pursuer.SetMeta("PursuitTargetLost", true);
            _pursuitByPursuer.Remove(pursuer.GetInstanceId());
            return;
        }

        if (pursuit.PursuitClarity < 30f)
        {
            int spread = Mathf.Clamp(Mathf.RoundToInt((30f - pursuit.PursuitClarity) / 5f), 1, 5);
            Vector2I baseTile = target.HasMeta("TravelEscapeStrategicCoord") ? (Vector2I)target.GetMeta("TravelEscapeStrategicCoord") : Vector2I.Zero;
            Vector2I probabilisticTile = new Vector2I(baseTile.X + (int)(pursuer.GetInstanceId() % (ulong)(spread + 1)) - spread / 2, baseTile.Y);
            pursuer.SetMeta("PursuitProbabilisticTile", probabilisticTile);
        }
    }

    private static void RecoverFatigueWhenIdle(CreatureStats pursuer)
    {
        if (!pursuer.HasMeta("PursuitState"))
        {
            return;
        }

        float fatigue = pursuer.HasMeta("PursuitFatigueLevel") ? (float)pursuer.GetMeta("PursuitFatigueLevel") : 0f;
        fatigue = Mathf.Max(0f, fatigue - 0.75f);
        pursuer.SetMeta("PursuitFatigueLevel", fatigue);

        if (fatigue <= 0f)
        {
            pursuer.SetMeta("PursuitFatigueSpeedPenaltyPercent", 0f);
            pursuer.SetMeta("PursuitFatiguePerceptionPenalty", 0f);
            pursuer.SetMeta("PursuitFatigueCooldownPenaltyPercent", 0f);
            pursuer.RemoveMeta("PursuitState");
            return;
        }

        ApplyFatiguePenalties(pursuer, new PursuitState { FatigueLevel = fatigue });
    }

    private static void ApplyFatiguePenalties(CreatureStats pursuer, PursuitState pursuit)
    {
        float speedPenalty = 0f;
        float perceptionPenalty = 0f;
        float cooldownPenalty = 0f;

        if (pursuit.FatigueLevel >= 3f)
        {
            speedPenalty = 0.05f;
            perceptionPenalty = 1f;
            cooldownPenalty = 0.05f;
        }

        if (pursuit.FatigueLevel >= 6f)
        {
            speedPenalty = 0.12f;
            perceptionPenalty = 3f;
            cooldownPenalty = 0.12f;
        }

        if (pursuit.FatigueLevel >= 10f)
        {
            speedPenalty = 0.2f;
            perceptionPenalty = 5f;
            cooldownPenalty = 0.2f;
        }

        pursuer.SetMeta("PursuitFatigueLevel", pursuit.FatigueLevel);
        pursuer.SetMeta("PursuitFatigueSpeedPenaltyPercent", speedPenalty);
        pursuer.SetMeta("PursuitFatiguePerceptionPenalty", perceptionPenalty);
        pursuer.SetMeta("PursuitFatigueCooldownPenaltyPercent", cooldownPenalty);
    }

    private static bool PerformTrackingCheck(CreatureStats pursuer, PursuitState pursuit)
    {
        int roll = Dice.Roll(1, 20);
        int trackingBonus = pursuer.GetSkillBonus(SkillType.Survival) + pursuer.GetPerceptionBonus();
        int dc = 15 + Mathf.RoundToInt((100f - pursuit.PursuitClarity) / 10f);
        return roll + trackingBonus >= dc;
    }

    /// <summary>
    /// Quickly resolves an offscreen chase using broad narrative factors rather than tactical movement.
    /// </summary>
    public static OffscreenPursuitOutcome ResolveOffscreenPursuit(CreatureStats pursuer, CreatureStats target)
    {
        if (pursuer == null || target == null || !GodotObject.IsInstanceValid(pursuer) || !GodotObject.IsInstanceValid(target))
        {
            return OffscreenPursuitOutcome.Stalemate;
        }

        float speedScore = (Mathf.Max(5f, pursuer.Template?.Speed_Land ?? 30f) - Mathf.Max(5f, target.Template?.Speed_Land ?? 30f)) * 0.35f;
        float terrainScore = ResolveTerrainModifier(target.GlobalPosition);
        float moraleScore = (ResolveMoralePressure(pursuer) - ResolveMoralePressure(target)) * 12f;
        float factionScore = ResolveFactionModifier(pursuer, target);
        float total = speedScore + terrainScore + moraleScore + factionScore;

        if (total >= 8f)
        {
            return OffscreenPursuitOutcome.Caught;
        }

        if (total <= -6f)
        {
            return OffscreenPursuitOutcome.Escaped;
        }

        return OffscreenPursuitOutcome.Stalemate;
    }

    /// <summary>
    /// Applies abstract offscreen outcome so no tactical tiles, pathfinding, or chase-state metadata are produced.
    /// </summary>
    public static void ResolveOffscreenDisengagement(CreatureStats escaper, IEnumerable<CreatureStats> pursuers)
    {
        CreatureStats bestPursuer = pursuers?
            .Where(p => p != null && GodotObject.IsInstanceValid(p) && !p.IsDead)
            .OrderByDescending(p => p.Template?.Speed_Land ?? 30f)
            .FirstOrDefault();

        if (bestPursuer == null)
        {
            escaper?.SetMeta("TravelOffscreenPursuitOutcome", OffscreenPursuitOutcome.Escaped.ToString());
            return;
        }

        OffscreenPursuitOutcome outcome = ResolveOffscreenPursuit(bestPursuer, escaper);
        escaper.SetMeta("TravelOffscreenPursuitOutcome", outcome.ToString());
        bestPursuer.SetMeta("TravelOffscreenPursuitOutcome", outcome.ToString());

        if (outcome == OffscreenPursuitOutcome.Caught)
        {
            // Simplified combat resolution: both sides take a small deterministic attrition hit.
            ApplyOffscreenAttrition(bestPursuer, escaper);
        }

        GD.Print($"[TravelPursuit] Offscreen resolution {bestPursuer.Name} vs {escaper.Name}: {outcome}.");
    }

    public static bool IsPlayerPursuing(CreatureStats target)
    {
        if (target == null || !GodotObject.IsInstanceValid(target))
        {
            return false;
        }

        ulong targetId = target.GetInstanceId();
        return _pursuitByPursuer.Any(p => p.Value.TargetID == targetId && IsPlayerSideEntityId(p.Key));
    }

    public static bool IsWithinPlayerSight(CreatureStats subject)
    {
        if (subject == null || !GodotObject.IsInstanceValid(subject))
        {
            return false;
        }

        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        List<CreatureStats> players = tree?.GetNodesInGroup("Player").OfType<CreatureStats>().ToList() ?? new List<CreatureStats>();
        return players.Any(player => player != null
            && GodotObject.IsInstanceValid(player)
            && !player.IsDead
            && LineOfSightManager.GetVisibility(player, subject).HasLineOfSight);
    }

    private static bool IsPlayerSideEntityId(ulong id)
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        return tree?.GetNodesInGroup("Player")
            .OfType<CreatureStats>()
            .Any(p => p != null && GodotObject.IsInstanceValid(p) && p.GetInstanceId() == id) == true;
    }

    private static float ResolveTerrainModifier(Vector3 position)
    {
        GridNode node = GridManager.Instance?.NodeFromWorldPoint(position);
        if (node == null)
        {
            return 0f;
        }

        return node.terrainType switch
        {
            TerrainType.Water => -4f,
            TerrainType.Ice => -2f,
            TerrainType.Air => 1f,
            TerrainType.Ground => 0.5f,
            _ => 0f
        };
    }

    private static float ResolveMoralePressure(CreatureStats creature)
    {
        float morale = creature.HasMeta("CurrentMorale") ? Mathf.Clamp((float)creature.GetMeta("CurrentMorale"), 0f, 1f) : 0.6f;
        if (creature.MyEffects != null && (creature.MyEffects.HasCondition(Condition.Panicked) || creature.MyEffects.HasCondition(Condition.Frightened)))
        {
            morale *= 0.4f;
        }

        morale -= creature.GetCorruptionMoralePenalty() * 0.5f;
        return Mathf.Clamp(morale, 0f, 1f);
    }

    private static float ResolveFactionModifier(CreatureStats pursuer, CreatureStats target)
    {
        bool pursuerPlayerSide = pursuer.IsInGroup("Player") || pursuer.IsInGroup("Ally");
        bool targetPlayerSide = target.IsInGroup("Player") || target.IsInGroup("Ally");
        if (pursuerPlayerSide == targetPlayerSide)
        {
            return -3f;
        }

        bool targetIsPlayer = target.IsInGroup("Player");
        return targetIsPlayer ? 2.5f : 1f;
    }

private static void ApplyOffscreenAttrition(CreatureStats pursuer, CreatureStats target)
    {
        if (pursuer != null && GodotObject.IsInstanceValid(pursuer))
        {
            int pursuerLoss = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, pursuer.GetEffectiveMaxHP()) * 0.03f));
            pursuer.TakeDamage(pursuerLoss, "OffscreenPursuit");
        }

        if (target != null && GodotObject.IsInstanceValid(target))
        {
            int targetLoss = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, target.GetEffectiveMaxHP()) * 0.12f));
            target.TakeDamage(targetLoss, "OffscreenPursuit");
        }
    }

    private static EscapeRecord ResolveBestTargetForPursuer(CreatureStats pursuer)
    {
        bool pursuerIsPlayerSide = pursuer.IsInGroup("Player") || pursuer.IsInGroup("Ally");

        return _recentEscapes.Values
            .Where(e => e != null && e.Escaper != null && GodotObject.IsInstanceValid(e.Escaper) && !e.Escaper.IsDead)
            .Where(e => pursuerIsPlayerSide ? e.Escaper.IsInGroup("Enemy") : (e.Escaper.IsInGroup("Player") || e.Escaper.IsInGroup("Ally")))
            .OrderByDescending(e => e.TacticalTurnAtEscape)
            .FirstOrDefault();
    }

    private static void AttachStateToEntityMeta(CreatureStats pursuer, PursuitState state)
    {
        var packed = new Godot.Collections.Dictionary<string, Variant>
        {
            ["TargetID"] = state.TargetID,
            ["LastKnownDirection"] = state.LastKnownDirection,
            ["DistanceEstimateFeet"] = state.DistanceEstimateFeet,
            ["PursuitClarity"] = state.PursuitClarity,
            ["FatigueLevel"] = state.FatigueLevel,
            ["HoursInChase"] = state.HoursInChase
        };

        pursuer.SetMeta("PursuitState", packed);
    }
}
