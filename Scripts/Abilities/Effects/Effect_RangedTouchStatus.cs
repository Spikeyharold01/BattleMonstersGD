using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_RangedTouchStatus.cs (GODOT VERSION)
// PURPOSE: Generic Ranged Touch Attack -> Status Effect (with Save for partial duration).
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_RangedTouchStatus : AbilityEffectComponent
{
[Export] public StatusEffect_SO EffectToApply;

[ExportGroup("Duration Logic")]
[Export] public string HitDuration = "1d2";
[Export] public string SaveDuration = "1";
[Export] public bool AllowsSave = true;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    var caster = context.Caster;
    var target = context.PrimaryTarget;
    if (target == null) return;

    // 1. Attack Roll
    bool hit = CombatMagic.ResolveAbilityAttack(caster, target, ability);
    if (!hit) return;

    // 2. Save Logic
    // Note: ImportUtils.ParseDurationStringToData was NOT in the provided ImportUtils.cs.
    // I will implement a local helper or assume Dice.Roll can parse directly if simple.
    // Dice.Roll(string) was converted.
    // However, "1d2" is a string. `DurationData` is a class.
    // I will implement parsing logic here using Dice.Roll for int result directly.
    
    int durationRounds = Dice.Roll(HitDuration);
    
    if (AllowsSave)
    {
        bool saved = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
        if (saved)
        {
            durationRounds = Dice.Roll(SaveDuration);
            GD.Print($"{target.Name} saved! Duration reduced to {durationRounds}.");
        }
    }

    // 3. Apply
    if (durationRounds > 0)
    {
        var instance = (StatusEffect_SO)EffectToApply.Duplicate();
        instance.DurationInRounds = durationRounds;
        target.MyEffects.AddEffect(instance, caster, ability);
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    if (EffectToApply.AiTacticalTag != null) 
        return AIScoringEngine.ScoreTacticalTag(EffectToApply.AiTacticalTag, context, 1);
    return 50f;
}
}