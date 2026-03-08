using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: IdentifyIncomingSpellEffect.cs (GODOT VERSION)
// PURPOSE: An effect component for identifying a spell as it is being cast.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class IdentifyIncomingSpellEffect : AbilityEffectComponent
{
// NOTE: This effect is special. It is called directly by CombatManager and does not go through the normal
// ability resolution pipeline. It does not need AI scoring as the decision is made in AIController.OnRequestReaction.

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    CreatureStats identifier = context.Caster;
    CreatureStats spellcaster = context.PrimaryTarget;
    int spellLevel = ability.SpellLevel; // The level is passed in via a dummy ability shell.

    if (identifier == null || spellcaster == null) return;

    // Rule: DC is 25 + spell level to identify a spell targeting you.
    int dc = 25 + spellLevel;
    int knowledgeRoll = Dice.Roll(1, 20) + identifier.GetSkillBonus(SkillType.KnowledgeArcana);

    GD.Print($"{identifier.Name} uses a reaction to identify the spell from {spellcaster.Name}. Knowledge (Arcana) check: {knowledgeRoll} vs DC {dc}.");

    if (knowledgeRoll >= dc)
    {
        GD.PrintRich($"[color=cyan]Success![/color] The spell is identified as '{ability.AbilityName}'.");
        // In a real game, this would log the information for the player or update AI memory.
    }
    else
    {
        GD.Print("Failed to identify the incoming spell.");
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    // This is not used for reactions. The decision logic is in AIController.
    return 0;
}
}