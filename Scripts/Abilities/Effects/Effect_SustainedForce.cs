using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: Effect_SustainedForce.cs (GODOT VERSION)
// PURPOSE: Moves an item telekinetically.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_SustainedForce : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
// TargetObject is Node3D
var targetNode = context.TargetObject as Node3D;
if (targetNode == null) return;

var worldItem = targetNode as WorldItem ?? targetNode.GetNodeOrNull<WorldItem>("WorldItem");
    if (worldItem == null || !worldItem.ItemData.IsEquippable) return;

    GD.Print($"Telekinetically moving {worldItem.ItemData.ItemName} to {context.AimPoint}.");
    // Move the root node of the WorldItem (usually the parent of the script if attached to child, or the node itself)
    // WorldItem.cs extends Node3D, so `worldItem.GlobalPosition` works if it's the root.
    worldItem.GlobalPosition = context.AimPoint; 
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var targetNode = context.TargetObject as Node3D;
    if (targetNode == null) return 0f;

    var worldItem = targetNode as WorldItem ?? targetNode.GetNodeOrNull<WorldItem>("WorldItem");
    if (worldItem == null || !worldItem.ItemData.IsEquippable) return 0f;

    var aiController = context.Caster.GetNodeOrNull<AIController>("AIController");
    var recipient = context.AllTargetsInAoE.FirstOrDefault(); 
    if (aiController == null || recipient == null) return 0f;

    float upgradeScore = aiController.GetItemUpgradeScore(worldItem.ItemData, recipient);
    
    return Mathf.Max(0, upgradeScore);
}
}