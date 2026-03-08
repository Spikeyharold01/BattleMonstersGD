using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: IdentifyAuraEffect.cs (GODOT VERSION)
// PURPOSE: Logic for identifying magical auras via Knowledge (Arcana).
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class IdentifyAuraEffect : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
CreatureStats target = context.PrimaryTarget;
CreatureStats caster = context.Caster;
if (target == null || caster == null) return;

var auraController = target.GetNodeOrNull<AuraController>("AuraController");
    if (auraController == null || !auraController.Auras.Any())
    {
        GD.Print($"{target.Name} has no magical auras to identify.");
        return;
    }

    var mostPotentAura = auraController.Auras
        .OrderByDescending(a => a.SpellLevel > 0 ? a.SpellLevel : a.CasterLevel / 2f)
        .First();

    int effectiveLevel = mostPotentAura.SpellLevel > 0 ? mostPotentAura.SpellLevel : Mathf.FloorToInt(mostPotentAura.CasterLevel / 2f);
    int dc = 15 + effectiveLevel;
    
    int knowledgeRoll = Dice.Roll(1, 20) + caster.GetSkillBonus(SkillType.KnowledgeArcana);

    GD.Print($"{caster.Name} attempts to identify auras on {target.Name} with Knowledge (Arcana). Rolls {knowledgeRoll} vs DC {dc}.");

    if (knowledgeRoll >= dc)
    {
        GD.PrintRich($"<color=cyan>Success!</color> Identified aura on {target.Name}. Total Auras: {auraController.Auras.Count}, Most Potent Strength: {mostPotentAura.Strength}, School: {mostPotentAura.School}.");
        CombatMemory.RecordMagicalAura(target, mostPotentAura.Strength);
    }
    else
    {
        GD.Print("Identification of auras failed. No information is revealed.");
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    if (context.PrimaryTarget == null) return 0f;
    var target = context.PrimaryTarget;
    
    var auraCtrl = target.GetNodeOrNull<AuraController>("AuraController");
    if (auraCtrl == null || !auraCtrl.Auras.Any())
    {
        return 0f;
    }

    if (CombatMemory.GetKnownAuraStrength(target) >= AuraStrength.Faint)
    {
        return 0f;
    }

    float score = 25f;
    if (target == CombatMemory.GetHighestThreat())
    {
        score += 25f;
    }

    int estimatedDC = 18;
    float successChance = Mathf.Clamp((10.5f + context.Caster.GetSkillBonus(SkillType.KnowledgeArcana) - estimatedDC) / 20f, 0f, 1f);

    return score * successChance;
}
}