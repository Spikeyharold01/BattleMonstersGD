using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_ViolentThrust.cs (GODOT VERSION)
// PURPOSE: Logic for Telekinesis Violent Thrust scoring/execution.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_ViolentThrust : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
// ... (Implementation for hurling generic projectiles as discussed) ...
GD.Print($"{context.Caster.Name} uses Violent Thrust! (Visual Placeholder)");
}

public override float GetAIEstimatedValue(EffectContext context)
{
    if (context.PrimaryTarget == null) return 0f;

    // Simple AI: Score as a direct damage spell.
    int numProjectiles = Mathf.Min(15, context.Caster.Template.CasterLevel);
    float avgDamagePerProjectile = 3.5f * 2; // 2d6 average
    
    int attackBonus = context.Caster.Template.BaseAttackBonus + context.Caster.IntModifier; 
    
    // Use CombatCalculations (static)
    int targetAC = CombatCalculations.CalculateFinalAC(context.PrimaryTarget, true, 0, context.Caster);
    float chanceToHit = Mathf.Clamp((21f + attackBonus - targetAC) / 20f, 0f, 1f);
    
    return numProjectiles * avgDamagePerProjectile * chanceToHit;
}
}