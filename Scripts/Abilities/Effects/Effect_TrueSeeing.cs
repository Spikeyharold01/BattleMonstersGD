using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: Effect_TrueSeeing.cs (GODOT VERSION)
// PURPOSE: An effect component for casting the True Seeing spell.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_TrueSeeing : AbilityEffectComponent
{
[Export]
[Tooltip("The StatusEffect_SO to apply, which should grant the TrueSeeing condition.")]
public StatusEffect_SO TrueSeeingEffect;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    var target = context.PrimaryTarget;
    if (target == null || TrueSeeingEffect == null) return;
    
    bool didSave = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
    if (didSave)
    {
        GD.Print($"{target.Name} resists the True Seeing effect.");
        return;
    }

    var effectInstance = (StatusEffect_SO)TrueSeeingEffect.Duplicate();
    effectInstance.DurationInRounds = context.Caster.Template.CasterLevel * 10; 
    target.MyEffects.AddEffect(effectInstance, context.Caster, ability);
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var caster = context.Caster;
    var target = context.PrimaryTarget;
    if (target == null) return 0f;

    if (target.MyEffects.HasCondition(Condition.TrueSeeing)) return 0f;

    float score = 0;
    
    // Access AI logic from Caster
    // We use AISpatialAnalysis static helpers for efficiency if available, 
    // or GetComponent logic if `FindVisibleTargets` isn't static. 
    // It WAS static in Part 35.
    var visibleEnemies = AISpatialAnalysis.FindVisibleTargets(caster);
    
    bool hasIllusionistEnemy = visibleEnemies.Any(e => e.Template.KnownAbilities.Any(a => a.School == MagicSchool.Illusion));
    bool hasInvisibleEnemy = visibleEnemies.Any(e => e.MyEffects.HasCondition(Condition.Invisible));

    if (hasIllusionistEnemy)
    {
        score += 150f; 
    }
    if (hasInvisibleEnemy)
    {
        score += 200f; 
    }

    var allies = AISpatialAnalysis.FindAllies(caster);
    allies.Add(caster);
    var highestBabAlly = allies.OrderByDescending(a => a.Template.BaseAttackBonus).FirstOrDefault();
    if (target == highestBabAlly)
    {
        score += 75f;
    }
    
    return score;
}
}