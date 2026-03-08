using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_SelfBanishment.cs (GODOT VERSION)
// PURPOSE: Handles "Plane Shift" logic for the combat simulator.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_SelfBanishment : AbilityEffectComponent
{
[ExportGroup("Offensive Flavor (Optional)")]
[Export]
[Tooltip("If targeting an enemy, apply this effect instead of banishing them (e.g. Staggered). Leave null for harmless failure.")]
public StatusEffect_SO OffensiveFailureEffect;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    var caster = context.Caster;
    var target = context.PrimaryTarget;

    if (target == null) return;

    // --- 1. SELF-TARGETING (FORFEIT) ---
	if (target.MyEffects.HasCondition(Condition.SoulBound))
    {
        GD.Print("Plane Shift failed: Target is Soul Bound.");
        return;
    }
    if (target == caster)
    {
        GD.PrintRich($"<color=magenta>{caster.Name} uses Plane Shift to flee the battle!</color>");
        GD.PrintRich($"<color=red>MATCH ENDED: {caster.Name} has forfeited.</color>");

        target.TakeDamage(99999, "Forfeit", caster); 
    }
    // --- 2. OFFENSIVE USE (BLOCKED) ---
    else
    {
        GD.PrintRich($"<color=gray>{caster.Name} attempts to Plane Shift {target.Name}, but the arena is Planar Anchored.</color>");
        
        bool saved = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
        
        if (saved)
        {
            GD.Print($"{target.Name} resists the planar energy.");
        }
        else if (OffensiveFailureEffect != null)
        {
            GD.Print($"{target.Name} is briefly disoriented by the failed shift.");
            var debuff = (StatusEffect_SO)OffensiveFailureEffect.Duplicate();
            target.MyEffects.AddEffect(debuff, caster, ability);
        }
        else
        {
            GD.Print("The spell fails harmlessly.");
        }
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var caster = context.Caster;
    var target = context.PrimaryTarget;

    // --- OFFENSIVE SCORING ---
    if (target != caster)
    {
        if (OffensiveFailureEffect != null)
        {
            return 40f; 
        }
        return 0f;
    }

    // --- DEFENSIVE SCORING (FORFEIT) ---
    float hpPercent = (float)caster.CurrentHP / caster.Template.MaxHP;
    bool criticalHealth = hpPercent < 0.15f; 

    var allies = AISpatialAnalysis.FindAllies(caster);
    bool alone = allies.Count == 0;

    var enemies = AISpatialAnalysis.FindVisibleTargets(caster);
    bool outnumbered = enemies.Count >= 2;

    if (criticalHealth && alone && outnumbered)
    {
        return 500f; 
    }

    return -1f; 
}
}