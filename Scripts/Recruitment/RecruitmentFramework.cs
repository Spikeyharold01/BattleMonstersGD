using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Role tags used by recruitment records.
/// 
/// Expected output:
/// - Designers and systems can classify recruits by battlefield function instead of raw stat quality.
/// - Synergy rules can scale through coordination identity (scout, swarm, support, etc.) without CR-breaking growth.
/// </summary>
public enum RecruitmentRoleTag
{
    None,
    Scout,
    Swarm,
    Heavy,
    Support,
    Flying,
    Aquatic
}

/// <summary>
/// Lightweight recruit record intentionally storing only identity and tactical metadata.
/// 
/// Expected output:
/// - Recruitment systems can persist and query companions without cloning CreatureStats.
/// - Save payloads remain small because only minimal fields are retained.
/// </summary>
public sealed class RecruitedCreatureRecord
{
    public string CreatureId;
    public int BaseCR;
    public RecruitmentRoleTag RoleTag;
    public float LoyaltyValue;
    public float BaseMorale = 0.7f;
    public float CurrentMorale = 0.7f;
    public RecruitmentRelationshipReason RecruitmentReason = RecruitmentRelationshipReason.Coerced;
    public List<string> IntelligenceTagsUnlocked = new();
}

/// <summary>
/// Tactical bonuses produced from recruitment/intelligence systems.
/// 
/// Expected output:
/// - Downstream systems can read one clean bundle for perception/initiative/ambush/trap modifiers.
/// - No direct HP or damage inflation is required to make recruitment feel meaningful.
/// </summary>
public sealed class TacticalModifierBundle
{
    public float PerceptionBonus;
    public float InitiativeBonus;
    public float AmbushResistanceBonus;
    public float TrapDamageReduction;
    public float DetectionRangeBonus;
    public float LineOfSightRangeBonus;
    public float SurpriseChanceReduction;
}

/// <summary>
/// Intelligence profile authored per creature family or recruitment archetype.
/// 
/// Expected output:
/// - Recruits unlock practical knowledge (habitat, tells, weak points) against related enemies.
/// - Combat remains stat-faithful while tactical awareness gets deeper.
/// </summary>
public sealed class CreatureIntelligenceProfile
{
    public string CreatureId;
    public List<string> RelatedEnemyTags = new();
    public List<string> WeaknessTags = new();
    public List<string> HabitatKnowledge = new();
    public List<string> CombatTells = new();
    public List<string> VulnerabilityTriggers = new();
    public TacticalModifierBundle TacticalModifiers = new();
}

/// <summary>
/// Shared recruitment limits used as anti-inflation guardrails.
/// 
/// Expected output:
/// - Any optional stat scaling path is hard-clamped and can never exceed safe bounds.
/// - Team power progression remains ecology-first (coordination and knowledge), not vertical stat creep.
/// </summary>
public static class RecruitmentRules
{
    public const float MaxStatMultiplierFromRecruitment = 1f;
}

/// <summary>
/// Consolidated synergy view calculated from lightweight recruit records.
/// 
/// Expected output:
/// - Travel, arena awareness, and simulation systems can consume one stable summary.
/// - Swarm and mixed-role effects are available without mutating base creature templates.
/// </summary>
public sealed class RecruitmentSynergySnapshot
{
    public int RecruitCount;
    public float SwarmCoordinationScore;
    public bool HasSwarmAttackBonus;
    public bool HasDistractionEffect;
    public TacticalModifierBundle TacticalModifiers = new();

    // Guardrail fields kept explicit so any future stat hooks remain capped.
    public float SafeHpMultiplier = 1f;
    public float SafeDamageMultiplier = 1f;
}

/// <summary>
/// Runtime manager for recruit lifecycle, intelligence unlocks, and tactical synergy exposure.
/// 
/// Expected output:
/// - Add/remove/query operations stay lightweight and deterministic.
/// - Systems can request tactical bonuses without touching CreatureStats internals.
/// </summary>
public sealed class RecruitmentManager
{
    private readonly Dictionary<string, RecruitedCreatureRecord> _recordsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CreatureIntelligenceProfile> _intelligenceProfilesByCreatureId = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _releasedKnowledgeLog = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<RecruitedCreatureRecord> ActiveRecruits => _recordsById.Values;

    /// <summary>
    /// Adds or replaces a recruit record.
    /// </summary>
    public void AddRecruit(RecruitedCreatureRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.CreatureId))
        {
            return;
        }

        _recordsById[record.CreatureId] = record;
    }

    /// <summary>
    /// Convenience helper that builds a minimal record directly from a live creature.
    /// </summary>
    public void AddRecruit(CreatureStats creature, RecruitmentRoleTag roleTag, float loyaltyValue, IEnumerable<string> unlockedIntelligenceTags = null)
    {
        if (creature?.Template == null)
        {
            return;
        }

        AddRecruit(new RecruitedCreatureRecord
        {
            CreatureId = BuildCreatureId(creature),
            BaseCR = creature.Template.ChallengeRating,
            RoleTag = roleTag,
            LoyaltyValue = loyaltyValue,
            BaseMorale = 0.7f,
            CurrentMorale = 0.7f,
            RecruitmentReason = RecruitmentRelationshipReason.Coerced,
            IntelligenceTagsUnlocked = unlockedIntelligenceTags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>()
        });
    }

    /// <summary>
    /// Removes a recruit by record identity.
    /// </summary>
    public bool RemoveRecruit(string creatureId)
    {
        if (string.IsNullOrWhiteSpace(creatureId))
        {
            return false;
        }

        return _recordsById.Remove(creatureId);
    }

    /// <summary>
    /// Removes a recruit using a live creature reference, intended for death handling hooks.
    /// </summary>
    public bool RemoveOnDeath(CreatureStats creature)
    {
        if (creature == null)
        {
            return false;
        }

        return RemoveRecruit(BuildCreatureId(creature));
    }

    /// <summary>
    /// Queries recruits by role for ecology-aware orchestration systems.
    /// </summary>
    public List<RecruitedCreatureRecord> QueryRecruitsByRole(RecruitmentRoleTag roleTag)
    {
        return _recordsById.Values.Where(record => record.RoleTag == roleTag).ToList();
    }

    /// <summary>
    /// Registers or replaces one creature intelligence profile.
    /// </summary>
    public void RegisterIntelligenceProfile(CreatureIntelligenceProfile profile)
    {
        if (profile == null || string.IsNullOrWhiteSpace(profile.CreatureId))
        {
            return;
        }

        _intelligenceProfilesByCreatureId[profile.CreatureId] = profile;
    }

    /// <summary>
    /// Resolves tactical modifiers unlocked by current recruits against a target creature template.
    /// </summary>
    public TacticalModifierBundle ResolveIntelligenceModifiers(CreatureTemplate_SO targetTemplate)
    {
        TacticalModifierBundle total = new TacticalModifierBundle();
        if (targetTemplate == null)
        {
            return total;
        }

        string targetName = targetTemplate.CreatureName ?? string.Empty;
        string targetType = targetTemplate.Type.ToString();
        HashSet<string> targetTags = new(StringComparer.OrdinalIgnoreCase) { targetName, targetType };

        foreach (RecruitedCreatureRecord recruit in _recordsById.Values)
        {
            if (!_intelligenceProfilesByCreatureId.TryGetValue(recruit.CreatureId, out CreatureIntelligenceProfile profile) || profile == null)
            {
                string creatureFamilyId = recruit.CreatureId?.Split(':')[0] + ":0";
                _intelligenceProfilesByCreatureId.TryGetValue(creatureFamilyId ?? string.Empty, out profile);
            }

            if (profile == null)
            {
                continue;
            }

            if (!profile.RelatedEnemyTags.Any(tag => targetTags.Contains(tag)))
            {
                continue;
            }

            bool hasUnlockTagGate = recruit.IntelligenceTagsUnlocked.Count == 0 ||
                                    profile.WeaknessTags.Any(tag => recruit.IntelligenceTagsUnlocked.Contains(tag, StringComparer.OrdinalIgnoreCase)) ||
                                    profile.HabitatKnowledge.Any(tag => recruit.IntelligenceTagsUnlocked.Contains(tag, StringComparer.OrdinalIgnoreCase)) ||
                                    profile.CombatTells.Any(tag => recruit.IntelligenceTagsUnlocked.Contains(tag, StringComparer.OrdinalIgnoreCase)) ||
                                    profile.VulnerabilityTriggers.Any(tag => recruit.IntelligenceTagsUnlocked.Contains(tag, StringComparer.OrdinalIgnoreCase));

            if (!hasUnlockTagGate)
            {
                continue;
            }

            total.PerceptionBonus += profile.TacticalModifiers.PerceptionBonus;
            total.InitiativeBonus += profile.TacticalModifiers.InitiativeBonus;
            total.AmbushResistanceBonus += profile.TacticalModifiers.AmbushResistanceBonus;
            total.TrapDamageReduction += profile.TacticalModifiers.TrapDamageReduction;
        }

        return total;
    }

    /// <summary>
    /// Builds one merged tactical picture used by travel and arena-aware systems.
    /// </summary>
    public RecruitmentSynergySnapshot BuildSynergySnapshot()
    {
        return SynergyCalculator.Calculate(_recordsById.Values);
    }

    /// <summary>
    /// Creates tiny save-ready payload records.
    /// </summary>
    public List<RecruitedCreatureRecord> ExportLightweightRecords()
    {
        return _recordsById.Values
            .Select(record => new RecruitedCreatureRecord
            {
                CreatureId = record.CreatureId,
                BaseCR = record.BaseCR,
                RoleTag = record.RoleTag,
                LoyaltyValue = record.LoyaltyValue,
                BaseMorale = record.BaseMorale,
                CurrentMorale = record.CurrentMorale,
                RecruitmentReason = record.RecruitmentReason,
                IntelligenceTagsUnlocked = new List<string>(record.IntelligenceTagsUnlocked)
            })
            .ToList();
    }

    /// <summary>
    /// Restores lightweight records into runtime state.
    /// </summary>
    public void ImportLightweightRecords(IEnumerable<RecruitedCreatureRecord> records)
    {
        _recordsById.Clear();

        if (records == null)
        {
            return;
        }

        foreach (RecruitedCreatureRecord record in records)
        {
            AddRecruit(record);
        }
    }


    /// <summary>
    /// Stores released creature family knowledge as a future hook for intelligence growth systems.
    /// </summary>
    public void RecordReleasedKnowledge(CreatureTemplate_SO template)
    {
        if (template == null)
        {
            return;
        }

        _releasedKnowledgeLog.Add(template.CreatureName ?? template.Type.ToString());
    }

    public IReadOnlyCollection<string> GetReleasedKnowledgeLog()
    {
        return _releasedKnowledgeLog;
    }

    public static string BuildCreatureId(CreatureStats creature)
    {
        string templateName = creature?.Template?.CreatureName ?? "UnknownCreature";
        return $"{templateName}:{creature?.GetInstanceId() ?? 0}";
    }
}

/// <summary>
/// Small static access point so combat/travel utility classes can read recruitment state without constructor rewiring.
/// 
/// Expected output:
/// - Legacy static systems (like StealthManager) can still gain tactical intelligence bonuses.
/// - Recruitment remains optional and null-safe in scenes that do not initialize phase services.
/// </summary>
public static class RecruitmentRuntime
{
    public static RecruitmentManager ActiveManager;
}
