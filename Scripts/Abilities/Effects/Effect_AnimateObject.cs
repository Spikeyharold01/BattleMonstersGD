using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: Effect_AnimateObject.cs
// PURPOSE: Transforms a specific environmental object into an active creature using Godot Groups as filters.
// =================================================================================================
[GlobalClass]
public partial class Effect_AnimateObject : AbilityEffectComponent
{
    [Export]
    [Tooltip("The creature template to spawn in place of the object.")]
    public CreatureTemplate_SO AnimatedTemplate;

    [Export]
    [Tooltip("Maximum number of these objects the caster can control at once.")]
    public int MaxControlled = 2;

    [Export]
    [Tooltip("If the animated object moves further than this distance from the caster, it becomes inert.")]
    public float ControlRange = 90f;

    [Export]
    [Tooltip("If true, it takes 1 full round to uproot/animate, stunning the creature for its first turn.")]
    public bool RequiresUprootDelay = true;

    [ExportGroup("Targeting Restrictions")]
    [Export]
    [Tooltip("The target object MUST belong to one of these Godot Groups to be animated (e.g., 'Tree', 'Corpse'). Leave empty to allow ANY object.")]
    public Godot.Collections.Array<string> RequiredObjectGroups = new();

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (AnimatedTemplate == null || AnimatedTemplate.CharacterPrefab == null)
        {
            GD.PrintErr("Effect_AnimateObject failed: No template or prefab assigned.");
            return;
        }

        var targetObject = context.TargetObject as ObjectDurability ?? context.TargetObject?.GetNodeOrNull<ObjectDurability>("ObjectDurability");
        
        if (targetObject == null)
        {
            GD.PrintRich($"[color=orange]{ability.AbilityName} failed: Must target a valid physical object.[/color]");
            return;
        }

        // Validate that the object belongs to the required group
        if (!IsObjectValidTarget(targetObject))
        {
            GD.PrintRich($"[color=orange]{ability.AbilityName} failed: Object is not a valid type for this animation.[/color]");
            return;
        }

        // Check control limits
        var currentAnimations = context.Caster.GetTree().GetNodesInGroup("AnimatedObjects")
            .Cast<AnimatedObjectController>()
            .Where(c => c.Creator == context.Caster)
            .ToList();

        if (currentAnimations.Count >= MaxControlled)
        {
            GD.PrintRich($"[color=orange]{ability.AbilityName} failed: Already controlling maximum number of objects ({MaxControlled}).[/color]");
            return;
        }

        // Record position and destroy original static object
        Vector3 spawnPos = targetObject.GlobalPosition;
        targetObject.QueueFree();

        // Spawn Animated Creature
        Node3D animatedNode = AnimatedTemplate.CharacterPrefab.Instantiate<Node3D>();
        context.Caster.GetTree().CurrentScene.AddChild(animatedNode);
        animatedNode.GlobalPosition = spawnPos;

        var newStats = animatedNode as CreatureStats ?? animatedNode.GetNodeOrNull<CreatureStats>("CreatureStats");
        if (newStats != null)
        {
            newStats.Template = AnimatedTemplate; // Ensure correct template
            
            // Inherit Faction
            if (context.Caster.IsInGroup("Player")) newStats.AddToGroup("Player");
            else newStats.AddToGroup("Enemy");

            // Attach Tether Controller
            var controller = new AnimatedObjectController();
            controller.Name = "AnimatedObjectController";
            newStats.AddChild(controller);
            controller.Initialize(context.Caster, ControlRange);

            // Add to Turn Order
            if (TurnManager.Instance != null)
            {
                TurnManager.Instance.ReviveCombatant(newStats); // Revive acts as an insertion mechanic
            }

            GD.PrintRich($"[color=purple]{context.Caster.Name} animates the object into a {AnimatedTemplate.CreatureName}![/color]");

            // Apply Uproot Delay
            if (RequiresUprootDelay)
            {
                var uprootEffect = new StatusEffect_SO
                {
                    EffectName = "Uprooting",
                    Description = "Uprooting and breaking free. Cannot act.",
                    ConditionApplied = Condition.Stunned,
                    DurationInRounds = 1
                };
                newStats.MyEffects.AddEffect(uprootEffect, context.Caster, ability);
                GD.Print($"{newStats.Name} will take 1 round to uproot itself.");
            }
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        var targetObject = context.TargetObject as ObjectDurability ?? context.TargetObject?.GetNodeOrNull<ObjectDurability>("ObjectDurability");
        
        if (targetObject == null || !IsObjectValidTarget(targetObject)) return 0f;

        var currentAnimations = context.Caster.GetTree().GetNodesInGroup("AnimatedObjects")
            .Cast<AnimatedObjectController>()
            .Where(c => c.Creator == context.Caster)
            .ToList();

        if (currentAnimations.Count >= MaxControlled) return 0f; // Limit reached

        // Very high value for permanently adding an allied combatant to the field
        return 200f;
    }

    private bool IsObjectValidTarget(Node3D targetObject)
    {
        // If no groups are required, any object works
        if (RequiredObjectGroups == null || RequiredObjectGroups.Count == 0) return true;

        foreach (string group in RequiredObjectGroups)
        {
            if (targetObject.IsInGroup(group) || (targetObject.GetParent() != null && targetObject.GetParent().IsInGroup(group)))
            {
                return true;
            }
        }

        return false;
    }
}