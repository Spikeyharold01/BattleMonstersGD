using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: BattleMonsters\Scripts\Abilities\Effects\Effect_ApplyBreakableStatus.cs
// PURPOSE: Applies a status effect that has HP and can be broken.
//          Logic for tracking HP is handled by a generic controller attached to the victim.
// =================================================================================================
[GlobalClass]
public partial class Effect_ApplyBreakableStatus : AbilityEffectComponent
{
    [Export] public StatusEffect_SO EffectToApply;
    
    [ExportGroup("Break Mechanics")]
    [Export] public int HP_PerCasterLevel = 1;
    [Export] public int Base_HP = 0;
    [Export] public Godot.Collections.Array<string> VulnerableDamageTypes = new(); // "Fire", "Bludgeoning"
    [Export] public bool AllowStrengthCheck = true;
    
    [ExportGroup("Conditions")]
    [Export] public bool ApplyOnlyOnFailedSave = true;
    [Export] public bool RequireMythic = false;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (RequireMythic && !context.IsMythicCast) return;

        foreach (var target in context.AllTargetsInAoE)
        {
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;

            bool saved = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
            if (ApplyOnlyOnFailedSave && saved) continue;

            // Apply the Status Effect
            var instance = (StatusEffect_SO)EffectToApply.Duplicate();
            target.MyEffects.AddEffect(instance, context.Caster, ability);

            // Calculate HP
            int hp = Base_HP + (HP_PerCasterLevel * context.Caster.Template.CasterLevel);
            int dc = ability.SavingThrow.BaseDC;

            // Attach Generic Controller
            var ctrl = new BreakableEffectController();
            ctrl.Name = $"Breakable_{EffectToApply.EffectName}";
            target.AddChild(ctrl);
            ctrl.Initialize(EffectToApply.EffectName, hp, dc, VulnerableDamageTypes, AllowStrengthCheck);
            
            GD.Print($"{target.Name} is affected by {EffectToApply.EffectName} (HP: {hp}).");
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        return 30f;
    }
}