
using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: Effect_DamageOverTime.cs (GODOT VERSION)
// PURPOSE: A reusable effect component that applies a damage-over-time status effect.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_DamageOverTime : AbilityEffectComponent
{
[ExportGroup("DoT Configuration")]
[Export] public DamageInfo DamagePerRound;
[Export] public int DurationInRounds = 3;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    foreach (var target in context.AllTargetsInAoE)
    {
        if (targetSaveResults.ContainsKey(target) && targetSaveResults[target]) continue;
        
        // Check immunity
        if (target.Template.Immunities != null && target.Template.Immunities.Any(i => i.Equals(DamagePerRound.DamageType, System.StringComparison.OrdinalIgnoreCase)))
        {
            GD.Print($"{target.Name} is immune to {DamagePerRound.DamageType} and the Damage over Time effect is negated.");
            continue; 
        }
        
        var dotEffect = new StatusEffect_SO();
        dotEffect.EffectName = $"{DamagePerRound.DamageType} DoT";
        dotEffect.DamagePerRound = Dice.Roll(DamagePerRound.DiceCount, DamagePerRound.DieSides) + DamagePerRound.FlatBonus;
        dotEffect.DamageType = DamagePerRound.DamageType;
        dotEffect.DurationInRounds = this.DurationInRounds;
        
        GD.Print($"{target.Name} is now taking {dotEffect.DamagePerRound} {dotEffect.DamageType} damage per round for {dotEffect.DurationInRounds} rounds.");
        target.MyEffects.AddEffect(dotEffect, context.Caster, ability);
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    float avgDamage = (DamagePerRound.DiceCount * (DamagePerRound.DieSides / 2f + 0.5f)) + DamagePerRound.FlatBonus;
    return avgDamage * DurationInRounds * context.AllTargetsInAoE.Count;
}
}