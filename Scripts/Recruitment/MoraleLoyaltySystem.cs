using Godot;
using System;

/// <summary>
/// Broad emotional state bands. Thresholds are configurable through MoraleLoyaltyTuning.
///
/// Expected output:
/// - AI systems can reason with stable named states instead of hardcoded float checks.
/// - Designers can tune thresholds without touching behavior code.
/// </summary>
public enum MoraleBand
{
    Broken,
    Shaken,
    Unsteady,
    Stable,
    Steadfast
}

/// <summary>
/// Allegiance commitment bands. Loyalty changes slower than morale by design.
///
/// Expected output:
/// - Arena obedience and desertion rules can use the same deterministic ladder.
/// - Betrayal logic can stay rare but possible by checking one explicit top-risk band.
/// </summary>
public enum LoyaltyBand
{
    Treacherous,
    Disloyal,
    Doubtful,
    Committed,
    Devoted
}

/// <summary>
/// Distinct obedience outcomes for player-issued orders.
///
/// Expected output:
/// - AI can express nuanced compliance instead of binary obey/refuse behavior.
/// - Narrative/UI hooks can describe why an ally followed, hesitated, or rebelled.
/// </summary>
public enum ObedienceOutcome
{
    FullCompliance,
    PartialCompliance,
    Refusal,
    ExtremeInstability
}

/// <summary>
/// Core tuning bundle for morale, loyalty, obedience, and instability pressure.
///
/// Expected output:
/// - Every important probability is centralized and data-driven.
/// - Combat and travel systems can consume one coherent model.
/// </summary>
public sealed class MoraleLoyaltyTuning
{
    public float SteadfastThreshold = 0.85f;
    public float StableThreshold = 0.65f;
    public float UnsteadyThreshold = 0.45f;
    public float ShakenThreshold = 0.25f;

    public float DevotedThreshold = 0.85f;
    public float CommittedThreshold = 0.65f;
    public float DoubtfulThreshold = 0.45f;
    public float DisloyalThreshold = 0.25f;

    public float HighAlignmentFrictionDecayPerSecond = 0.006f;
    public float MediumAlignmentFrictionDecayPerSecond = 0.003f;
    public float SharedAxesStabilityBonusPerSecond = 0.0015f;

    public float ObedienceBase = 0.45f;
    public float MoraleObedienceWeight = 0.35f;
    public float LoyaltyObedienceWeight = 0.45f;
    public float HealthObedienceWeight = 0.15f;
    public float ThreatObedienceWeight = 0.15f;
    public float FearPenalty = 0.25f;
    public float AlignmentFrictionPenalty = 0.2f;
    public float AuthorityObedienceWeight = 1f;


    public float PartialComplianceThreshold = 0.42f;
    public float RefusalThreshold = 0.22f;

    public float DesertionBase = 0.005f;
    public float DesertionMoraleWeight = 0.5f;
    public float DesertionLoyaltyWeight = 0.55f;
    public float DesertionOutnumberedWeight = 0.25f;
    public float DesertionLowLeaderHpWeight = 0.2f;
    public float DesertionLowSelfHpWeight = 0.2f;
    public float AuthorityDesertionWeight = 0.35f;


    public float BetrayalBase = 0.001f;
    public float BetrayalImminentDefeatBonus = 0.01f;
    public float BetrayalGlobalCap = 0.03f;

    public float AggressionModifierSteadfast = 1.2f;
    public float AggressionModifierStable = 1.05f;
    public float AggressionModifierUnsteady = 0.9f;
    public float AggressionModifierShaken = 0.72f;
    public float AggressionModifierBroken = 0.55f;

    public float RiskModifierSteadfast = 1.2f;
    public float RiskModifierStable = 1.05f;
    public float RiskModifierUnsteady = 0.85f;
    public float RiskModifierShaken = 0.65f;
    public float RiskModifierBroken = 0.45f;

    public float RetreatThresholdSteadfast = 0.75f;
    public float RetreatThresholdStable = 0.95f;
    public float RetreatThresholdUnsteady = 1.1f;
    public float RetreatThresholdShaken = 1.35f;
    public float RetreatThresholdBroken = 1.6f;

    public float InitiativeVarianceSteadfast = 0.03f;
    public float InitiativeVarianceStable = 0.06f;
    public float InitiativeVarianceUnsteady = 0.12f;
    public float InitiativeVarianceShaken = 0.18f;
    public float InitiativeVarianceBroken = 0.24f;

    public float VictoryMoraleDelta = 0.05f;
    public float AllySavedMoraleDelta = 0.07f;
    public float SharedAxisReinforcementDelta = 0.03f;
    public float StrongLeadershipMoraleDelta = 0.04f;
    public float SuccessfulTacticsMoraleDelta = 0.03f;

    public float AllyDeathMoraleDelta = -0.14f;
    public float LeaderNearDeathMoraleDelta = -0.09f;
    public float HeavyDamageMoraleDelta = -0.08f;
    public float ExtremeDangerOrderMoraleDelta = -0.06f;
    public float TravelHardshipMoraleDelta = -0.05f;

    public float LoyaltyDeltaScale = 0.2f;
    public float MoraleRecoveryIntelligenceWeight = 0.02f;
    public float MoraleRecoveryIntelligenceCap = 0.3f;
}

/// <summary>
/// One normalized input pack used to evaluate obedience probability.
/// </summary>
public struct ObedienceCheckContext
{
    public float Morale;
    public float Loyalty;
    public float HealthPercent;
    public float RelativeThreat;
    public bool IsAfraid;
    public float AlignmentFriction;
    public float AuthorityModifier;
}

public struct DesertionCheckContext
{
    public float Morale;
    public float Loyalty;
    public float SelfHpPercent;
    public float LeaderHpPercent;
    public float OutnumberedPressure;
    public float AlignmentFriction;
    public float AuthorityModifier;
}

/// <summary>
/// Runtime modifiers that adjust AI decision weights rather than hard-coding outcomes.
/// </summary>
public struct MoraleDecisionModifiers
{
    public float RiskBiasModifier;
    public float ObedienceModifier;
    public float AggressionModifier;
    public float RetreatThresholdModifier;
    public float InitiativeVariance;
}

/// <summary>
/// Global runtime access point for morale tuning.
/// </summary>
public static class MoraleLoyaltyRuntime
{
    public static MoraleLoyaltyTuning Tuning = new MoraleLoyaltyTuning();
}

public static class MoraleLoyaltyResolver
{
    public static MoraleBand ResolveMoraleBand(float morale)
    {
        MoraleLoyaltyTuning t = MoraleLoyaltyRuntime.Tuning;
        float v = Mathf.Clamp(morale, 0f, 1f);
        if (v >= t.SteadfastThreshold) return MoraleBand.Steadfast;
        if (v >= t.StableThreshold) return MoraleBand.Stable;
        if (v >= t.UnsteadyThreshold) return MoraleBand.Unsteady;
        if (v >= t.ShakenThreshold) return MoraleBand.Shaken;
        return MoraleBand.Broken;
    }

    public static LoyaltyBand ResolveLoyaltyBand(float loyalty)
    {
        MoraleLoyaltyTuning t = MoraleLoyaltyRuntime.Tuning;
        float v = Mathf.Clamp(loyalty, 0f, 1f);
        if (v >= t.DevotedThreshold) return LoyaltyBand.Devoted;
        if (v >= t.CommittedThreshold) return LoyaltyBand.Committed;
        if (v >= t.DoubtfulThreshold) return LoyaltyBand.Doubtful;
        if (v >= t.DisloyalThreshold) return LoyaltyBand.Disloyal;
        return LoyaltyBand.Treacherous;
    }

    public static MoraleDecisionModifiers BuildDecisionModifiers(float morale, float loyalty)
    {
        MoraleBand band = ResolveMoraleBand(morale);
        float risk = band switch
        {
            MoraleBand.Steadfast => MoraleLoyaltyRuntime.Tuning.RiskModifierSteadfast,
            MoraleBand.Stable => MoraleLoyaltyRuntime.Tuning.RiskModifierStable,
            MoraleBand.Unsteady => MoraleLoyaltyRuntime.Tuning.RiskModifierUnsteady,
            MoraleBand.Shaken => MoraleLoyaltyRuntime.Tuning.RiskModifierShaken,
            _ => MoraleLoyaltyRuntime.Tuning.RiskModifierBroken
        };

        float aggro = band switch
        {
            MoraleBand.Steadfast => MoraleLoyaltyRuntime.Tuning.AggressionModifierSteadfast,
            MoraleBand.Stable => MoraleLoyaltyRuntime.Tuning.AggressionModifierStable,
            MoraleBand.Unsteady => MoraleLoyaltyRuntime.Tuning.AggressionModifierUnsteady,
            MoraleBand.Shaken => MoraleLoyaltyRuntime.Tuning.AggressionModifierShaken,
            _ => MoraleLoyaltyRuntime.Tuning.AggressionModifierBroken
        };

        float retreat = band switch
        {
            MoraleBand.Steadfast => MoraleLoyaltyRuntime.Tuning.RetreatThresholdSteadfast,
            MoraleBand.Stable => MoraleLoyaltyRuntime.Tuning.RetreatThresholdStable,
            MoraleBand.Unsteady => MoraleLoyaltyRuntime.Tuning.RetreatThresholdUnsteady,
            MoraleBand.Shaken => MoraleLoyaltyRuntime.Tuning.RetreatThresholdShaken,
            _ => MoraleLoyaltyRuntime.Tuning.RetreatThresholdBroken
        };

        float variance = band switch
        {
            MoraleBand.Steadfast => MoraleLoyaltyRuntime.Tuning.InitiativeVarianceSteadfast,
            MoraleBand.Stable => MoraleLoyaltyRuntime.Tuning.InitiativeVarianceStable,
            MoraleBand.Unsteady => MoraleLoyaltyRuntime.Tuning.InitiativeVarianceUnsteady,
            MoraleBand.Shaken => MoraleLoyaltyRuntime.Tuning.InitiativeVarianceShaken,
            _ => MoraleLoyaltyRuntime.Tuning.InitiativeVarianceBroken
        };

        float obedience = Mathf.Lerp(0.65f, 1.25f, Mathf.Clamp(loyalty, 0f, 1f));

        return new MoraleDecisionModifiers
        {
            RiskBiasModifier = risk,
            AggressionModifier = aggro,
            RetreatThresholdModifier = retreat,
            InitiativeVariance = variance,
            ObedienceModifier = obedience
        };
    }

    public static float ComputeAlignmentFriction(AlignmentAxes leaderAxes, AlignmentAxes memberAxes)
    {
        if (leaderAxes == null || memberAxes == null)
        {
            return 0f;
        }

        if (leaderAxes.IsTrueNeutral || memberAxes.IsTrueNeutral)
        {
            return 0f;
        }

        bool opposedMoral = (leaderAxes.Moral == AlignmentAxisValue.Good && memberAxes.Moral == AlignmentAxisValue.Evil) ||
                            (leaderAxes.Moral == AlignmentAxisValue.Evil && memberAxes.Moral == AlignmentAxisValue.Good);
        bool opposedOrder = (leaderAxes.Order == AlignmentAxisValue.Lawful && memberAxes.Order == AlignmentAxisValue.Chaotic) ||
                            (leaderAxes.Order == AlignmentAxisValue.Chaotic && memberAxes.Order == AlignmentAxisValue.Lawful);

        if (opposedMoral && opposedOrder)
        {
            return 1f;
        }

        if (opposedMoral || opposedOrder)
        {
            return 0.6f;
        }

        bool sharedMoral = leaderAxes.Moral == memberAxes.Moral && leaderAxes.Moral != AlignmentAxisValue.Neutral;
        bool sharedOrder = leaderAxes.Order == memberAxes.Order && leaderAxes.Order != AlignmentAxisValue.Neutral;
        if (sharedMoral && sharedOrder)
        {
            return -0.15f;
        }

        return 0f;
    }

    public static float ComputeObedienceProbability(ObedienceCheckContext context)
    {
        MoraleLoyaltyTuning t = MoraleLoyaltyRuntime.Tuning;
        float p = t.ObedienceBase;
        p += Mathf.Clamp(context.Morale, 0f, 1f) * t.MoraleObedienceWeight;
        p += Mathf.Clamp(context.Loyalty, 0f, 1f) * t.LoyaltyObedienceWeight;
        p += Mathf.Clamp(context.HealthPercent, 0f, 1f) * t.HealthObedienceWeight;
        p -= Mathf.Clamp(context.RelativeThreat, 0f, 1f) * t.ThreatObedienceWeight;
        p -= Mathf.Clamp(context.AlignmentFriction, 0f, 1f) * t.AlignmentFrictionPenalty;
        p += context.AuthorityModifier * t.AuthorityObedienceWeight;

        if (context.IsAfraid)
        {
            p -= t.FearPenalty;
        }

        return Mathf.Clamp(p, 0.01f, 0.99f);
    }

    public static ObedienceOutcome RollObedienceOutcome(ObedienceCheckContext context, float random01)
    {
        MoraleBand moraleBand = ResolveMoraleBand(context.Morale);
        LoyaltyBand loyaltyBand = ResolveLoyaltyBand(context.Loyalty);

        float obedience = ComputeObedienceProbability(context);
        float roll = Mathf.Clamp(random01, 0f, 0.9999f);

        if (roll <= obedience)
        {
            return ObedienceOutcome.FullCompliance;
        }

        if (roll <= obedience + MoraleLoyaltyRuntime.Tuning.PartialComplianceThreshold)
        {
            return ObedienceOutcome.PartialCompliance;
        }

        bool extremeInstability = moraleBand == MoraleBand.Broken && (loyaltyBand == LoyaltyBand.Disloyal || loyaltyBand == LoyaltyBand.Treacherous);
        if (extremeInstability && roll > 1f - MoraleLoyaltyRuntime.Tuning.RefusalThreshold)
        {
            return ObedienceOutcome.ExtremeInstability;
        }

        return ObedienceOutcome.Refusal;
    }

    public static float ComputeDesertionProbability(DesertionCheckContext context)
    {
        MoraleLoyaltyTuning t = MoraleLoyaltyRuntime.Tuning;
        float p = t.DesertionBase;
        p += (1f - Mathf.Clamp(context.Morale, 0f, 1f)) * t.DesertionMoraleWeight;
        p += (1f - Mathf.Clamp(context.Loyalty, 0f, 1f)) * t.DesertionLoyaltyWeight;
        p += Mathf.Clamp(context.OutnumberedPressure, 0f, 1f) * t.DesertionOutnumberedWeight;
        p += (1f - Mathf.Clamp(context.LeaderHpPercent, 0f, 1f)) * t.DesertionLowLeaderHpWeight;
        p += (1f - Mathf.Clamp(context.SelfHpPercent, 0f, 1f)) * t.DesertionLowSelfHpWeight;
        p += Mathf.Clamp(context.AlignmentFriction, 0f, 1f) * 0.2f;
        p -= Mathf.Max(0f, context.AuthorityModifier) * t.AuthorityDesertionWeight;
        p += Mathf.Max(0f, -context.AuthorityModifier) * t.AuthorityDesertionWeight;
        return Mathf.Clamp(p, 0f, 0.97f);
    }


    public static float ApplyLeaderIntelligenceMoraleStability(float moraleDelta, int leaderIntelligence)
    {
        if (moraleDelta <= 0f)
        {
            float reduction = Mathf.Min(MoraleLoyaltyRuntime.Tuning.MoraleRecoveryIntelligenceCap, leaderIntelligence * MoraleLoyaltyRuntime.Tuning.MoraleRecoveryIntelligenceWeight);
            return moraleDelta * (1f - reduction);
        }

        float bonus = Mathf.Min(MoraleLoyaltyRuntime.Tuning.MoraleRecoveryIntelligenceCap, leaderIntelligence * MoraleLoyaltyRuntime.Tuning.MoraleRecoveryIntelligenceWeight);
        return moraleDelta * (1f + bonus);
    }

    public static float ComputeBetrayalProbability(float morale, float loyalty, float alignmentFriction, bool imminentDefeat)
    {
        LoyaltyBand loyaltyBand = ResolveLoyaltyBand(loyalty);
        MoraleBand moraleBand = ResolveMoraleBand(morale);

        if (loyaltyBand != LoyaltyBand.Treacherous || moraleBand != MoraleBand.Broken || alignmentFriction < 0.95f)
        {
            return 0f;
        }

        float p = MoraleLoyaltyRuntime.Tuning.BetrayalBase;
        if (imminentDefeat)
        {
            p += MoraleLoyaltyRuntime.Tuning.BetrayalImminentDefeatBonus;
        }

        return Mathf.Clamp(p, 0f, MoraleLoyaltyRuntime.Tuning.BetrayalGlobalCap);
    }
}
