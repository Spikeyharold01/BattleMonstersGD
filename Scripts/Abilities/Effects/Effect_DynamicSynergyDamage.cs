using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: Effect_DynamicSynergyDamage.cs
// PURPOSE: A generic payload effect that scales damage based on how many allies nearby meet a specific condition.
//          Used to create "Lightning's Kiss" (Bonus damage per allied Whirlwind nearby).
// =================================================================================================
[GlobalClass]
public partial class Effect_DynamicSynergyDamage : AbilityEffectComponent
{
    [ExportGroup("Base Damage")]
    [Export] public DamageInfo BaseDamage;

    [ExportGroup("Synergy Scaling")]
    [Export]
    [Tooltip("The condition allies must possess to contribute to the synergy (e.g. WhirlwindForm).")]
    public Condition RequiredAllyCondition = Condition.WhirlwindForm;
    
    [Export]
    [Tooltip("The extra damage added PER ALLY that meets the condition.")]
    public DamageInfo ScalingDamagePerAlly;

    [Export] public float SynergyRadius = 50f;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (BaseDamage == null || context.PrimaryTarget == null) return;

        int totalDice = BaseDamage.DiceCount;
        int flatBonus = BaseDamage.FlatBonus;

        // Find allies meeting condition
        var allies = AISpatialAnalysis.FindAllies(context.Caster);
        int synergyCount = 0;

        foreach (var ally in allies)
        {
            if (ally.GlobalPosition.DistanceTo(context.Caster.GlobalPosition) <= SynergyRadius)
            {
                if (ally.MyEffects.HasCondition(RequiredAllyCondition))
                {
                    synergyCount++;
                }
            }
        }

        if (ScalingDamagePerAlly != null)
        {
            totalDice += (ScalingDamagePerAlly.DiceCount * synergyCount);
            flatBonus += (ScalingDamagePerAlly.FlatBonus * synergyCount);
        }

        int finalDamage = Dice.Roll(totalDice, BaseDamage.DieSides) + flatBonus;
        
        bool saved = targetSaveResults.ContainsKey(context.PrimaryTarget) && targetSaveResults[context.PrimaryTarget];
        if (saved && ability.SavingThrow.EffectOnSuccess == SaveEffect.HalfDamage) finalDamage /= 2;

        GD.PrintRich($"[color=yellow]{context.Caster.Name}'s synergy attack hits {context.PrimaryTarget.Name} for {finalDamage} {BaseDamage.DamageType} damage! (Synergy Allies: {synergyCount})[/color]");
        context.PrimaryTarget.TakeDamage(finalDamage, BaseDamage.DamageType, context.Caster);
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        // This is evaluated dynamically as part of the Whirlwind's payload.
        float score = (BaseDamage.DiceCount * (BaseDamage.DieSides / 2f + 0.5f));
        return score * 1.5f;
    }
}