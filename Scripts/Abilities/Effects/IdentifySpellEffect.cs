using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: IdentifySpellEffect.cs (GODOT VERSION)
// PURPOSE: An effect component for identifying a spell as it is being cast using Spellcraft.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
// A special context class to pass the incoming spell information to the effect
public class IdentifySpellContext : EffectContext
{
public Ability_SO IncomingSpell;
}
[GlobalClass]
public partial class IdentifySpellEffect : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
CreatureStats identifier = context.Caster;
CreatureStats spellcaster = context.PrimaryTarget;

// The incoming spell is passed via a modified context from the CombatManager
    Ability_SO incomingSpell = (context as IdentifySpellContext)?.IncomingSpell;

    if (identifier == null || spellcaster == null || incomingSpell == null) return;

    // Rule: Trained Only
    int spellcraftRanks = identifier.Template.SkillRanks?.Find(s => s.Skill == SkillType.Spellcraft)?.Ranks ?? 0;
    if (spellcraftRanks == 0)
    {
        GD.Print($"{identifier.Name} is untrained in Spellcraft and cannot attempt to identify the spell.");
        return;
    }
    
    // Rule: DC = 15 + spell level.
    int dc = 15 + incomingSpell.SpellLevel;
    int spellcraftBonus = identifier.GetSkillBonus(SkillType.Spellcraft);

    // Rule: Incurs the same penalties as a Perception check due to distance, conditions, etc.
    // Assuming CalculatePerceptionModifiers exists or simulating simple distance check.
    // CombatCalculations has GetVisibilityFromPoint, LineOfSightManager has GetVisibility.
    // `CalculatePerceptionModifiers` was likely an extension method or internal helper in Unity source not fully detailed before.
    // I will implement a basic distance penalty (-1 per 10ft) here if the method is missing from ported LoS Manager.
    // Since `LineOfSightManager` snippet provided earlier didn't have `CalculatePerceptionModifiers`, I will implement logic inline.
    
    float distance = identifier.GlobalPosition.DistanceTo(spellcaster.GlobalPosition);
    int perceptionPenalties = Mathf.FloorToInt(distance / 10f); // -1 per 10ft standard rule
    
    int finalBonus = spellcraftBonus - perceptionPenalties;

    int spellcraftRoll = Dice.Roll(1, 20) + finalBonus;

    GD.Print($"{identifier.Name} uses a reaction to identify the spell from {spellcaster.Name}. Spellcraft check: {spellcraftRoll} (Roll + {finalBonus} Bonus) vs DC {dc}.");

    if (spellcraftRoll >= dc)
    {
        GD.PrintRich($"[color=cyan]Success![/color] The spell is identified as '{incomingSpell.AbilityName}'.");
        // For AI, record this knowledge in Combat Memory
        CombatMemory.RecordIdentifiedSpell(spellcaster, incomingSpell);
    }
    else
    {
        GD.Print("Failed to identify the incoming spell.");
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    // This is a reaction, so its value is not scored in the normal AI action loop.
    // The decision to use it is handled in AIController.OnRequestReaction.
    return 0;
}
}