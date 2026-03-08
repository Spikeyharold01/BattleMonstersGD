using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: DamageAndApplyEffectOnFail.cs (GODOT VERSION)
// PURPOSE: Effect dealing damage (half on save) and applying status (negate on save).
//          Updated to support Dice Scaling and Mythic Overrides.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class DamageAndApplyEffectOnFail : AbilityEffectComponent
{
    [ExportGroup("Damage Component")]
    [Export] public DamageInfo Damage;
    
    [ExportGroup("Scaling")]
    [Export] public bool ScalesWithCasterLevel = false;
    [Export] public int DiceScalingDivisor = 0; // e.g. 2 for "per 2 levels"
    [Export] public int DiceScalingPerStep = 1;
    [Export] public int MaximumScaledDiceCount = 0; // e.g. 5 for "Max 5d6"

    [ExportGroup("Status Effect Component")]
    [Export]
    [Tooltip("This status effect is ONLY applied if the target fails their saving throw.")]
    public StatusEffect_SO EffectToApplyOnFail;

    [ExportGroup("Mythic Overrides")]
    [Export] public bool HasMythicVersion = false;
    [Export] public int MythicDieSides = 0; // e.g. 8 for d8s
    [Export] public bool MythicDurationMatchesTier = false; // If true, sets rounds to Mythic Rank
	 public bool BypassResistanceAndImmunity = false; // Set dynamically by Mythic Component

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (Damage == null) return;

        foreach (var target in context.AllTargetsInAoE)
        {
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;

            // --- 1. Calculate Dice Count ---
            int diceToRoll = Damage.DiceCount;
            int dieSides = Damage.DieSides;

            if (ScalesWithCasterLevel)
            {
                int cl = context.Caster.Template.CasterLevel;
                
                if (DiceScalingDivisor > 0)
                {
                    // "1d6 per 2 levels" usually implies minimum 1. 
                    // Formula: (CL / Divisor) * Step. 
                    // To ensure Level 1 gets 1 die if desired, configure Base=0 and use Ceil, or Base=1 and logic.
                    // Standard Pathfinder "1d6 per 2 levels" often starts at 1d6.
                    // We will use a simple Additive logic: Base + (CL / Divisor).
                    // For Ear-Piercing Scream (1d6/2 levels), set Base=0 in Asset, but ensure Min=1 in logic?
                    // Better: logic is Base + (CL / Divisor).
                    // If Spell is 1d6 per 2 levels, usually it's 1d6 at lvl 1, 1d6 at lvl 2, 2d6 at lvl 3?
                    // Or 1d6 at Lv2? RAW implies Lv2. But let's assume minimum 1 die for gameplay.
                    int extraDice = Mathf.FloorToInt((float)cl / DiceScalingDivisor) * DiceScalingPerStep;
                    diceToRoll += extraDice;
                }
                else
                {
                    // Default legacy scaling: 1 + (CL-1)/2
                    diceToRoll = 1 + Mathf.FloorToInt((cl - 1) / 2f);
                }

                // Apply Cap
                if (MaximumScaledDiceCount > 0)
                {
                    diceToRoll = Mathf.Min(diceToRoll, MaximumScaledDiceCount);
                }
                
                // Safety Floor
                diceToRoll = Mathf.Max(1, diceToRoll);
            }

            // --- 2. Mythic Overrides ---
            if (context.IsMythicCast && HasMythicVersion)
            {
                if (MythicDieSides > 0) dieSides = MythicDieSides;
            }

            // --- 3. Roll Damage ---
            int totalDamage = Dice.Roll(diceToRoll, dieSides) + Damage.FlatBonus;
            
            bool didSave = targetSaveResults.ContainsKey(target) && targetSaveResults[target];

            if (didSave)
            {
                // On a successful save, damage is halved, and the status effect is NOT applied.
                totalDamage /= 2;
                GD.Print($"{target.Name} saved against {ability.AbilityName}. Damage halved, Effect negated.");
            }
            else
            {
                // On a failed save, apply the status effect.
                if (EffectToApplyOnFail != null)
                {
                    var effectInstance = (StatusEffect_SO)EffectToApplyOnFail.Duplicate();
                    
                    if (context.IsMythicCast && HasMythicVersion && MythicDurationMatchesTier)
                    {
                        effectInstance.DurationInRounds = Mathf.Max(1, context.Caster.Template.MythicRank);
                        GD.Print($"Mythic Daze Duration: {effectInstance.DurationInRounds} rounds (Tier {context.Caster.Template.MythicRank}).");
                    }
                    int dc = ability.SavingThrow.BaseDC; // Or calculate dynamic DC if needed
                    target.MyEffects.AddEffect(effectInstance, context.Caster, ability, dc);
                }
            }

            if (totalDamage > 0)
            {
                 target.TakeDamage(totalDamage, Damage.DamageType, context.Caster, null, null, null, BypassResistanceAndImmunity);
            }
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        // AI scoring: Value is the damage plus the tactical value of the status effect.
        float damageValue = (Damage.DiceCount * (Damage.DieSides / 2f + 0.5f)) * context.AllTargetsInAoE.Count;
        float effectValue = 0;
        if (EffectToApplyOnFail != null && EffectToApplyOnFail.AiTacticalTag != null)
        {
            effectValue = AIScoringEngine.ScoreTacticalTag(EffectToApplyOnFail.AiTacticalTag, context, context.AllTargetsInAoE.Count);
        }
        
        return damageValue + effectValue;
    }
}