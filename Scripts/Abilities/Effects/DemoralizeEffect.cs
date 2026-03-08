using Godot;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
// =================================================================================================
// FILE: DemoralizeEffect.cs (GODOT VERSION)
// PURPOSE: An effect component for using the Intimidate skill to demoralize a foe.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class DemoralizeEffect : AbilityEffectComponent
{
[Export]
[Tooltip("The StatusEffect_SO to apply on a successful check (should be 'Shaken').")]
public StatusEffect_SO ShakenEffect;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    CreatureStats caster = context.Caster;
    CreatureStats target = context.PrimaryTarget;
    if (caster == null || target == null || ShakenEffect == null) return;

    // Rule: Target must be within 30 feet.
    if (caster.GlobalPosition.DistanceTo(target.GlobalPosition) > 30f)
    {
        GD.Print($"Demoralize failed: {target.Name} is out of range.");
        return;
    }

    // Rule: Target must be able to see and hear you. We use Line of Sight as the check.
    var visibility = LineOfSightManager.GetVisibility(target, caster);
    if (!visibility.HasLineOfSight)
    {
        GD.Print($"Demoralize failed: {target.Name} cannot clearly see or hear {caster.Name}.");
        return;
    }

    // Rule: DC = 10 + the target’s Hit Dice + the target’s Wisdom modifier.
    int targetHitDice = 0;
    if (!string.IsNullOrEmpty(target.Template.HitDice))
    {
        Match hdMatch = Regex.Match(target.Template.HitDice, @"^\d+");
        if (hdMatch.Success) int.TryParse(hdMatch.Value, out targetHitDice);
    }
    int dc = 10 + targetHitDice + target.WisModifier;

    // Rule: Each additional check increases the DC by +5.
    dc += CombatMemory.GetIntimidateRetryPenalty(caster, target);

    // Rule: Get the full Intimidate bonus, including size, feats, and race.
    int intimidateBonus = caster.GetIntimidateBonus(target);
    int intimidateRoll = Dice.Roll(1, 20) + intimidateBonus;

    GD.Print($"{caster.Name} attempts to Demoralize {target.Name} (Intimidate). Rolls {intimidateRoll} vs DC {dc}.");

    if (intimidateRoll >= dc)
    {
        // Success: Target is shaken for 1 round + 1 for every 5 points over DC.
        int duration = 1 + Mathf.FloorToInt((intimidateRoll - dc) / 5f);
        
        GD.PrintRich($"[color=green]Success![/color] {target.Name} is now Shaken for {duration} round(s).");

        // Apply a fresh instance of the Shaken effect.
        // Check based on effect reference or name.
        var existingEffect = target.MyEffects.ActiveEffects.FirstOrDefault(e => e.EffectData.EffectName == ShakenEffect.EffectName);
        if (existingEffect != null)
        {
            // If already shaken by this, just extend the duration.
            existingEffect.RemainingDuration = Mathf.Max(existingEffect.RemainingDuration, duration);
        }
        else
        {
            var effectInstance = (StatusEffect_SO)ShakenEffect.Duplicate();
            effectInstance.DurationInRounds = duration;
            target.MyEffects.AddEffect(effectInstance, caster, ability);
        }
    }
    else
    {
        // Fail: The opponent is not shaken. Record the failure for the retry penalty.
        GD.PrintRich($"[color=red]Failure.[/color] The attempt has no effect.");
        CombatMemory.RecordIntimidateFailure(caster, target);
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    CreatureStats caster = context.Caster;
    CreatureStats target = context.PrimaryTarget;
    if (caster == null || target == null) return 0f;

    // AI Rule: Don't try to demoralize a creature that is already Shaken, Frightened, or Panicked.
    if (target.MyEffects.HasCondition(Condition.Shaken) || target.MyEffects.HasCondition(Condition.Frightened) || target.MyEffects.HasCondition(Condition.Panicked))
    {
        return 0f;
    }

    // AI calculates its chance of success.
    int targetHitDice = 0;
    if (!string.IsNullOrEmpty(target.Template.HitDice))
    {
        Match hdMatch = Regex.Match(target.Template.HitDice, @"^\d+");
        if (hdMatch.Success) int.TryParse(hdMatch.Value, out targetHitDice);
    }
    int dc = 10 + targetHitDice + target.WisModifier + CombatMemory.GetIntimidateRetryPenalty(caster, target);
    int bonus = caster.GetIntimidateBonus(target);
    
    // Probability to succeed: (21 + bonus - dc) / 20
    float successChance = Mathf.Clamp((21f + bonus - dc) / 20f, 0f, 1f);
    if (successChance < 0.3f) return 0f; // AI won't attempt very low-probability checks.

    // The value of Shaken is the -2 penalty it applies. This is a very effective debuff.
    float score = 80f;

    // It's more valuable to demoralize a high-threat target.
    if (target == CombatMemory.GetHighestThreat())
    {
        score += 50f;
    }

    return score * successChance;
}
}