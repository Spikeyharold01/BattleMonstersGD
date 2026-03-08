using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_FrightfulPresence.cs
// PURPOSE: Resolves the Hit Dice checks, Panicked/Shaken thresholding, and 24-hour immunity logic
//          for the Frightful Presence special quality.
// =================================================================================================
[GlobalClass]
public partial class Effect_FrightfulPresence : AbilityEffectComponent
{
    [ExportGroup("Fear Conditions")]
    [Export] public StatusEffect_SO ShakenEffectTemplate;
    [Export] public StatusEffect_SO PanickedEffectTemplate;

    [ExportGroup("Rules & Thresholds")]
    [Export]
    [Tooltip("Creatures with this many Hit Dice or fewer become Panicked instead of Shaken. (Default: 4)")]
    public int PanicHDThreshold = 4;

    [Export]
    [Tooltip("Duration of the fear effect. Standard is 5d6.")]
    public string DurationFormula = "5d6";

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (ShakenEffectTemplate == null || PanickedEffectTemplate == null)
        {
            GD.PrintErr("Effect_FrightfulPresence is missing its StatusEffect templates.");
            return;
        }

        int casterHD = CreatureRulesUtility.GetHitDiceCount(context.Caster);
        string immunityEffectName = $"Immune to Frightful Presence ({context.Caster.Name})";

        foreach (var target in context.AllTargetsInAoE)
        {
            if (target == context.Caster) continue;
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;

            // Rule: This ability affects only opponents with fewer Hit Dice than the creature has.
            int targetHD = CreatureRulesUtility.GetHitDiceCount(target);
            if (targetHD >= casterHD)
            {
                GD.Print($"{target.Name} ({targetHD} HD) is unaffected by {context.Caster.Name}'s Frightful Presence ({casterHD} HD).");
                continue;
            }

            // Rule: An opponent that succeeds on the saving throw is immune to that same creature’s frightful presence for 24 hours.
            if (target.MyEffects.HasEffect(immunityName: immunityEffectName))
            {
                continue;
            }

            // Safeguard: If they are already suffering from our fear, don't make them save again
            if (target.MyEffects.ActiveEffects.Any(e => e.SourceCreature == context.Caster && (e.EffectData.EffectName == ShakenEffectTemplate.EffectName || e.EffectData.EffectName == PanickedEffectTemplate.EffectName)))
            {
                continue;
            }

            bool saved = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
            if (saved)
            {
                GD.PrintRich($"[color=green]{target.Name} resists Frightful Presence! Immune for 24 hours.[/color]");
                var immunity = new StatusEffect_SO 
                { 
                    EffectName = immunityEffectName, 
                    DurationInRounds = 14400, // 24 Hours
                    Description = "Immune to this creature's Frightful Presence."
                };
                target.MyEffects.AddEffect(immunity, context.Caster);
            }
            else
            {
                int durationRounds = Mathf.Max(1, Dice.Roll(DurationFormula));
                
                if (targetHD <= PanicHDThreshold)
                {
                    GD.PrintRich($"[color=red]{target.Name} ({targetHD} HD) fails save and is PANICKED by Frightful Presence![/color]");
                    var panicInstance = (StatusEffect_SO)PanickedEffectTemplate.Duplicate();
                    panicInstance.DurationInRounds = durationRounds;
                    target.MyEffects.AddEffect(panicInstance, context.Caster, ability);
                }
                else
                {
                    GD.PrintRich($"[color=orange]{target.Name} ({targetHD} HD) fails save and is SHAKEN by Frightful Presence![/color]");
                    var shakenInstance = (StatusEffect_SO)ShakenEffectTemplate.Duplicate();
                    shakenInstance.DurationInRounds = durationRounds;
                    target.MyEffects.AddEffect(shakenInstance, context.Caster, ability);
                }
            }
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        // Because Frightful Presence is usually triggered automatically via the Controller, 
        // the AI doesn't "choose" to cast it as an action. However, if granted by a spell 
        // (like Frightful Aspect), we give it a high control value.
        float score = 0f;
        int casterHD = CreatureRulesUtility.GetHitDiceCount(context.Caster);
        
        foreach (var target in context.AllTargetsInAoE)
        {
            if (target == context.Caster) continue;
            if (CreatureRulesUtility.GetHitDiceCount(target) >= casterHD) continue;
            if (target.MyEffects.HasEffect($"Immune to Frightful Presence ({context.Caster.Name})")) continue;
            if (target.HasImmunity(ImmunityType.MindAffecting) || target.HasImmunity(ImmunityType.Fear)) continue;
            
            score += 40f; 
        }
        return score;
    }
}