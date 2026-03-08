using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
// =================================================================================================
// FILE: FascinateEffect.cs (GODOT VERSION)
// PURPOSE: Handles Fascinate spell logic (HD cap, Sorting targets).
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class FascinateEffect : AbilityEffectComponent
{
[Export]
[Tooltip("The StatusEffect_SO to apply on a failed save (should be 'Fascinated').")]
public StatusEffect_SO FascinatedEffect;

[Export]
[Tooltip("The base duration of the fascination in rounds, added to the caster's concentration time.")]
public int BaseDuration = 2;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    if (FascinatedEffect == null)
    {
        GD.PrintErr("FascinateEffect is missing its Status Effect asset!");
        return;
    }

    // Rule: Roll 2d4 + caster level (max 10) to determine total HD affected.
    int maxHD = Dice.Roll(2, 4) + Mathf.Min(10, context.Caster.Template.CasterLevel);
    GD.Print($"{context.Caster.Name}'s {ability.AbilityName} can affect up to {maxHD} HD of creatures.");

    // Rule: Sightless creatures are not affected.
    var potentialTargets = context.AllTargetsInAoE
        .Where(t => t.MyEffects.HasCondition(Condition.Blinded) == false)
        .ToList();

    // Rule: Affect creatures with the fewest HD first. Among equals, affect closest to the origin first.
    var sortedTargets = potentialTargets.OrderBy(t => GetCreatureHD(t))
                                        .ThenBy(t => context.AimPoint.DistanceTo(t.GlobalPosition))
                                        .ToList();

    int affectedHD = 0;
    foreach (var target in sortedTargets)
    {
        int targetHD = GetCreatureHD(target);
        if (affectedHD + targetHD > maxHD)
        {
            continue;
        }

        if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target))
        {
            continue;
        }

        bool didSave = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
        if (didSave)
        {
            GD.Print($"{target.Name} saved against {ability.AbilityName}.");
            continue;
        }
        
        // Apply the effect
        GD.PrintRich($"<color=magenta>{target.Name} ({targetHD} HD) is Fascinated by {ability.AbilityName}!</color>");
        var effectInstance = (StatusEffect_SO)FascinatedEffect.Duplicate();
        // A simple concentration could be 3 rounds + the base duration.
        effectInstance.DurationInRounds = 3 + BaseDuration; 
        target.MyEffects.AddEffect(effectInstance, context.Caster, ability);
        
        affectedHD += targetHD;
    }

    GD.Print($"Total HD affected: {affectedHD}/{maxHD}.");
}

public override float GetAIEstimatedValue(EffectContext context)
{
    if (context.AllTargetsInAoE.Count == 0) return 0;

    int maxHD = (int)(4.5f + Mathf.Min(10, context.Caster.Template.CasterLevel)); // Average of 2d4 is 4.5
    
    var potentialTargets = context.AllTargetsInAoE
        .Where(t => t.MyEffects.HasCondition(Condition.Blinded) == false && 
                    (TargetFilter == null || TargetFilter.IsTargetValid(context.Caster, t)))
        .OrderBy(t => GetCreatureHD(t))
        .ThenBy(t => context.AimPoint.DistanceTo(t.GlobalPosition))
        .ToList();

    float totalScore = 0;
    int affectedHD = 0;
    float baseValuePerTarget = 120f; 

    foreach (var target in potentialTargets)
    {
        int targetHD = GetCreatureHD(target);
        if (affectedHD + targetHD > maxHD) break;

        // AI estimates chance to fail the save.
        int predictedWillSave = target.GetWillSave(context.Caster, context.Ability);
        int dc = 10 + (context.Ability != null ? context.Ability.SpellLevel : 1) + context.Caster.WisModifier; 
        int rollNeeded = dc - predictedWillSave;
        float chanceToFailSave = Mathf.Clamp((rollNeeded - 1f) / 20f, 0f, 1f);

        if (chanceToFailSave > 0.3f) 
        {
            totalScore += baseValuePerTarget * chanceToFailSave;
            affectedHD += targetHD;
        }
    }

    int alliesHit = context.AllTargetsInAoE.Count(t => TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, t));
    totalScore -= alliesHit * 200f;

    return Mathf.Max(0, totalScore);
}

private int GetCreatureHD(CreatureStats creature)
{
    if (creature == null || string.IsNullOrEmpty(creature.Template.HitDice)) return 1;

    Match hdMatch = Regex.Match(creature.Template.HitDice, @"^\d+");
    if (hdMatch.Success && int.TryParse(hdMatch.Value, out int hd))
    {
        return hd;
    }
    return 1; 
}
}