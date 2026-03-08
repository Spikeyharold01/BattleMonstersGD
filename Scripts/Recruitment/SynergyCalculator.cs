using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Legacy compatibility calculator.
///
/// Expected output:
/// - Existing call sites keep working while party-based recruitment owns real power through entity presence.
/// - No hidden combat or travel modifiers are generated from abstract roster counts.
/// </summary>
public static class SynergyCalculator
{
    public static RecruitmentSynergySnapshot Calculate(IEnumerable<RecruitedCreatureRecord> recruits)
    {
        List<RecruitedCreatureRecord> roster = recruits?.ToList() ?? new List<RecruitedCreatureRecord>();
        return new RecruitmentSynergySnapshot
        {
            RecruitCount = roster.Count,
            SwarmCoordinationScore = 0f,
            HasSwarmAttackBonus = false,
            HasDistractionEffect = false,
            TacticalModifiers = new TacticalModifierBundle(),
            SafeHpMultiplier = 1f,
            SafeDamageMultiplier = 1f
        };
    }
}
