using Godot;
using System.Linq;
// =================================================================================================
// FILE: AIScoringEngine.cs (GODOT VERSION)
// PURPOSE: A centralized, static class for scoring the tactical value of abilities.
// ATTACH TO: Do not attach (Static Class).
// =================================================================================================
public static class AIScoringEngine
{
public static float ScoreTacticalTag(TacticalTag_SO tag, EffectContext context, int validTargetCount)
{
if (tag == null || validTargetCount == 0) return 0;

// Note: AIController is a child node. We access it via GetNodeOrNull.
    var aiController = context.Caster.GetNodeOrNull<AIController>("AIController");
    if (aiController == null) return 0;

    float score = tag.BaseValue * validTargetCount;
    float personalityMultiplier = 1.0f;
	
    if (context.Caster.MyEffects.HasCondition(Condition.ArcaneSight))
    {
        if (tag.AbilityName.Contains("Dispel Magic") && context.PrimaryTarget != null)
        {
            if (CombatMemory.GetKnownAuraStrength(context.PrimaryTarget) >= AuraStrength.Moderate)
            {
                score += 150f; 
                score *= 2.0f;
            }
        }
    }

    switch (tag.Role)
    {
        case TacticalRole.Buff_Offensive:
        case TacticalRole.Buff_Defensive:
            personalityMultiplier = aiController.Personality.W_Defensive / 100f;
			
            if (context.PrimaryTarget != null && context.PrimaryTarget != context.Caster)
            {
                if (context.PrimaryTarget.Template.BaseAttackBonus >= context.Caster.Template.BaseAttackBonus)
                {
                    score += 50f; 
                }

                float healthPercent = context.PrimaryTarget.CurrentHP / (float)context.PrimaryTarget.Template.MaxHP;
                if (healthPercent < 0.5f)
                {
                    score += 75f; 
                }
            }

            if (TurnManager.Instance.GetCurrentRound() <= 2) score *= 1.5f;
            break;

        case TacticalRole.Debuff_Offensive:
        case TacticalRole.Debuff_Defensive:
            personalityMultiplier = aiController.Personality.W_Strategic / 100f;
            if (context.PrimaryTarget != null && context.PrimaryTarget == CombatMemory.GetHighestThreat())
            {
                score += 50f;
            }
            break;
        
        case TacticalRole.BattlefieldControl:
             personalityMultiplier = aiController.Personality.W_Strategic / 100f;
			 // If the effect applies Fascinated (which breaks on damage), we need specific conditions.
             if (tag.AbilityName.Contains("Enthrall") || tag.AbilityName.Contains("Fascinate"))
             {
                 var visibleEnemies = AISpatialAnalysis.FindVisibleTargets(context.Caster);
                 var allies = AISpatialAnalysis.FindAllies(context.Caster);

                 // 1. If I am alone, this spell is useless (I can't attack them without breaking it).
                 if (allies.Count == 0)
                 {
                     return -1f; // Do NOT cast.
                 }

                 // 2. If there is only 1 enemy, just kill them. Don't stall.
                 if (visibleEnemies.Count <= 1)
                 {
                     return 5f; // Very low score.
                 }

                 // 3. If I can hit MORE enemies than I have allies, this is great.
                 // (e.g. 2 Allies vs 6 Enemies -> Stall 4 of them).
                 if (validTargetCount > allies.Count)
                 {
                     score *= 2.0f; // Boost significantly.
                 }
             }
             if (tag.AbilityName.Contains("Fog") || tag.AbilityName.Contains("Mist"))
             {
                 if (context.Caster.Template.HasMistsight)
                 {
                     if (context.AimPoint.DistanceTo(context.Caster.GlobalPosition) < 10f)
                     {
                         score += 200f; 
                     }
                 }
             }
			 
             if (tag.AbilityName.Contains("Sleet") || tag.AbilityName.Contains("Blizzard") || tag.AbilityName.Contains("Ice"))
             {
                 if (context.Caster.Template.HasSnowsight)
                 {
                     score += 150f;
                     
                     if (context.AimPoint.DistanceTo(context.Caster.GlobalPosition) < 20f)
                     {
                         score += 50f;
                     }
                 }
             }
             break;

        case TacticalRole.Healing:
            personalityMultiplier = aiController.Personality.W_Defensive / 100f;
            if (context.PrimaryTarget != null)
            {
                if (context.PrimaryTarget.CurrentHP < 0)
                {
                    score *= 5.0f; 
                    score += 200;  
                }
                else
                {
                    float missingHealthPercent = (context.PrimaryTarget.Template.MaxHP - context.PrimaryTarget.CurrentHP) / (float)context.PrimaryTarget.Template.MaxHP;
                    score *= (1.0f + missingHealthPercent * 2.0f); 
                }
            }
            break;
    }

    if (context.Caster != null)
    {
        CorruptionMetrics casterCorruption = context.Caster.GetCorruptionMetrics();
        score *= Mathf.Clamp(1f - casterCorruption.CorruptionSeverityScore * 0.4f, 0.35f, 1f);
    }

    if (context.PrimaryTarget != null)
    {
        CorruptionMetrics targetCorruption = context.PrimaryTarget.GetCorruptionMetrics();
        score *= 1f + Mathf.Clamp(targetCorruption.CorruptionSeverityScore * 0.5f, 0f, 0.6f);
    }

    foreach (var strategy in tag.CounterStrategies)
    {
        switch (strategy)
        {
			case CounterStrategy.Counters_Evil:
                var evilEnemies = AISpatialAnalysis.FindVisibleTargets(context.Caster).Where(e => e.Template.Alignment.Contains("Evil"));
                if (evilEnemies.Any()) score += 50f;
                break;
            
            case CounterStrategy.Counters_MindControl:
                var mindControllers = AISpatialAnalysis.FindVisibleTargets(context.Caster).Where(e => e.Template.KnownAbilities.Any(a => a.DescriptionForTooltip.Contains("mind-affecting")));
                if (mindControllers.Any()) score += 150f; 
                break;
            
            case CounterStrategy.Counters_SummonedCreatures:
                var summonedEnemies = AISpatialAnalysis.FindVisibleTargets(context.Caster).Where(e => e.IsSummoned);
                if (summonedEnemies.Any()) score += 75f;
                break;
            case CounterStrategy.Counters_Fear:
                var allEnemies = AISpatialAnalysis.FindVisibleTargets(context.Caster);
                bool fearEnemyPresent = allEnemies.Any(e => e.Template.KnownAbilities.Any(a => a.AbilityName.Contains("Fear")));
                if (fearEnemyPresent) score += 75f;
                break;
                
            case CounterStrategy.Counters_Darkness:
                GridNode casterNode = GridManager.Instance.NodeFromWorldPoint(context.Caster.GlobalPosition);
                // Light level <= 0 logic from GridManager
                if (GridManager.Instance.GetEffectiveLightLevel(casterNode) <= 0 && casterNode.lightInfluences.Any(i => i.IntensityChange < 0))
                {
                    score += 200f;
                }
                break;
				 case CounterStrategy.Counters_NormalVision:
                if (!context.Caster.Template.HasDarkvision) break; 
                
                var blindableEnemies = context.AllTargetsInAoE
                    .Where(e => e.IsInGroup("Player") != context.Caster.IsInGroup("Player") && !e.Template.HasDarkvision)
                    .ToList();
                
                var blindedAllies = context.AllTargetsInAoE
                    .Where(a => a.IsInGroup("Player") == context.Caster.IsInGroup("Player") && !a.Template.HasDarkvision)
                    .ToList();

                score += (blindableEnemies.Count * 100f) - (blindedAllies.Count * 150f);
                break;

            case CounterStrategy.Exploits_LightSensitivity:
                var lightSensitiveEnemies = context.AllTargetsInAoE.Where(t => t.Template.HasLightSensitivity).ToList();
                if (lightSensitiveEnemies.Any())
                {
                    score += 100f * lightSensitiveEnemies.Count + aiController.Personality.B_ExploitWeakness;
                }
                break;
        }
    }
    
    // This block was trailing in the provided snippet outside the switch. 
    // Logic suggests it belongs in the main scoring flow or specific strategy check.
    // Assuming it's a general check like Mistsight synergy above.
    if (tag.AbilityName.Contains("Darkness") || tag.AbilityName.Contains("Shadow"))
     {
         if (context.Caster.Template.HasSeeInDarkness)
         {
             if (context.AimPoint.DistanceTo(context.Caster.GlobalPosition) < 10f)
             {
                 score += 250f; 
             }
         }
     }

    return score * personalityMultiplier;
}

/// <summary>
/// Shared tactical equation used by systemic action evaluators.
///
/// Expected output:
/// - Encourages actions that increase enemy hazard exposure while protecting self.
/// - Keeps scoring generic so behaviors emerge from measured battlefield context.
/// </summary>
public static float ScoreTacticalAction(
    float positionalAdvantage,
    float enemyHazardExposure,
    float selfHazardExposure,
    float awarenessEdge,
    float moraleImpact,
    float actionSuccessProbability)
{
    return positionalAdvantage +
           enemyHazardExposure -
           selfHazardExposure +
           awarenessEdge +
           moraleImpact +
           actionSuccessProbability;
}

}
