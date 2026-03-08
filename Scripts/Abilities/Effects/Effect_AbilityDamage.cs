using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_AbilityDamage.cs (GODOT VERSION)
// PURPOSE: Effect dealing ability score damage on failed save.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class AbilityDamageInfo : Resource
{
[Export] public AbilityScore StatToDamage;
[Export] public int DiceCount = 1;
[Export] public int DieSides = 6;
[Export] public bool IsDrain = false;
}
[GlobalClass]
public partial class Effect_AbilityDamage : AbilityEffectComponent
{
[Export] public Godot.Collections.Array<AbilityDamageInfo> AbilityDamages = new();

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    foreach (var target in context.AllTargetsInAoE)
    {
        if (targetSaveResults.ContainsKey(target) && targetSaveResults[target]) continue;
        
        foreach (var damageInfo in AbilityDamages)
        {
            int damageAmount = Dice.Roll(damageInfo.DiceCount, damageInfo.DieSides);
            string typeString = damageInfo.IsDrain ? "drain" : "damage";
            GD.Print($"{target.Name} takes {damageAmount} point of {damageInfo.StatToDamage} {typeString}.");
            target.TakeAbilityDamage(damageInfo.StatToDamage, damageAmount, damageInfo.IsDrain);
        }
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    // Damaging a core stat is very powerful.
    return 80f * context.AllTargetsInAoE.Count;
}
}