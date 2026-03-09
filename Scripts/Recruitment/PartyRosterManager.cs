using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Strongly typed player choice offered after a successful arena victory.
///
/// Expected output:
/// - UI and gameplay systems can resolve recruitment outcomes without relying on string values.
/// - New outcomes can be added later (capture, ransom, interrogation) without breaking callers.
/// </summary>
public enum RecruitmentDecision
{
    Recruit,
    Study,
    Release,
    Execute
}

public enum RecruitmentRelationshipReason
{
    TwoAxisAligned,
    SharedSingleAxis,
    SharedSingleAxisWithOpposedOtherAxis,
    TrueNeutralBridge,
    Coerced
}

public enum AlignmentAxisValue
{
    Neutral,
    Good,
    Evil,
    Lawful,
    Chaotic
}

public sealed class AlignmentAxes
{
    public AlignmentAxisValue Moral;
    public AlignmentAxisValue Order;

    public bool IsTrueNeutral => Moral == AlignmentAxisValue.Neutral && Order == AlignmentAxisValue.Neutral;
}

/// <summary>
/// Tunable recruitment parameters exposed in one location for designers.
/// </summary>
public sealed class RecruitmentTuning
{
    public int LanguageIntelligenceThreshold = 6;
    public bool RequireLanguageForIntelligentCreatures = true;

    public float TwoAxisMorale = 0.85f;
    public float TwoAxisLoyalty = 0.85f;

    public float SharedAxisMorale = 0.65f;
    public float SharedAxisLoyalty = 0.6f;

    public float OpposedAxisMorale = 0.35f;
    public float OpposedAxisLoyalty = 0.3f;

    public float TrueNeutralMorale = 0.6f;
    public float TrueNeutralLoyalty = 0.55f;

    public float DesertionRiskBonusWhenOpposed = 0.2f;
}

public sealed class PartyMemberState
{
    public string PartyMemberId;
    public CreatureTemplate_SO Template;
    public CreatureStats RuntimeInstance;
    public float BaseMorale;
    public float CurrentMorale;
    public float Loyalty;
    public float DesertionRiskModifier;
    public RecruitmentRelationshipReason RecruitmentReason;
}

public sealed class RecruitmentEvaluation
{
    public bool CanRecruit;
    public string FailureReason;
    public string IntelligenceDifferenceWarning;
    public RecruitmentRelationshipReason RelationshipReason;
    public float StartingMorale;
    public float StartingLoyalty;
    public float DesertionRiskModifier;
}

/// <summary>
/// Party roster service that treats recruited creatures as true entities rather than passive bonuses.
///
/// Expected output:
/// - AI systems can query morale/loyalty state in real time.
/// - Recruitment relationship reason is preserved for friction and decay calculations.
/// - Morale events are broadcast with deterministic deltas so behavior changes are reproducible.
/// </summary>
public sealed class PartyRosterManager
{
    private readonly Dictionary<string, PartyMemberState> _members = new(StringComparer.OrdinalIgnoreCase);
    public RecruitmentTuning Tuning { get; } = new RecruitmentTuning();

    /// <summary>
    /// Raised whenever morale-driving events are applied to one or more members.
    /// </summary>
    public event Action<string, float> MoraleEventApplied;

    public IReadOnlyCollection<PartyMemberState> ActiveMembers => _members.Values;

    public RecruitmentEvaluation EvaluateRecruitment(CreatureStats recruiter, CreatureStats candidate)
    {
        RecruitmentEvaluation result = new RecruitmentEvaluation();
        if (recruiter?.Template == null || candidate?.Template == null)
        {
            result.CanRecruit = false;
            result.FailureReason = "Recruitment requires both recruiter and candidate templates.";
            return result;
        }

        if (!CanSatisfyIntelligenceLanguageGate(recruiter, candidate, out string languageFailure))
        {
            result.CanRecruit = false;
            result.FailureReason = languageFailure;
            result.IntelligenceDifferenceWarning = BuildIntelligenceDifferenceWarning(recruiter, candidate);
            return result;
        }

        AlignmentAxes recruiterAxes = ParseAlignment(recruiter.Template.Alignment);
        AlignmentAxes candidateAxes = ParseAlignment(candidate.Template.Alignment);

        bool sharesMoral = recruiterAxes.Moral == candidateAxes.Moral && recruiterAxes.Moral != AlignmentAxisValue.Neutral;
        bool sharesOrder = recruiterAxes.Order == candidateAxes.Order && recruiterAxes.Order != AlignmentAxisValue.Neutral;
        bool oneTrueNeutral = recruiterAxes.IsTrueNeutral || candidateAxes.IsTrueNeutral;

        if (!oneTrueNeutral && !sharesMoral && !sharesOrder)
        {
            result.CanRecruit = false;
            result.FailureReason = "Recruitment failed because neither moral nor order axis is shared.";
            result.IntelligenceDifferenceWarning = BuildIntelligenceDifferenceWarning(recruiter, candidate);
            return result;
        }

        result.CanRecruit = true;

        bool opposedOtherAxis = (sharesMoral && AreOpposedOrder(recruiterAxes.Order, candidateAxes.Order)) ||
                               (sharesOrder && AreOpposedMoral(recruiterAxes.Moral, candidateAxes.Moral));

        if (sharesMoral && sharesOrder)
        {
            result.RelationshipReason = RecruitmentRelationshipReason.TwoAxisAligned;
            result.StartingMorale = Tuning.TwoAxisMorale;
            result.StartingLoyalty = Tuning.TwoAxisLoyalty;
        }
        else if (oneTrueNeutral)
        {
            result.RelationshipReason = RecruitmentRelationshipReason.TrueNeutralBridge;
            result.StartingMorale = Tuning.TrueNeutralMorale;
            result.StartingLoyalty = Tuning.TrueNeutralLoyalty;
        }
        else if (opposedOtherAxis)
        {
            result.RelationshipReason = RecruitmentRelationshipReason.SharedSingleAxisWithOpposedOtherAxis;
            result.StartingMorale = Tuning.OpposedAxisMorale;
            result.StartingLoyalty = Tuning.OpposedAxisLoyalty;
            result.DesertionRiskModifier = Tuning.DesertionRiskBonusWhenOpposed;
        }
        else
        {
            result.RelationshipReason = RecruitmentRelationshipReason.SharedSingleAxis;
            result.StartingMorale = Tuning.SharedAxisMorale;
            result.StartingLoyalty = Tuning.SharedAxisLoyalty;
        }

        result.IntelligenceDifferenceWarning = BuildIntelligenceDifferenceWarning(recruiter, candidate);

        float recruiterCorruption = recruiter.GetCorruptionMetrics().CorruptionSeverityScore;
        float candidateCorruption = candidate.GetCorruptionMetrics().CorruptionSeverityScore;
        float corruptionPressure = Mathf.Clamp(recruiterCorruption + candidateCorruption, 0f, 1.5f);

        result.StartingMorale = Mathf.Clamp01(result.StartingMorale - corruptionPressure * 0.15f);
        result.StartingLoyalty = Mathf.Clamp01(result.StartingLoyalty - corruptionPressure * 0.1f);
        result.DesertionRiskModifier += corruptionPressure * 0.25f;

        return result;
    }

    public bool RecruitCreature(CreatureStats recruiter, CreatureStats defeatedCandidate, GridNode spawnParent, Vector3 spawnPosition, CreaturePersistenceService persistence, out string failureReason)
    {
        failureReason = null;

        RecruitmentEvaluation evaluation = EvaluateRecruitment(recruiter, defeatedCandidate);
        if (!evaluation.CanRecruit)
        {
            failureReason = evaluation.FailureReason;
            return false;
        }

        if (defeatedCandidate?.Template?.CreatureScene == null)
        {
            failureReason = "Recruitment failed because candidate template does not define a CreatureScene.";
            return false;
        }

        PackedScene scene = defeatedCandidate.Template.CreatureScene;
        Node3D spawnedNode = scene.Instantiate<Node3D>();
        if (spawnedNode == null)
        {
            failureReason = "Recruitment failed because creature prefab could not instantiate.";
            return false;
        }

        spawnParent?.AddChild(spawnedNode);
        spawnedNode.GlobalPosition = spawnPosition;

        CreatureStats recruitedStats = spawnedNode as CreatureStats ?? spawnedNode.GetNodeOrNull<CreatureStats>("CreatureStats");
        if (recruitedStats == null)
        {
            spawnedNode.QueueFree();
            failureReason = "Recruitment failed because prefab did not expose CreatureStats.";
            return false;
        }

        recruitedStats.RemoveFromGroup("Enemy");
        recruitedStats.AddToGroup("Player");
        recruitedStats.AddToGroup("Ally");
        recruitedStats.AddToGroup("PlayerTeam");

        persistence?.RegisterCreature(recruitedStats);

        string id = BuildRosterId(recruitedStats);
        _members[id] = new PartyMemberState
        {
            PartyMemberId = id,
            Template = defeatedCandidate.Template,
            RuntimeInstance = recruitedStats,
            BaseMorale = evaluation.StartingMorale,
            CurrentMorale = evaluation.StartingMorale,
            Loyalty = evaluation.StartingLoyalty,
            DesertionRiskModifier = evaluation.DesertionRiskModifier,
            RecruitmentReason = evaluation.RelationshipReason
        };

        return true;
    }

    /// <summary>
    /// Removes a member after death and broadcasts a negative morale event to survivors.
    /// </summary>
    public void RemoveOnDeath(CreatureStats creature)
    {
        if (creature == null)
        {
            return;
        }

        string id = BuildRosterId(creature);
        if (_members.Remove(id))
        {
            ApplyMoraleEvent("AllyDeath", MoraleLoyaltyRuntime.Tuning.AllyDeathMoraleDelta);
        }
    }

    public void ApplyVictoryMoraleEvent() => ApplyMoraleEvent("Victory", MoraleLoyaltyRuntime.Tuning.VictoryMoraleDelta);
    public void ApplyLossMoraleEvent() => ApplyMoraleEvent("HeavyLoss", MoraleLoyaltyRuntime.Tuning.LeaderNearDeathMoraleDelta);
    public void ApplyTravelHardshipMoraleEvent() => ApplyMoraleEvent("TravelHardship", MoraleLoyaltyRuntime.Tuning.TravelHardshipMoraleDelta);

    public bool TryGetMemberState(CreatureStats creature, out PartyMemberState state)
    {
        state = null;
        if (creature == null)
        {
            return false;
        }

        string id = BuildRosterId(creature);
        return _members.TryGetValue(id, out state);
    }

    public void ApplyMoraleEvent(string eventId, float delta)
    {
        CreatureStats leader = TurnManager.Instance?.GetPlayerLeader();
        float moraleDecayModifier = IntelligenceGrowthRuntime.Service.ComputeMoraleDecayMultiplier(leader);
        float stabilizedDelta = delta >= 0f ? delta * (2f - moraleDecayModifier) : delta * moraleDecayModifier;

        foreach (PartyMemberState member in _members.Values)
        {
            member.CurrentMorale = Mathf.Clamp(member.CurrentMorale + stabilizedDelta, 0f, 1f);

            float loyaltyDelta = stabilizedDelta * MoraleLoyaltyRuntime.Tuning.LoyaltyDeltaScale;
            if (MoraleLoyaltyResolver.ResolveMoraleBand(member.CurrentMorale) == MoraleBand.Broken)
            {
                loyaltyDelta -= 0.05f + member.DesertionRiskModifier;
            }

            member.Loyalty = Mathf.Clamp(member.Loyalty + loyaltyDelta, 0f, 1f);
        }

        MoraleEventApplied?.Invoke(eventId, delta);
    }

    /// <summary>
    /// Applies passive morale pressure from alignment friction over travel/combat time.
    /// </summary>
    public void ApplyAlignmentFrictionDecay(CreatureStats leader, float deltaSeconds)
    {
        if (leader?.Template == null || deltaSeconds <= 0f)
        {
            return;
        }

        AlignmentAxes leaderAxes = ParseAlignment(leader.Template.Alignment);

        foreach (PartyMemberState member in _members.Values)
        {
            if (member?.Template == null)
            {
                continue;
            }

            AlignmentAxes memberAxes = ParseAlignment(member.Template.Alignment);
            float friction = MoraleLoyaltyResolver.ComputeAlignmentFriction(leaderAxes, memberAxes);

            float delta = 0f;
            if (friction >= 0.95f)
            {
                delta = -MoraleLoyaltyRuntime.Tuning.HighAlignmentFrictionDecayPerSecond * deltaSeconds;
            }
            else if (friction > 0f)
            {
                delta = -MoraleLoyaltyRuntime.Tuning.MediumAlignmentFrictionDecayPerSecond * deltaSeconds;
            }
            else if (friction < 0f)
            {
                delta = MoraleLoyaltyRuntime.Tuning.SharedAxesStabilityBonusPerSecond * deltaSeconds;
            }

            if (Mathf.Abs(delta) > 0.0001f)
            {
                member.CurrentMorale = Mathf.Clamp(member.CurrentMorale + delta, 0f, 1f);
            }
        }
    }

    public static string BuildRosterId(CreatureStats creature)
    {
        return $"{creature?.Template?.CreatureName ?? "Unknown"}:{creature?.GetInstanceId() ?? 0}";
    }

    public static AlignmentAxes ParseAlignment(string alignment)
    {
        string normalized = alignment?.ToLowerInvariant() ?? string.Empty;
        AlignmentAxes axes = new AlignmentAxes
        {
            Moral = normalized.Contains("good") ? AlignmentAxisValue.Good : normalized.Contains("evil") ? AlignmentAxisValue.Evil : AlignmentAxisValue.Neutral,
            Order = (normalized.Contains("lawful") || normalized.Contains("law")) ? AlignmentAxisValue.Lawful : (normalized.Contains("chaotic") || normalized.Contains("chaos")) ? AlignmentAxisValue.Chaotic : AlignmentAxisValue.Neutral
        };

        if (normalized.Contains("lg")) axes = new AlignmentAxes { Moral = AlignmentAxisValue.Good, Order = AlignmentAxisValue.Lawful };
        if (normalized.Contains("ng")) axes = new AlignmentAxes { Moral = AlignmentAxisValue.Good, Order = AlignmentAxisValue.Neutral };
        if (normalized.Contains("cg")) axes = new AlignmentAxes { Moral = AlignmentAxisValue.Good, Order = AlignmentAxisValue.Chaotic };
        if (normalized.Contains("ln")) axes = new AlignmentAxes { Moral = AlignmentAxisValue.Neutral, Order = AlignmentAxisValue.Lawful };
        if (normalized.Contains("tn") || normalized.Contains("true neutral") || normalized == "neutral") axes = new AlignmentAxes { Moral = AlignmentAxisValue.Neutral, Order = AlignmentAxisValue.Neutral };
        if (normalized.Contains("cn")) axes = new AlignmentAxes { Moral = AlignmentAxisValue.Neutral, Order = AlignmentAxisValue.Chaotic };
        if (normalized.Contains("le")) axes = new AlignmentAxes { Moral = AlignmentAxisValue.Evil, Order = AlignmentAxisValue.Lawful };
        if (normalized.Contains("ne")) axes = new AlignmentAxes { Moral = AlignmentAxisValue.Evil, Order = AlignmentAxisValue.Neutral };
        if (normalized.Contains("ce")) axes = new AlignmentAxes { Moral = AlignmentAxisValue.Evil, Order = AlignmentAxisValue.Chaotic };

        return axes;
    }

    private bool CanSatisfyIntelligenceLanguageGate(CreatureStats recruiter, CreatureStats candidate, out string failure)
    {
        failure = null;

        bool recruiterLowInt = recruiter.Template.Intelligence < Tuning.LanguageIntelligenceThreshold;
        bool candidateLowInt = candidate.Template.Intelligence < Tuning.LanguageIntelligenceThreshold;
        bool sharesLanguage = recruiter.SharesLanguageWith(candidate);
        bool hasPartialComprehension = candidate.Template?.RacialLanguages != null &&
                                       candidate.Template.RacialLanguages.Any(language => IntelligenceGrowthRuntime.Service.HasPartialComprehension(recruiter, language));

        if (recruiterLowInt)
        {
            if (!candidateLowInt && !sharesLanguage && !hasPartialComprehension)
            {
                failure = "Recruitment failed because low-intelligence leader cannot coordinate with intelligent candidate without shared language.";
                return false;
            }

            return true;
        }

        if (Tuning.RequireLanguageForIntelligentCreatures && !candidateLowInt && !sharesLanguage && !hasPartialComprehension)
        {
            failure = "Recruitment failed because intelligent candidate does not share language with recruiter.";
            return false;
        }

        return true;
    }

    private static string BuildIntelligenceDifferenceWarning(CreatureStats recruiter, CreatureStats candidate)
    {
        int recruiterInt = recruiter?.Template?.Intelligence ?? 0;
        int candidateInt = candidate?.Template?.Intelligence ?? 0;
        int diff = candidateInt - recruiterInt;
        if (diff <= 0)
        {
            return null;
        }

        return $"Candidate intelligence exceeds leader by {diff}. Smart allies may resist low-authority leadership.";
    }

    private static bool AreOpposedMoral(AlignmentAxisValue a, AlignmentAxisValue b)
    {
        return (a == AlignmentAxisValue.Good && b == AlignmentAxisValue.Evil) || (a == AlignmentAxisValue.Evil && b == AlignmentAxisValue.Good);
    }

    private static bool AreOpposedOrder(AlignmentAxisValue a, AlignmentAxisValue b)
    {
        return (a == AlignmentAxisValue.Lawful && b == AlignmentAxisValue.Chaotic) || (a == AlignmentAxisValue.Chaotic && b == AlignmentAxisValue.Lawful);
    }
}

public static class PartyRosterRuntime
{
    public static PartyRosterManager ActiveManager;
}
