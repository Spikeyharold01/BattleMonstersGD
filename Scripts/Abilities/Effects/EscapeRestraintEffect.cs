using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: EscapeRestraintEffect.cs (GODOT VERSION)
// PURPOSE: Logic for escaping generic restraints (Entangled, etc) using Escape Artist.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class EscapeRestraintEffect : AbilityEffectComponent
{
[Export]
[Tooltip("The name of the status effect this action attempts to remove (e.g., 'Entangled by Spell', 'Snared').")]
public string EffectNameToRemove;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    CreatureStats self = context.Caster;
    if (self == null || !self.MyEffects.HasEffect(EffectNameToRemove)) return;

    // The DC is set on the Ability_SO asset itself.
    // Assuming Ability_SO has SkillCheckInfo (SkillCheck)
    int dc = ability.SkillCheck.BaseDC;
    int escapeRoll = Dice.Roll(1, 20) + self.GetSkillBonus(SkillType.EscapeArtist);

    GD.Print($"{self.Name} attempts to escape {EffectNameToRemove} with Escape Artist. Roll: {escapeRoll} vs DC: {dc}.");

    if (escapeRoll >= dc)
    {
        GD.PrintRich($"<color=green>Success!</color> {self.Name} breaks free from {EffectNameToRemove}.");
        self.MyEffects.RemoveEffect(EffectNameToRemove);
    }
    else
    {
        GD.PrintRich($"<color=red>Failure.</color> The escape attempt fails.");
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    CreatureStats self = context.Caster;
    if (!self.MyEffects.HasEffect(EffectNameToRemove)) return 0f;

    // Using Ability from Context (Added in Part 31 adjustment)
    // If context.Ability is null, we can't estimate DC.
    if (context.Ability == null) return 0f;

    float score = 150f;
    int dc = context.Ability.SkillCheck.BaseDC;
    float successChance = Mathf.Clamp((10.5f + self.GetSkillBonus(SkillType.EscapeArtist) - dc) / 20f, 0f, 1f);
    
    return score * successChance;
}
}