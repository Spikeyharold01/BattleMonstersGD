using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: PartialSaveEffect.cs (GODOT VERSION)
// PURPOSE: Applies a Status Effect with different durations based on Save Success/Failure.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class PartialSaveEffect : AbilityEffectComponent
{
[Export]
[Tooltip("Effect to Apply")]
public StatusEffect_SO StatusEffect;

[ExportGroup("Duration Settings")]
[Export]
[Tooltip("If Save Fails (Full Effect). Dice notation (e.g., '1d2') or flat number.")]
public string FailDuration = "1d2";

[Export]
[Tooltip("If Save Succeeds (Partial Effect). Usually '1' or '0'.")]
public string SuccessDuration = "1";

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    CreatureStats target = context.PrimaryTarget;
    if (target == null || StatusEffect == null) return;

    // 1. Check Save
    bool saveSuccess = targetSaveResults.ContainsKey(target) && targetSaveResults[target];

    // 2. Calculate Duration
    int rounds = 0;
    string formula = saveSuccess ? SuccessDuration : FailDuration;
    
    // Using Dice.Roll for parsing as implemented in Effect_RangedTouchStatus previously
    rounds = Dice.Roll(formula);

    if (rounds <= 0)
    {
        GD.Print($"{target.Name} saved and the effect duration is 0. Effect negated.");
        return;
    }

    GD.Print($"{target.Name} {(saveSuccess ? "SAVES" : "FAILS")} vs {ability.AbilityName}. Applying {StatusEffect.EffectName} for {rounds} rounds.");

    // 3. Apply Effect
    var instance = (StatusEffect_SO)StatusEffect.Duplicate();
    instance.DurationInRounds = rounds;
    target.MyEffects.AddEffect(instance, context.Caster, ability);
}

public override float GetAIEstimatedValue(EffectContext context)
{
    if (StatusEffect.AiTacticalTag == null) return 50f;
    return AIScoringEngine.ScoreTacticalTag(StatusEffect.AiTacticalTag, context, 1) * 0.75f;
}
}