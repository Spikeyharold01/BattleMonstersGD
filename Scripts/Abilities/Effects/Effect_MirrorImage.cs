using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_MirrorImage.cs (GODOT VERSION)
// PURPOSE: An effect component for casting the Mirror Image spell.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_MirrorImage : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
CreatureStats caster = context.Caster;
if (caster == null) return;

// Rule: 1d4 images + 1 per 3 caster levels (max 8 total).
    int baseImages = Dice.Roll(1, 4);
    int levelBonusImages = Mathf.FloorToInt(caster.Template.CasterLevel / 3f);
    int totalImages = Mathf.Min(8, baseImages + levelBonusImages);

    // Rule: Duration 1 min/level = 10 rounds/level = 60 seconds/level
    float duration = caster.Template.CasterLevel * 60f;

    // Add or get the controller component
    var controller = caster.GetNodeOrNull<MirrorImageController>("MirrorImageController");
    if (controller == null)
    {
        controller = new MirrorImageController();
        controller.Name = "MirrorImageController";
        caster.AddChild(controller);
    }
    controller.Initialize(totalImages, duration);
}

public override float GetAIEstimatedValue(EffectContext context)
{
    float score = 80f; 
    
    float healthPercent = context.Caster.CurrentHP / (float)context.Caster.Template.MaxHP;
    if (healthPercent < 0.7f)
    {
        score += 150f * (1 - healthPercent);
    }
    
    int threatCount = AoOManager.Instance.GetThreateningCreatures(context.Caster).Count;
    score += threatCount * 20f;
    
    return score;
}
}