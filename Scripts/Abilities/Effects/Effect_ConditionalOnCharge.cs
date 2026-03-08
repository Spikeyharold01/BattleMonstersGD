using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: BattleMonsters\Scripts\Abilities\Effects\Effect_ConditionalOnCharge.cs
// PURPOSE: Executes child effects ONLY if the caster is currently Charging.
//          Used for abilities like "Powerful Charge" that add extra effects.
// =================================================================================================
[GlobalClass]
public partial class Effect_ConditionalOnCharge : AbilityEffectComponent
{
    [Export]
    [Tooltip("The effects to execute if the condition is met.")]
    public Godot.Collections.Array<AbilityEffectComponent> EffectsToExecute = new();

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        // Check if the caster has the 'Charging' condition.
        // This condition is applied by AIAction_Charge / Player charge logic before the attack.
        if (!context.Caster.MyEffects.HasCondition(Condition.Charging))
        {
            // Not charging, so we do nothing.
            return;
        }

        GD.Print($"{context.Caster.Name} is charging! Triggering Powerful Charge effects.");

        // Execute all child effects
        foreach (var effect in EffectsToExecute)
        {
            effect.ExecuteEffect(context, ability, targetSaveResults);
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        // If the AI isn't planning a charge (doesn't have condition yet), 
        // we assume it MIGHT charge if evaluating this as part of a charge action.
        // However, standard Ability evaluation happens before action selection.
        // We return the sum of child values to encourage the AI to value this option.
        float total = 0;
        foreach (var effect in EffectsToExecute)
        {
            total += effect.GetAIEstimatedValue(context);
        }
        return total;
    }
}