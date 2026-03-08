using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: FeignHarmlessnessEffect.cs (GODOT VERSION)
// PURPOSE: Logic for Feigning Harmlessness via Bluff.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class FeignHarmlessnessEffect : AbilityEffectComponent
{
[Export]
public StatusEffect_SO HarmlessStatusEffect;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    CreatureStats bluffer = context.Caster;
    CreatureStats target = context.PrimaryTarget;

    if (bluffer == null || target == null || HarmlessStatusEffect == null) return;
    
     // --- Opposed Skill Check ---
    int bluffRoll = Dice.Roll(1, 20) + bluffer.GetSkillBonus(SkillType.Bluff);
    int senseMotiveRoll = Dice.Roll(1, 20) + target.GetSkillBonus(SkillType.SenseMotive);

    if ((int)bluffer.Template.Size < (int)target.Template.Size && !CombatMemory.HasActedOffensively(bluffer))
    {
        bluffRoll += 5;
    }

    GD.Print($"{bluffer.Name} attempts to Feign Harmlessness to {target.Name}. Bluff: {bluffRoll} vs Sense Motive: {senseMotiveRoll}.");

    if (bluffRoll >= senseMotiveRoll)
    {
        GD.PrintRich($"<color=green>Success!</color> {target.Name} perceives {bluffer.Name} as harmless.");
        // Apply the special status effect. The source is the bluffer.
        target.MyEffects.AddEffect((StatusEffect_SO)HarmlessStatusEffect.Duplicate(), bluffer, ability);
    }
    else
    {
        GD.PrintRich($"<color=red>Failure.</color> The attempt has no effect.");
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    CreatureStats bluffer = context.Caster;
    CreatureStats target = context.PrimaryTarget;
    if (bluffer == null || target == null) return 0f;

    // AI won't try if it has already been aggressive.
    if (CombatMemory.HasActedOffensively(bluffer)) return 0f;

    float estimatedSenseMotive = 10.5f + target.GetSkillBonus(SkillType.SenseMotive);
    float averageBluff = 10.5f + bluffer.GetSkillBonus(SkillType.Bluff);
    
    if ((int)bluffer.Template.Size < (int)target.Template.Size)
    {
        averageBluff += 5;
    }

    float successChance = Mathf.Clamp((averageBluff - estimatedSenseMotive + 1) / 20f, 0f, 1f);
    if (successChance < 0.4f) return 0f; 

    float score = 250f;
    if (target != CombatMemory.GetHighestThreat())
    {
        score *= 0.5f; 
    }

    return score * successChance;
}
}
// Depen