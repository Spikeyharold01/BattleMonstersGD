using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Simulation-only encounter power scoring.
/// 
/// Expected output:
/// - AI-vs-AI background simulations can estimate matchup outcomes with recruitment intelligence and synergy.
/// - No creature stat block is mutated; this score exists only for probabilistic resolution.
/// </summary>
public static class EncounterPowerScoring
{
    public sealed class EffectiveEncounterPowerScore
    {
        public float BaseCR;
        public int RecruitCount;
        public float SynergyBonus;
        public float IntelligenceAdvantageScore;
        public float CorruptionAdjustmentScore;
        public float FinalScore;
    }

    public static EffectiveEncounterPowerScore BuildScore(float baseCr, RecruitmentManager recruitmentManager, CreatureTemplate_SO anticipatedEnemyTemplate, IEnumerable<CreatureStats> alliedRoster = null, IEnumerable<CreatureStats> enemyRoster = null)
    {
        TacticalModifierBundle intelligence = recruitmentManager?.ResolveIntelligenceModifiers(anticipatedEnemyTemplate) ?? new TacticalModifierBundle();

        float synergyBonus = 0f;
        float intelligenceScore = intelligence.PerceptionBonus * 0.35f
                                 + intelligence.InitiativeBonus * 0.45f
                                 + intelligence.AmbushResistanceBonus * 6f
                                 + intelligence.TrapDamageReduction * 2f;

        float corruptionAdjustment = ComputeCorruptionAdjustment(alliedRoster, enemyRoster);

        return new EffectiveEncounterPowerScore
        {
            BaseCR = baseCr,
            RecruitCount = 0,
            SynergyBonus = synergyBonus,
            IntelligenceAdvantageScore = intelligenceScore,
            CorruptionAdjustmentScore = corruptionAdjustment,
            FinalScore = baseCr + synergyBonus + intelligenceScore + corruptionAdjustment
        };
    }

    private static float ComputeCorruptionAdjustment(IEnumerable<CreatureStats> allies, IEnumerable<CreatureStats> enemies)
    {
        float allySeverity = allies?.Sum(c => c?.GetCorruptionMetrics().CorruptionSeverityScore ?? 0f) ?? 0f;
        float enemySeverity = enemies?.Sum(c => c?.GetCorruptionMetrics().CorruptionSeverityScore ?? 0f) ?? 0f;

        return (enemySeverity - allySeverity) * 10f;
    }
}
