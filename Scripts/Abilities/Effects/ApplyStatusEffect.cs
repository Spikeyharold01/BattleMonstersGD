using Godot;
using System.Collections.Generic;
using System.Linq;

[GlobalClass]
public partial class ApplyStatusEffect : AbilityEffectComponent
{
    [Export] public StatusEffect_SO EffectToApply;
    [Export] public bool DurationIsPerLevel = false;
	[Export] public bool LimitTargetsByLevel = false;
    [Export] public int DistributionStepRounds = 0; // E.g., 100 for 10-minute intervals

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (EffectToApply == null)
        {
            GD.PrintErr($"ApplyStatusEffect on {ability.AbilityName} is missing its 'EffectToApply' asset!");
            return;
        }
        int maxTargets = LimitTargetsByLevel ? context.Caster.Template.CasterLevel : int.MaxValue;
        int count = 0;
        foreach (var target in context.AllTargetsInAoE)
        {
            if (count >= maxTargets) break;
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;

            int effectSaveDC = 0;
            if (ability.SavingThrow.SaveType != SaveType.None)
            {
                effectSaveDC = ability.SavingThrow.BaseDC;
                if (ability.SavingThrow.IsDynamicDC)
                {
                    int statMod = 0;
                    switch (ability.SavingThrow.DynamicDCStat)
                    {
                        case AbilityScore.Charisma: statMod = context.Caster.ChaModifier; break;
                        case AbilityScore.Wisdom: statMod = context.Caster.WisModifier; break;
                        case AbilityScore.Intelligence: statMod = context.Caster.IntModifier; break;
                    }
                    effectSaveDC = 10 + ability.SpellLevel + statMod + ability.SavingThrow.DynamicDCBonus;
                }
                else if (ability.SavingThrow.IsSpecialAbilityDC)
                {
                    int statMod = 0;
                    switch (ability.SavingThrow.DynamicDCStat)
                    {
                        case AbilityScore.Charisma: statMod = context.Caster.ChaModifier; break;
                        case AbilityScore.Wisdom:   statMod = context.Caster.WisModifier; break;
                        case AbilityScore.Intelligence: statMod = context.Caster.IntModifier; break;
                        case AbilityScore.Constitution: statMod = context.Caster.ConModifier; break;
                        case AbilityScore.Dexterity: statMod = context.Caster.DexModifier; break;
                        case AbilityScore.Strength:  statMod = context.Caster.StrModifier; break;
                    }
                    effectSaveDC = 10 + Mathf.FloorToInt(CreatureRulesUtility.GetHitDiceCount(context.Caster) / 2f) + statMod + ability.SavingThrow.DynamicDCBonus;
                }
            }

            // Lazy Save Check: If target wasn't in original list (e.g. added by SelectRandomTargets), roll save now.
            bool didSave = false;
            if (targetSaveResults.ContainsKey(target))
            {
                didSave = targetSaveResults[target];
            }
            else if (ability.SavingThrow.SaveType != SaveType.None)
            {
                int saveBonus = 0;
                switch (ability.SavingThrow.SaveType)
                {
                    case SaveType.Fortitude: saveBonus = target.GetFortitudeSave(context.Caster, ability); break;
                    case SaveType.Reflex: saveBonus = target.GetReflexSave(context.Caster, ability); break;
                    case SaveType.Will: saveBonus = target.GetWillSave(context.Caster, ability); break;
                }

                if (ability.SaveBonusWhenTargetTypeDiffersFromCaster != 0 && context.Caster?.Template != null && target.Template != null && context.Caster.Template.Type != target.Template.Type)
                {
                    saveBonus += ability.SaveBonusWhenTargetTypeDiffersFromCaster;
                }

                int roll = Dice.Roll(1, 20);
                if (roll + saveBonus >= effectSaveDC) didSave = true;
                GD.Print($"{target.Name} rolls lazy save vs {ability.AbilityName} (DC {effectSaveDC}): {roll + saveBonus} ({(didSave ? "Success" : "Fail")})");
            }

            if (didSave) continue;

            // Duplicate to create a unique runtime instance
            var effectInstance = (StatusEffect_SO)EffectToApply.Duplicate();
            // Resolve dynamic dice modifications immediately upon application
            foreach (var mod in effectInstance.Modifications)
            {
                if (mod.UseDiceForValue)
                {
                    int roll = Dice.Roll(mod.DiceCount, mod.DieSides);

                    if (mod.CannotReduceBelowOne && mod.IsPenalty)
                    {
                        // LOGIC: Check current score of target
                        int currentScore = context.PrimaryTarget.GetAbilityScore(mod.StatToModify);
                        // Cap the penalty so Score - Penalty >= 1
                        int maxPenalty = Mathf.Max(0, currentScore - 1);
                        roll = Mathf.Min(roll, maxPenalty);
                        GD.Print($"Rolled {roll} (Capped at {maxPenalty} to prevent <1).");
                    }

                    mod.ModifierValue = mod.IsPenalty ? -roll : roll;
                    GD.Print($"Final Mod for {mod.StatToModify}: {mod.ModifierValue}");
                }
            }

            int finalDuration = effectInstance.DurationInRounds;
            if (effectInstance.DurationScalesWithLevel)
            {
                finalDuration = context.Caster.Template.CasterLevel * effectInstance.DurationPerLevel;
            }

            if (effectInstance.DurationIsDivided && context.AllTargetsInAoE.Count > 0)
            {
                if (DistributionStepRounds > 0)
                {
                    int availableSteps = finalDuration / DistributionStepRounds;
                    int stepsPerTarget = availableSteps / context.AllTargetsInAoE.Count;
                    finalDuration = stepsPerTarget * DistributionStepRounds;
                }
                else
                {
                    finalDuration /= context.AllTargetsInAoE.Count;
                }
            }

            effectInstance.DurationInRounds = Mathf.Max(1, finalDuration);

            // Assume CreatureStats has the MyEffects controller
            target.MyEffects.AddEffect(effectInstance, context.Caster, ability, effectSaveDC);
            count++;
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (EffectToApply == null || EffectToApply.AiTacticalTag == null) return 0;

        // --- SPECIAL AI LOGIC FOR WATER BREATHING ---
        if (EffectToApply.EffectName == "Water Breathing")
        {
            float score = 0;
            
            // ARCHITECTURE NOTE: In Godot, we assume AIController is a Child Node or easily accessible.
            // We use a safe cast or search here. 
            // Adjust "AIController" string if your node is named differently in the scene.
            var aiController = context.Caster.GetNodeOrNull<AIController>("AIController");
            
            if (aiController == null) return 0;
            
            var primaryTarget = aiController.GetPerceivedHighestThreat();
            if (primaryTarget == null) return 0; 

            foreach (var ally in context.AllTargetsInAoE)
            {
                if (ally == context.Caster) continue;
                // Check Godot Groups or Tags for "Water Breathing" effect existence
                if (ally.MyEffects.HasEffect("Water Breathing") || ally.Template.SpeedSwim > 0) continue;

                // Pathfinding Singleton Check
                var path = Pathfinding.Instance.FindPath(ally, ally.GlobalPosition, primaryTarget.GlobalPosition);
                
                // GridManager Singleton Check
                if (path != null && path.Any(p => GridManager.Instance.NodeFromWorldPoint(p).TerrainType == TerrainType.Water))
                {
                    score += 120f;
                }
            }
            return score;
        }
        // --- END SPECIAL LOGIC ---

        int validCount = 0;
        foreach(var t in context.AllTargetsInAoE)
        {
            if ((TargetFilter == null || TargetFilter.IsTargetValid(context.Caster, t)) && !t.MyEffects.HasEffect(EffectToApply.EffectName))
            {
                validCount++;
            }
        }

        // AIScoringEngine Static Call
        return AIScoringEngine.ScoreTacticalTag(EffectToApply.AiTacticalTag, context, validCount);
    }
}