using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: FleeEffect.cs (GODOT VERSION)
// PURPOSE: An effect component for causing creatures to flee, as if panicked.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class FleeEffect : AbilityEffectComponent
{
// The targetFilter for this component should be configured to only allow Undead creatures.

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    foreach (var target in context.AllTargetsInAoE)
    {
        if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target))
        {
            continue;
        }

        bool didSave = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
        if (didSave)
        {
            GD.Print($"{target.Name} resists the Turn Undead effect.");
            continue;
        }

        GD.PrintRich($"<color=orange>{target.Name} fails its save and is panicked by Turn Undead!</color>");
        var panickedEffect = new StatusEffect_SO();
        panickedEffect.EffectName = "Turned (Panicked)";
        panickedEffect.DurationInRounds = 10; 
        panickedEffect.ConditionApplied = Condition.Panicked;
        
        panickedEffect.AllowsRecurringSave = true;
        panickedEffect.RecurringSaveType = SaveType.Will;
        panickedEffect.RecurringSaveRequiresIntelligence = true;

        int saveDC = ability.SavingThrow.BaseDC;
        if (ability.SavingThrow.IsDynamicDC)
        {
            int statMod = (ability.SavingThrow.DynamicDCStat == AbilityScore.Charisma) ? context.Caster.ChaModifier : 0;
            saveDC = 10 + Mathf.FloorToInt(context.Caster.Template.CasterLevel / 2f) + statMod;
        }
  
        
        target.MyEffects.AddEffect(panickedEffect, context.Caster, ability, saveDC);
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var validTargets = context.AllTargetsInAoE
        .Where(t => TargetFilter != null && TargetFilter.IsTargetValid(context.Caster, t))
        .ToList();

    if (!validTargets.Any()) return 0f;

    float totalScore = 0;
    float baseValuePerTarget = 150f; 

    foreach (var target in validTargets)
    {
        int predictedWillSave = target.GetWillSave(context.Caster); 
        int dc = 10 + Mathf.FloorToInt(context.Caster.Template.CasterLevel / 2f) + context.Caster.ChaModifier;
        
        int rollNeeded = dc - predictedWillSave;
        float chanceToFailSave = Mathf.Clamp((rollNeeded - 1f) / 20f, 0f, 1f);
        
        totalScore += baseValuePerTarget * chanceToFailSave;
    }

    if (validTargets.Count > 1)
    {
        totalScore *= (1 + (validTargets.Count * 0.5f));
    }

    return totalScore;
}
}