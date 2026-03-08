using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_GrantTempHpToCaster.cs
// PURPOSE: Generic effect that grants Temporary HP to the caster when the ability executes.
//          Highly reusable for life-leeching attacks, Vampiric Touch, or self-buffs.
// =================================================================================================
[GlobalClass]
public partial class Effect_GrantTempHpToCaster : AbilityEffectComponent
{
    [Export]
    [Tooltip("The flat amount of Temporary Hit Points granted to the caster.")]
    public int FlatAmount = 5;

    [Export]
    [Tooltip("If true, scales the Temp HP by rolling dice instead of using the flat amount.")]
    public bool UseDice = false;

    [Export] public int DiceCount = 1;
    [Export] public int DieSides = 8;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (context.Caster == null) return;

        // To prevent gaining HP multiple times on AoE spells, we only trigger once per execution.
        int amountToGrant = UseDice ? Dice.Roll(DiceCount, DieSides) + FlatAmount : FlatAmount;

        context.Caster.AddTemporaryHP(amountToGrant);
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (context.Caster == null) return 0f;

        // If the caster already has a lot of Temp HP, getting more isn't very valuable (Temp HP doesn't stack).
        if (context.Caster.TemporaryHP >= FlatAmount) return 0f;

        // Otherwise, evaluate it as pure healing.
        float expectedHp = UseDice ? (DiceCount * (DieSides / 2f + 0.5f)) + FlatAmount : FlatAmount;
        return expectedHp * 2.0f; // Temporary HP is moderately valuable for survival
    }
}