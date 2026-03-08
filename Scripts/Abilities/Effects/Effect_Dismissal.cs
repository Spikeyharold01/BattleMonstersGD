using Godot;
using System;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_Dismissal.cs
// PURPOSE: Temporarily banishes extraplanar creatures for 1d4 rounds.
// =================================================================================================
[GlobalClass]
public partial class Effect_Dismissal : AbilityEffectComponent
{
    [Export]
    [Tooltip("Optional fallback effect if the target succeeds the save.")]
    public StatusEffect_SO OnSuccessfulSaveEffect;

    [Export]
    [Tooltip("Chance (0-1) of sending the creature to a different plane; this applies the 10% HP reduction on return.")]
    public float WrongPlaneChance = 0.20f;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        var caster = context.Caster;
        var target = context.PrimaryTarget;

        if (target == null) return;

        if (!CreatureRulesUtility.IsExtraplanar(target))
        {
            GD.Print($"{ability.AbilityName} has no effect on {target.Name}; target is not extraplanar.");
            return;
        }

        bool saved = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
        if (saved)
        {
            GD.Print($"{target.Name} resists {ability.AbilityName}.");
            if (OnSuccessfulSaveEffect != null)
            {
                target.MyEffects.AddEffect((StatusEffect_SO)OnSuccessfulSaveEffect.Duplicate(), caster, ability);
            }
            return;
        }

        int banishRounds = Dice.Roll(1, 4);
        bool wrongPlane = GD.Randf() < Mathf.Clamp(WrongPlaneChance, 0f, 1f);

        var controller = target.GetNodeOrNull<TemporaryBanishmentController>("TemporaryBanishmentController");
        if (controller == null)
        {
            controller = new TemporaryBanishmentController();
            controller.Name = "TemporaryBanishmentController";
            target.AddChild(controller);
        }

        controller.ApplyTemporaryBanishment(banishRounds, wrongPlane);
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        var caster = context.Caster;
        var target = context.PrimaryTarget;
        if (caster == null || target == null) return 0f;
        if (!CreatureRulesUtility.IsExtraplanar(target)) return -1f;
		
        float value = 120f;

        float casterHpPercent = caster.CurrentHP / (float)Math.Max(1, caster.Template.MaxHP);
        if (casterHpPercent < 0.5f) value += 60f;
        if (casterHpPercent < 0.3f) value += 80f;

        var enemies = AISpatialAnalysis.FindVisibleTargets(caster);
        if (enemies.Count >= 2) value += 60f;

        bool targetIsHighThreat = target.Template.BaseAttackBonus >= caster.Template.BaseAttackBonus
                                  || target.Template.KnownAbilities.Count > caster.Template.KnownAbilities.Count;
        if (targetIsHighThreat) value += 40f;

        return value;
    }
}
