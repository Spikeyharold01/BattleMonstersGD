using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// What style of manipulative pressure is being attempted against the player's judgment.
///
/// Expected output:
/// - Awareness and deception systems can ask for resistance in plain terms.
/// - The intelligence manager can apply one coherent resistance model across illusions, mimicry, and social pressure.
/// </summary>
public enum ManipulationType
{
    Illusion,
    Mimicry,
    Psychological
}

/// <summary>
/// Data-driven tuning bundle for the unified intelligence architecture.
///
/// Expected output:
/// - All progression, awareness, morale, and language behavior can be tuned without code edits.
/// - No hidden constants are required in downstream systems.
/// </summary>
public sealed class IntelligenceSystemTuning
{
    public float IQThresholdMultiplier = 4f;
    public int ExposureClampMaxDifference = 6;
    public float NearParityExposureMultiplier = 0.5f;

    public float DetectionWeight = 0.03f;
    public float DetectionModifierMin = -0.45f;
    public float DetectionModifierMax = 0.45f;

    public float EscalationWeight = 0.05f;
    public float EscalationModifierMin = -0.5f;
    public float EscalationModifierMax = 0.5f;

    public float ResistanceWeight = 0.02f;
    public float ResistanceBonusCap = 0.9f;

    public float InvestigationDelayWeight = 0.02f;
    public float InvestigationDelayCap = 0.30f;

    public float AuthorityWeight = 0.02f;
    public float MoraleStabilityWeight = 0.02f;
    public float MoraleStabilityCap = 0.30f;

    public float LanguageExposureMultiplier = 2f;
    public float LanguageBaseThreshold = 50f;
    public float LanguageScalingFactor = 10f;
    public int LanguageLearningIntMinimum = 4;
    public int LanguageReducedGainIntMinimum = 6;
    public float LanguageReducedGainMultiplier = 0.5f;
    public float PartialComprehensionThresholdPercent = 0.5f;

    public bool AllowNonPlayerEntities = false;
}

/// <summary>
/// Persistent cognitive profile for one controlled creature or faction leader.
///
/// Expected output:
/// - Player cognition stays separate from static template IQ values.
/// - Progression memory survives arena and travel transitions.
/// </summary>
public sealed class IntelligenceProfileState
{
    public int CurrentINT;
    public float CognitiveExposure;
    public HashSet<Language> LanguagesKnown = new();
    public Dictionary<Language, float> LanguageExposure = new();
    public HashSet<ulong> StudiedCreatureInstanceIds = new();
}

/// <summary>
/// Result payload returned after attempting a Study outcome.
///
/// Expected output:
/// - UI and logs can explain whether study was valid, what exposure was gained, and what languages were unlocked.
/// - Recruitment and telemetry systems can remain deterministic while still being expressive.
/// </summary>
public sealed class IntelligenceUiSnapshot
{
    public int CurrentINT;
    public float CurrentExposure;
    public float Threshold;
    public float Progress01;
}

public sealed class IntelligenceStudyResult
{
    public bool Applied;
    public string FailureReason;
    public float ExposureGained;
    public int IntelligenceBefore;
    public int IntelligenceAfter;
    public List<Language> NewlyLearnedLanguages = new();
}

/// <summary>
/// Runtime singleton access point used by recruitment, morale, awareness, and AI systems.
/// </summary>
public static class IntelligenceSystemRuntime
{
    public static IntelligenceSystemTuning Tuning = new();
    public static IntelligenceSystemManager Manager = new();
}

/// <summary>
/// Backward-compatible runtime alias so existing call sites can keep compiling while now using the unified manager.
/// </summary>
public static class IntelligenceGrowthRuntime
{
    public static IntelligenceSystemTuning Tuning => IntelligenceSystemRuntime.Tuning;
    public static IntelligenceSystemManager Service => IntelligenceSystemRuntime.Manager;
}

/// <summary>
/// Unified intelligence manager that owns cognitive growth, language acquisition, authority pressure, and awareness modifiers.
///
/// Expected output:
/// - Intelligence growth is strategic and exposure-based rather than grind-based.
/// - Awareness and morale systems consume one deterministic intelligence authority.
/// - The architecture remains extensible and supports optional non-player progression.
/// </summary>
public sealed class IntelligenceSystemManager
{
    private readonly Dictionary<string, IntelligenceProfileState> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public IntelligenceProfileState GetOrCreateState(CreatureStats owner)
    {
        if (owner?.Template == null)
        {
            return new IntelligenceProfileState();
        }

        string id = BuildOwnerId(owner);
        if (!_profiles.TryGetValue(id, out IntelligenceProfileState state))
        {
            state = new IntelligenceProfileState
            {
                CurrentINT = owner.Template.Intelligence
            };

            if (owner.Template.RacialLanguages != null)
            {
                state.LanguagesKnown.UnionWith(owner.Template.RacialLanguages);
            }

            if (owner.LearnedLanguages != null)
            {
                state.LanguagesKnown.UnionWith(owner.LearnedLanguages);
            }

            _profiles[id] = state;
        }

        return state;
    }

    public int GetEffectiveIntelligence(CreatureStats owner)
    {
        return owner?.Template == null ? 0 : GetOrCreateState(owner).CurrentINT;
    }

    public float ComputeThreshold(int intelligence)
    {
        int intValue = Mathf.Max(1, intelligence);
        return IntelligenceSystemRuntime.Tuning.IQThresholdMultiplier * intValue * intValue;
    }

    public float GetExposureProgress01(CreatureStats owner)
    {
        IntelligenceProfileState state = GetOrCreateState(owner);
        float threshold = ComputeThreshold(state.CurrentINT);
        return threshold <= 0f ? 1f : Mathf.Clamp(state.CognitiveExposure / threshold, 0f, 1f);
    }

    public IntelligenceUiSnapshot BuildUiSnapshot(CreatureStats owner)
    {
        IntelligenceProfileState state = GetOrCreateState(owner);
        float threshold = ComputeThreshold(state.CurrentINT);
        return new IntelligenceUiSnapshot
        {
            CurrentINT = state.CurrentINT,
            CurrentExposure = state.CognitiveExposure,
            Threshold = threshold,
            Progress01 = threshold <= 0f ? 1f : Mathf.Clamp(state.CognitiveExposure / threshold, 0f, 1f)
        };
    }

    public float GetLanguageProgress01(CreatureStats owner, Language language)
    {
        IntelligenceProfileState state = GetOrCreateState(owner);
        state.LanguageExposure.TryGetValue(language, out float current);
        float threshold = ComputeLanguageThreshold(state.LanguagesKnown.Count);
        return threshold <= 0f ? 1f : Mathf.Clamp(current / threshold, 0f, 1f);
    }

    public bool HasPartialComprehension(CreatureStats owner, Language language)
    {
        IntelligenceProfileState state = GetOrCreateState(owner);
        if (state.LanguagesKnown.Contains(language))
        {
            return true;
        }

        state.LanguageExposure.TryGetValue(language, out float current);
        float threshold = ComputeLanguageThreshold(state.LanguagesKnown.Count);
        return current >= (threshold * IntelligenceSystemRuntime.Tuning.PartialComprehensionThresholdPercent);
    }

    public float GetAuthorityModifier(CreatureStats leader, CreatureStats ally)
    {
        int leaderInt = GetEffectiveIntelligence(leader);
        int allyInt = ally?.Template?.Intelligence ?? 0;
        return (leaderInt - allyInt) * IntelligenceSystemRuntime.Tuning.AuthorityWeight;
    }

    public float ComputeAuthorityModifierPercent(CreatureStats leader, CreatureStats ally)
    {
        return GetAuthorityModifier(leader, ally);
    }

    public float ComputeMoraleDecayMultiplier(CreatureStats leader)
    {
        int leaderInt = GetEffectiveIntelligence(leader);
        float reduction = Mathf.Min(IntelligenceSystemRuntime.Tuning.MoraleStabilityCap, leaderInt * IntelligenceSystemRuntime.Tuning.MoraleStabilityWeight);
        return Mathf.Clamp(1f - reduction, 0.1f, 1f);
    }

    public float ComputeDetectionModifier(CreatureStats leader, int threatInt)
    {
        int playerInt = GetEffectiveIntelligence(leader);
        float modifier = (playerInt - threatInt) * IntelligenceSystemRuntime.Tuning.DetectionWeight;
        return Mathf.Clamp(modifier, IntelligenceSystemRuntime.Tuning.DetectionModifierMin, IntelligenceSystemRuntime.Tuning.DetectionModifierMax);
    }

    public float ComputeEscalationModifier(CreatureStats leader, int threatInt)
    {
        int playerInt = GetEffectiveIntelligence(leader);
        float modifier = (threatInt - playerInt) * IntelligenceSystemRuntime.Tuning.EscalationWeight;
        return Mathf.Clamp(modifier, IntelligenceSystemRuntime.Tuning.EscalationModifierMin, IntelligenceSystemRuntime.Tuning.EscalationModifierMax);
    }

    public float ComputeManipulationResistanceBonus(CreatureStats leader, ManipulationType manipulationType)
    {
        _ = manipulationType;
        int playerInt = GetEffectiveIntelligence(leader);
        return Mathf.Clamp(playerInt * IntelligenceSystemRuntime.Tuning.ResistanceWeight, 0f, IntelligenceSystemRuntime.Tuning.ResistanceBonusCap);
    }

    public float ComputeInvestigationDelayMultiplier(CreatureStats leader)
    {
        int playerInt = GetEffectiveIntelligence(leader);
        float reduction = Mathf.Min(IntelligenceSystemRuntime.Tuning.InvestigationDelayCap, playerInt * IntelligenceSystemRuntime.Tuning.InvestigationDelayWeight);
        return Mathf.Clamp(1f - reduction, 0.1f, 1f);
    }

    public bool CanStudy(CreatureStats leader, CreatureStats defeated, RecruitmentEvaluation evaluation, bool hadMeaningfulChoice, out string reason)
    {
        reason = null;

        if (leader?.Template == null || defeated?.Template == null)
        {
            reason = "Study requires both the acting leader and the defeated creature.";
            return false;
        }

        if (evaluation == null || !evaluation.CanRecruit)
        {
            reason = "Study is only valid when recruitment was genuinely possible.";
            return false;
        }

        if (!hadMeaningfulChoice)
        {
            reason = "Study grants no exposure when there was no real decision between outcomes.";
            return false;
        }

        int leaderInt = GetEffectiveIntelligence(leader);
        int targetInt = defeated.Template.Intelligence;
        if (targetInt <= leaderInt)
        {
            reason = "Study only grants growth when the target intelligence is higher than the leader intelligence.";
            return false;
        }

        return true;
    }

    public IntelligenceStudyResult ApplyStudy(CreatureStats leader, CreatureStats defeated, RecruitmentEvaluation evaluation, bool hadMeaningfulChoice = true)
    {
        IntelligenceStudyResult result = new();
        if (!CanStudy(leader, defeated, evaluation, hadMeaningfulChoice, out string failure))
        {
            result.Applied = false;
            result.FailureReason = failure;
            return result;
        }

        IntelligenceProfileState state = GetOrCreateState(leader);
        ulong defeatedId = defeated.GetInstanceId();
        if (state.StudiedCreatureInstanceIds.Contains(defeatedId))
        {
            result.Applied = false;
            result.FailureReason = "This creature instance has already been studied once.";
            return result;
        }

        state.StudiedCreatureInstanceIds.Add(defeatedId);

        int leaderInt = state.CurrentINT;
        int targetInt = defeated.Template.Intelligence;

        result.IntelligenceBefore = leaderInt;
        result.ExposureGained = ComputeExposureGain(leaderInt, targetInt);
        state.CognitiveExposure += result.ExposureGained;

        ProcessLanguageExposure(state, leader, defeated, result.NewlyLearnedLanguages);

        while (state.CognitiveExposure >= ComputeThreshold(state.CurrentINT))
        {
            float threshold = ComputeThreshold(state.CurrentINT);
            state.CurrentINT += 1;
            state.CognitiveExposure = Mathf.Max(0f, state.CognitiveExposure - threshold);
        }

        result.IntelligenceAfter = state.CurrentINT;
        result.Applied = true;
        return result;
    }

    private static string BuildOwnerId(CreatureStats owner)
    {
        if (!IntelligenceSystemRuntime.Tuning.AllowNonPlayerEntities && !owner.IsInGroup("PlayerTeam"))
        {
            return "PlayerLeader";
        }

        return PartyRosterManager.BuildRosterId(owner);
    }

    private static float ComputeExposureGain(int playerInt, int targetInt)
    {
        int difference = targetInt - playerInt;
        if (difference <= 0)
        {
            return 0f;
        }

        int clampedDifference = Mathf.Min(difference, IntelligenceSystemRuntime.Tuning.ExposureClampMaxDifference);
        float gain = clampedDifference * targetInt;

        if (difference == 1)
        {
            gain *= IntelligenceSystemRuntime.Tuning.NearParityExposureMultiplier;
        }

        return Mathf.Max(0f, gain);
    }

    private static float ComputeLanguageThreshold(int knownLanguagesCount)
    {
        return IntelligenceSystemRuntime.Tuning.LanguageBaseThreshold +
               (IntelligenceSystemRuntime.Tuning.LanguageScalingFactor * Mathf.Max(0, knownLanguagesCount));
    }

    private static void ProcessLanguageExposure(IntelligenceProfileState state, CreatureStats leader, CreatureStats defeated, List<Language> learnedNow)
    {
        if (defeated?.Template?.RacialLanguages == null)
        {
            return;
        }

        if (state.CurrentINT < IntelligenceSystemRuntime.Tuning.LanguageLearningIntMinimum)
        {
            return;
        }

        int targetInt = defeated.Template.Intelligence;
        if (targetInt < 6)
        {
            return;
        }

        foreach (Language language in defeated.Template.RacialLanguages)
        {
            if (state.LanguagesKnown.Contains(language))
            {
                continue;
            }

            float gain = targetInt * IntelligenceSystemRuntime.Tuning.LanguageExposureMultiplier;
            if (state.CurrentINT < IntelligenceSystemRuntime.Tuning.LanguageReducedGainIntMinimum)
            {
                gain *= IntelligenceSystemRuntime.Tuning.LanguageReducedGainMultiplier;
            }

            state.LanguageExposure.TryGetValue(language, out float current);
            current += gain;
            state.LanguageExposure[language] = current;

            float threshold = ComputeLanguageThreshold(state.LanguagesKnown.Count);
            if (current < threshold)
            {
                continue;
            }

            state.LanguagesKnown.Add(language);
            state.LanguageExposure.Remove(language);
            learnedNow.Add(language);

            if (leader?.LearnedLanguages != null && !leader.LearnedLanguages.Contains(language))
            {
                leader.LearnedLanguages.Add(language);
            }
        }
    }
}
