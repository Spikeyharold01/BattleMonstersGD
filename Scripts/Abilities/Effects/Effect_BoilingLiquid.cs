using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_BoilingLiquid.cs (GODOT VERSION)
// PURPOSE: Handles Boiling Liquid effect damage.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_BoilingLiquid : AbilityEffectComponent
{
[Export]
[Tooltip("The damage dealt by a splash or single-target hit.")]
public DamageInfo SplashDamage;

[Export]
[Tooltip("The massive damage dealt by total immersion (e.g., in a geyser).")]
public DamageInfo ImmersionDamage;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    foreach (var target in context.AllTargetsInAoE)
    {
        if (targetSaveResults.ContainsKey(target) && targetSaveResults[target]) continue;
        
        // For now, we assume a breath weapon is not "total immersion".
        int damage = Dice.Roll(SplashDamage.DiceCount, SplashDamage.DieSides) + SplashDamage.FlatBonus;
        
        GD.Print($"{target.Name} is hit by boiling liquid and takes {damage} scalding damage.");
        // We can treat "scalding" as fire damage for resistance/immunity purposes.
        target.TakeDamage(damage, "Fire", context.Caster, null, null);
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    float avgDamage = (SplashDamage.DiceCount * (SplashDamage.DieSides / 2f + 0.5f)) + SplashDamage.FlatBonus;
    return avgDamage * context.AllTargetsInAoE.Count;
}
}