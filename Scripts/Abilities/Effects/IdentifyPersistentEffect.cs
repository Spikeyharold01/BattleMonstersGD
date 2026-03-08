using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: IdentifyPersistentEffect.cs (GODOT VERSION)
// PURPOSE: Effect for identifying persistent spell areas.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class IdentifyPersistentEffect : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
CreatureStats caster = context.Caster;
Vector3 targetPoint = context.AimPoint;
if (caster == null) return;

PersistentEffectController effect = EffectManager.Instance.GetEffectAtPosition(targetPoint);

    if (effect == null)
    {
        GD.Print("There is no spell effect to identify at that location.");
        return;
    }

    int dc = 20 + effect.Info.SpellLevel;
    int knowledgeRoll = Dice.Roll(1, 20) + caster.GetSkillBonus(SkillType.KnowledgeArcana);

    GD.Print($"{caster.Name} attempts to identify the spell effect with Knowledge (Arcana). Rolls {knowledgeRoll} vs DC {dc}.");

    if (knowledgeRoll >= dc)
    {
        GD.PrintRich($"<color=cyan>Success!</color> The effect is identified as '{effect.Info.EffectName}'.");
        // UI Hook here
    }
    else
    {
        GD.Print("Failed to identify the spell effect.");
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var effect = EffectManager.Instance.GetEffectAtPosition(context.AimPoint);
    if (effect == null) return 0f;

    // Check faction (Tag logic -> Group logic)
    // Assuming caster's group determines allegiance
    string casterGroup = context.Caster.IsInGroup("Player") ? "Player" : "Enemy";
    string effectCasterGroup = effect.Info.Caster.IsInGroup("Player") ? "Player" : "Enemy";

    if (casterGroup == effectCasterGroup)
    {
        return 0f;
    }

    return 10f;
}
}