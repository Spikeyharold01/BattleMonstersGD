using Godot;
using System.Collections.Generic;
using System.Linq; // Needed for Sum

[GlobalClass]
public partial class HealEffect : AbilityEffectComponent
{
    [Export] public int DiceCount;
    [Export] public int DieSides;
    [Export] public int FlatBonus;
    [Export] public bool ScalesWithCasterLevel = false;
	[Export] public int DiceScalingDivisor = 0;
    [Export] public int DiceScalingPerStep = 1;
    [Export] public int MaximumScaledDiceCount = 0;
	[Export] public int ScalingFlatBonusPerCasterLevel = 0;
    [Export] public int ScalingFlatBonusDivisor = 0;
    [Export] public int MaxScalingBonus = 0;

    [ExportGroup("Flat Per Level Mode")]
    [Export] public bool UseFlatAmountPerCasterLevel = false;
    [Export] public int FlatAmountPerCasterLevel = 0;
    [Export] public int FlatAmountMaximum = 0;
    [Export] public bool DamageUndeadInstead = false;
    [Export] public string UndeadDamageType = "Positive";

    [ExportGroup("Mythic Overrides")]
    [Export] public bool HasMythicVersion = false;
    [Export] public int MythicDiceCount;
    [Export] public int MythicDieSides;
    [Export] public int MythicScalingDivisor = 0; 
    [Export] public int MythicMaxBonus = 0;
    [Export] public int MythicFlatPerLevel = 0; 

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        foreach (var target in context.AllTargetsInAoE)
        {
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;
            
            bool didSave = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
            if (didSave) continue;

            int finalDiceCount = DiceCount;
            int finalDieSides = DieSides;
            int finalFlatBonus = FlatBonus;

            // --- MYTHIC LOGIC ---
            if (context.IsMythicCast && HasMythicVersion)
            {
                finalDiceCount = MythicDiceCount;
                finalDieSides = MythicDieSides;
                
                int levelBonus = 0;
                if (MythicFlatPerLevel > 0)
                {
                    levelBonus = context.Caster.Template.CasterLevel * MythicFlatPerLevel;
                }
                else if (MythicScalingDivisor > 0)
                {
                    levelBonus = Mathf.FloorToInt((float)context.Caster.Template.CasterLevel / MythicScalingDivisor);
                }

                if (MythicMaxBonus > 0) levelBonus = Mathf.Min(levelBonus, MythicMaxBonus);
                finalFlatBonus += levelBonus;
            }
            // --- STANDARD LOGIC ---
            else
            {
               if (DiceScalingDivisor > 0)
                {
                    int scaledDice = DiceCount + (Mathf.FloorToInt((float)context.Caster.Template.CasterLevel / DiceScalingDivisor) * Mathf.Max(DiceScalingPerStep, 1));
                    finalDiceCount = (MaximumScaledDiceCount > 0) ? Mathf.Min(scaledDice, MaximumScaledDiceCount) : scaledDice;
                }
                else if (ScalesWithCasterLevel)
                {
                    finalDiceCount = 1 + Mathf.FloorToInt((context.Caster.Template.CasterLevel - 1) / 2f);
                }
                
                if (ScalingFlatBonusPerCasterLevel > 0)
                {
                    int levelBonus = context.Caster.Template.CasterLevel * ScalingFlatBonusPerCasterLevel;
                    if (MaxScalingBonus > 0) levelBonus = Mathf.Min(levelBonus, MaxScalingBonus);
                    finalFlatBonus += levelBonus;
                }
                else if (ScalingFlatBonusDivisor > 0)
                {
                    int levelBonus = Mathf.FloorToInt((float)context.Caster.Template.CasterLevel / ScalingFlatBonusDivisor);
                    if (MaxScalingBonus > 0) levelBonus = Mathf.Min(levelBonus, MaxScalingBonus);
                    finalFlatBonus += levelBonus;
                }
            }

            int amountHealed;
            if (UseFlatAmountPerCasterLevel)
            {
                amountHealed = context.Caster.Template.CasterLevel * FlatAmountPerCasterLevel;
                if (FlatAmountMaximum > 0) amountHealed = Mathf.Min(amountHealed, FlatAmountMaximum);
            }
            else
            {
                amountHealed = Dice.Roll(finalDiceCount, finalDieSides) + finalFlatBonus;
            }

            if (amountHealed > 0)
            {
                bool isUndead = target.Template != null && target.Template.Type == CreatureType.Undead;
                if (DamageUndeadInstead && isUndead)
                {
                    target.TakeDamage(amountHealed, UndeadDamageType, context.Caster);
                }
                else
                {
                    target.HealDamage(amountHealed);
                }
            }
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (context.PrimaryTarget == null && context.AllTargetsInAoE.Count == 0) return 0;
        
        CreatureStats sampleTarget = context.PrimaryTarget ?? context.AllTargetsInAoE[0];
        if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, sampleTarget)) return 0;
        
        // Sum using standard Linq on Godot Array
        float totalMissingHealth = 0;
        foreach(var t in context.AllTargetsInAoE) totalMissingHealth += (t.Template.MaxHP - t.CurrentHP);
        
        int diceToRoll = DiceCount;
        if (DiceScalingDivisor > 0)
        {
            int scaledDice = DiceCount + (Mathf.FloorToInt((float)context.Caster.Template.CasterLevel / DiceScalingDivisor) * Mathf.Max(DiceScalingPerStep, 1));
            diceToRoll = (MaximumScaledDiceCount > 0) ? Mathf.Min(scaledDice, MaximumScaledDiceCount) : scaledDice;
        }
        else if (ScalesWithCasterLevel)
        {
            diceToRoll = 1 + Mathf.FloorToInt((context.Caster.Template.CasterLevel - 1) / 2f);
        }
        float finalFlatBonus = FlatBonus;
        if (ScalingFlatBonusPerCasterLevel > 0)
        {
            finalFlatBonus += context.Caster.Template.CasterLevel * ScalingFlatBonusPerCasterLevel;
        }
        else if (ScalingFlatBonusDivisor > 0)
        {
            finalFlatBonus += Mathf.FloorToInt((float)context.Caster.Template.CasterLevel / ScalingFlatBonusDivisor);
        }
		 if (MaxScalingBonus > 0)
        {
            finalFlatBonus = Mathf.Min(finalFlatBonus, FlatBonus + MaxScalingBonus);
        }
        float averageHeal;
        if (UseFlatAmountPerCasterLevel)
        {
            averageHeal = context.Caster.Template.CasterLevel * FlatAmountPerCasterLevel;
            if (FlatAmountMaximum > 0) averageHeal = Mathf.Min(averageHeal, FlatAmountMaximum);
        }
        else
        {
            averageHeal = (diceToRoll * (DieSides / 2f + 0.5f)) + finalFlatBonus;
        }

        return Mathf.Min(totalMissingHealth, averageHeal * context.AllTargetsInAoE.Count);
    }
}