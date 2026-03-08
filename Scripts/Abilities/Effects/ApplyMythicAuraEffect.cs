using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: ApplyMythicAuraEffect.cs (GODOT VERSION)
// PURPOSE: An effect component for applying a StatusEffect_SO to all valid targets in an area (Mythic).
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class ApplyMythicAuraEffect : MythicAbilityEffectComponent
{
[Export]
[Tooltip("The status effect to apply to all creatures in the aura.")]
public StatusEffect_SO EffectToApply;

[Export]
[Tooltip("(Optional) This aura will only apply to targets that meet these conditions.")]
public TargetFilter_SO AuraTargetFilter;

public override void ExecuteMythicEffect(EffectContext context, Ability_SO ability)
{
    // Find all creatures within the ability's AoE
    Vector3 auraCenter = context.TargetObject != null ? (context.TargetObject as Node3D).GlobalPosition : context.AimPoint;
    
    var allCreaturesInAura = AoEHelper.GetTargetsInBurst(auraCenter, ability.AreaOfEffect, "Player");
    var enemies = AoEHelper.GetTargetsInBurst(auraCenter, ability.AreaOfEffect, "Enemy");
    foreach(var e in enemies) allCreaturesInAura.Add(e); 
    
    foreach (var target in allCreaturesInAura)
    {
        if (AuraTargetFilter != null && !AuraTargetFilter.IsTargetValid(context.Caster, target))
        {
            continue;
        }
        
        GD.Print($"{ability.AbilityName} aura applies '{EffectToApply.EffectName}' to {target.Name}.");
        target.MyEffects.AddEffect((StatusEffect_SO)EffectToApply.Duplicate(), context.Caster);
    }
}

public override string GetMythicDescription()
{
    return $"Creates an aura that applies '{EffectToApply.EffectName}' to creatures in the area.";
}
}