using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: IdentifyCreatureEffect.cs (GODOT VERSION)
// PURPOSE: An effect component for using a Knowledge skill to identify a creature's abilities.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class IdentifyCreatureEffect : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
CreatureStats target = context.PrimaryTarget;
CreatureStats caster = context.Caster;
if (target == null || caster == null || !ability.SkillCheck.RequiresSkillCheck) return;

SkillType knowledgeSkill = GetKnowledgeSkillForCreature(target.Template.Type);
    if (knowledgeSkill == SkillType.Knowledge) // Knowledge is a placeholder, means no specific skill applies
    {
        GD.PrintErr($"No valid Knowledge skill found to identify creature type: {target.Template.Type}");
        return;
    }

    int skillBonus = caster.GetSkillBonus(knowledgeSkill);
    
    // Rule: Untrained checks cannot be made against DCs higher than 10.
    int ranks = caster.Template.SkillRanks?.Find(s => s.Skill == knowledgeSkill)?.Ranks ?? 0;
    if (ranks == 0)
    {
        GD.Print($"{caster.Name} is untrained in {knowledgeSkill} and cannot identify this creature.");
        return;
    }

    // Rule: DC = 10 + monster's CR.
    int dc = 10 + target.Template.ChallengeRating;
    int knowledgeRoll = Dice.Roll(1, 20) + skillBonus;
    
    GD.Print($"{caster.Name} attempts to identify {target.Name} with {knowledgeSkill} (Roll: {knowledgeRoll} vs DC: {dc}).");

    if (knowledgeRoll >= dc)
    {
        int successes = 1 + ((knowledgeRoll - dc) / 5);
        RevealCreatureInformation(target, successes);
    }
    else
    {
        GD.Print($"Identification failed. No information is recalled about {target.Name}.");
    }
}

private void RevealCreatureInformation(CreatureStats target, int piecesOfInfo)
{
    GD.PrintRich($"[color=cyan]Success! Recalling {piecesOfInfo} piece(s) of information about {target.Name}.[/color]");
    
    // Create a list of all potential pieces of information to reveal.
    var allInfo = new List<string>();
    
    // Godot Arrays handling for LINQ
    if (target.Template.Weaknesses != null)
        foreach (var w in target.Template.Weaknesses) 
            foreach(var type in w.DamageTypes) allInfo.Add($"Vulnerability:{type}");
            
    if (target.Template.Resistances != null)
        foreach (var r in target.Template.Resistances)
            foreach(var type in r.DamageTypes) allInfo.Add($"Resistance:{type}");
            
    if (target.Template.Immunities != null)
        foreach (var i in target.Template.Immunities) allInfo.Add($"Immunity:{i}");
        
    if (target.Template.SpecialQualities != null)
        foreach (var sq in target.Template.SpecialQualities) allInfo.Add($"Special:{sq}");
        
    if (target.Template.Regeneration > 0)
        allInfo.Add("Special:Regeneration");

    // Reveal new information that hasn't been identified yet.
    for (int i = 0; i < piecesOfInfo; i++)
    {
        var newInfo = allInfo.FirstOrDefault(info => !CombatMemory.IsTraitIdentified(target, info));
        if (newInfo != null)
        {
            CombatMemory.RecordIdentifiedTrait(target, newInfo);
            // In a real game, this would also trigger a UI element to display this information to the player.
        }
        else
        {
            GD.Print("...but no new information is available to recall.");
            break;
        }
    }
}

private SkillType GetKnowledgeSkillForCreature(CreatureType type)
{
    switch (type)
    {
        case CreatureType.Construct:
        case CreatureType.Dragon:
        case CreatureType.MagicalBeast:
            return SkillType.KnowledgeArcana;
        case CreatureType.Aberration:
        case CreatureType.Ooze:
            return SkillType.KnowledgeDungeoneering;
        case CreatureType.Humanoid:
            return SkillType.KnowledgeLocal;
        case CreatureType.Animal:
        case CreatureType.Fey:
        case CreatureType.MonstrousHumanoid:
        case CreatureType.Plant:
        case CreatureType.Vermin:
            return SkillType.KnowledgeNature;
        case CreatureType.Outsider:
            return SkillType.KnowledgePlanes;
        case CreatureType.Undead:
            return SkillType.KnowledgeReligion;
        default:
            return SkillType.Knowledge; // Represents no valid skill
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    if (context.PrimaryTarget == null) return 0f;

    var target = context.PrimaryTarget;
    
    // AI won't try to identify a creature if it already knows everything about it.
    var knownTraits = CombatMemory.GetIdentifiedTraits(target);
    int totalPossibleTraits = (target.Template.Weaknesses?.Count ?? 0) + (target.Template.Immunities?.Count ?? 0); // Simplified count
    if (knownTraits.Count >= totalPossibleTraits && totalPossibleTraits > 0)
    {
        return 0f; // Already know everything
    }

    // Base value for gaining information is moderate.
    float baseValue = 40f;
    
    // Value increases if the target is high-threat.
    if (target == CombatMemory.GetHighestThreat())
    {
        baseValue += 30f;
    }

    // Value is weighted by the chance of success.
    int dc = 10 + target.Template.ChallengeRating;
    SkillType skill = GetKnowledgeSkillForCreature(target.Template.Type);
    float successChance = Mathf.Clamp((21f + context.Caster.GetSkillBonus(skill) - dc) / 20f, 0f, 1f);
    
    return baseValue * successChance;
}
}