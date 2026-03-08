using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: NonlethalDamageEffect.cs (GODOT VERSION)
// PURPOSE: An effect component for dealing nonlethal damage.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class NonlethalDamageEffect : AbilityEffectComponent
{
[Export] public DamageInfo Damage;
[Export] public SaveEffect EffectOnSave = SaveEffect.HalfDamage;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    foreach (var target in context.AllTargetsInAoE)
    {
        if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;

        int totalDamage = Dice.Roll(Damage.DiceCount, Damage.DieSides) + Damage.FlatBonus;
        bool didSave = targetSaveResults.ContainsKey(target) && targetSaveResults[target];

        if (didSave)
        {
            if (EffectOnSave == SaveEffect.HalfDamage) totalDamage /= 2;
            if (EffectOnSave == SaveEffect.Negates) totalDamage = 0;
        }

        if (totalDamage > 0)
        {
            target.TakeNonlethalDamage(totalDamage);
        }
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    if (context.AllTargetsInAoE.Count == 0) return 0;
    
    float baseValue = 75f; 
    
    float totalScore = 0;
    foreach (var target in context.AllTargetsInAoE)
    {
        if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;

        float avgDamage = (Damage.DiceCount * (Damage.DieSides / 2f + 0.5f)) + Damage.FlatBonus;
        
        float hpRemaining = target.CurrentHP - target.CurrentNonlethalDamage;
        if (avgDamage >= hpRemaining)
        {
            totalScore += baseValue * 2.0f; 
        }
        else
        {
            // Safety div by zero check if hpRemaining <= 0 (already down)
            if (hpRemaining > 0)
                totalScore += baseValue * (avgDamage / hpRemaining);
        }
    }
    
    return totalScore;
}
}