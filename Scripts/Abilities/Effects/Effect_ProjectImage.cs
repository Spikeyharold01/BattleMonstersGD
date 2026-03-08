using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: Effect_ProjectImage.cs (GODOT VERSION)
// PURPOSE: Spawns a Projected Image of the caster.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_ProjectImage : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
// The caster must have a character prefab assigned to their template to be duplicated.
if (context.Caster.Template.CharacterPrefab == null)
{
GD.PrintErr($"Cannot cast Project Image: Caster {context.Caster.Name} is missing a CharacterPrefab in their CreatureTemplate_SO.");
return;
}

Node3D imageGO = context.Caster.Template.CharacterPrefab.Instantiate<Node3D>();
    imageGO.Name = $"{context.Caster.Name}'s Projected Image";

    SceneTree tree = (SceneTree)Engine.GetMainLoop();
    tree.CurrentScene.AddChild(imageGO);
    
    imageGO.GlobalPosition = context.AimPoint;
    // Copy rotation from parent body of stats
    imageGO.GlobalRotation = context.Caster.GetParent<Node3D>().GlobalRotation;

    // Clean up components that shouldn't be on the image (Stats, AI) if the prefab has them
    var oldStats = imageGO.GetNodeOrNull<CreatureStats>("CreatureStats") ?? imageGO as CreatureStats;
    if (oldStats != null) oldStats.QueueFree();
    
    var oldAI = imageGO.GetNodeOrNull<AIController>("AIController");
    if (oldAI != null) oldAI.QueueFree();

    // Add ProjectedImageController
    var imageController = new ProjectedImageController();
    imageController.Name = "ProjectedImageController";
    imageGO.AddChild(imageController);
    
    imageController.Initialize(context.Caster);

    GD.Print($"{context.Caster.Name} creates a Projected Image at {context.AimPoint}.");
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var caster = context.Caster;
    float healthPercent = caster.CurrentHP / (float)caster.Template.MaxHP;

    // AI Strategy: This is a powerful defensive and strategic spell.
    // Value is highest when the caster is in a dangerous position.
    float score = 0;

    if (AoOManager.Instance.IsThreatened(caster) || healthPercent < 0.6f)
    {
        // Extremely high value for creating a safe casting proxy when threatened or wounded.
        score = 350f * (1 - healthPercent);
    }

    // Also valuable for bypassing enemy cover.
    var ai = caster.GetNodeOrNull<AIController>("AIController");
    var primaryTarget = ai?.GetPerceivedHighestThreat();
    if (primaryTarget != null)
    {
        var visibility = LineOfSightManager.GetVisibility(caster, primaryTarget);
        if (visibility.CoverBonusToAC > 0)
        {
            score += 100f; // High value for creating an angle to bypass cover.
        }
    }

    return score;
}
}