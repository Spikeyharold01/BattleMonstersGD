using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_Harvest.cs (GODOT VERSION)
// PURPOSE: Handles the "Harvest" finisher logic.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_Harvest : AbilityEffectComponent
{
[ExportGroup("Prerequisites")]
[Export] public int HpThreshold = 20;
[Export] public int ConThreshold = 4;
[Export]
[Tooltip("The exact name of the status effect applied by Far Reaching.")]
public string StanceEffectName = "Far Reaching Stance";

[ExportGroup("On Success (Harvest)")]
[Export] public int HealAmount = 50;

[ExportGroup("On Failure (Punishment)")]
[Export]
[Tooltip("The effect applied to the harvester if the target saves.")]
public StatusEffect_SO StaggeredEffect;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    CreatureStats caster = context.Caster;
    CreatureStats target = context.PrimaryTarget;

    if (caster == null || target == null) return;

    // --- 1. VALIDATE CONDITIONS ---
    bool inStance = caster.MyEffects.HasEffect(StanceEffectName);
    bool isGrappledByMe = target.CurrentGrappleState != null && target.CurrentGrappleState.Controller == caster;
    int currentCon = target.Template.Constitution - target.ConDamage;
    bool isCritical = target.CurrentHP <= HpThreshold || currentCon <= ConThreshold;

    if (!inStance || !isGrappledByMe || !isCritical)
    {
        GD.PrintRich($"<color=red>Harvest Failed Pre-Check:</color> Stance: {inStance}, Grappled: {isGrappledByMe}, Critical: {isCritical}");
        return; 
    }

    // --- 2. CHECK SAVE RESULT ---
    bool targetResisted = targetSaveResults.ContainsKey(target) && targetSaveResults[target];

    if (targetResisted)
    {
        GD.PrintRich($"<color=orange>{target.Name} resists the Harvest! {caster.Name} is punished.</color>");

        // 1. End Grapple
        CombatManeuvers.BreakGrapple(target);

        // 2. Stagger Caster
        if (StaggeredEffect != null)
        {
            var punishInstance = (StatusEffect_SO)StaggeredEffect.Duplicate();
            punishInstance.DurationInRounds = 1;
            caster.MyEffects.AddEffect(punishInstance, target); 
        }
    }
    else
    {
        GD.PrintRich($"<color=red>{caster.Name} HARVESTS {target.Name}!</color>");

        // 1. Kill Target
        target.TakeDamage(9999, "Death", caster);

        // 2. Heal Caster
        caster.HealDamage(HealAmount);
        GD.Print($"{caster.Name} absorbs life force and heals {HealAmount} HP.");
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var caster = context.Caster;
    var target = context.PrimaryTarget;
    if (target == null) return 0f;

    // --- CHECK PREREQUISITES FOR AI ---
    bool inStance = caster.MyEffects.HasEffect(StanceEffectName);
    if (!inStance) return 0f; 

    bool isGrappledByMe = target.CurrentGrappleState != null && target.CurrentGrappleState.Controller == caster;
    if (!isGrappledByMe) return 0f; 

    int currentCon = target.Template.Constitution - target.ConDamage;
    bool isCritical = target.CurrentHP <= HpThreshold || currentCon <= ConThreshold;
    
    if (!isCritical) return 0f; 

    return 1100f;
}
}