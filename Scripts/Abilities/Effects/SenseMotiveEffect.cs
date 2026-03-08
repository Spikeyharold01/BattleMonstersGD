using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: SenseMotiveEffect.cs (GODOT VERSION)
// PURPOSE: Logic for actively using Sense Motive.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class SenseMotiveEffect : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
CreatureStats caster = context.Caster;
CreatureStats target = context.PrimaryTarget;
if (caster == null || target == null) return;

// Using BaseDC from ability SkillCheck
    int dc = ability.SkillCheck.BaseDC;

    int senseMotiveRoll = Dice.Roll(1, 20) + caster.GetSkillBonus(SkillType.SenseMotive);
    GD.Print($"{caster.Name} uses Sense Motive to assess {target.Name}. Rolls {senseMotiveRoll} vs DC {dc}.");

    if (senseMotiveRoll >= dc)
    {
        GD.PrintRich($"<color=cyan>Success!</color> {caster.Name} gets a feeling that something is amiss about {target.Name}.");
        CombatMemory.RecordIdentifiedTrait(target, "IsSuspicious");
    }
    else
    {
        GD.Print($"Sense Motive check failed. {caster.Name} learns nothing unusual about {target.Name}.");
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    CreatureStats caster = context.Caster;
    CreatureStats target = context.PrimaryTarget;
    if (caster == null || target == null) return 0f;

    if (CombatMemory.IsTraitIdentified(target, "IsSuspicious")) return 0f;

    float score = (target == CombatMemory.GetHighestThreat()) ? 25f : 10f;
    
    return score;
}
}