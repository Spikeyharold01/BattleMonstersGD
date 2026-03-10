using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: Effect_Veil.cs (GODOT VERSION)
// PURPOSE: Handles Veil illusion application.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_Veil : AbilityEffectComponent
{
[Export]
[Tooltip("The StatusEffect_SO to represent being veiled. Its description will be shown.")]
public StatusEffect_SO VeiledStatusEffect;

[Export]
[Tooltip("The illusion to apply. If null, the AI/Player must choose one.")]
public CreatureTemplate_SO IllusionToApply; // For AI to pre-define its choice

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    if (VeiledStatusEffect == null) return;
    
    CreatureTemplate_SO finalIllusion = IllusionToApply;
    
    // TargetObject (GameObject) -> Node3D
    // CreatureTemplateHolder component logic simulation
    if (context.TargetObject != null)
    {
        var holder = context.TargetObject as CreatureTemplateHolder ?? context.TargetObject.GetNodeOrNull<CreatureTemplateHolder>("CreatureTemplateHolder");
        if (holder != null)
        {
            finalIllusion = holder.Template;
        }
    }
    
    if (finalIllusion == null)
    {
        GD.PrintErr("Veil cast without a target illusion to project!");
        return;
    }

    // Rule: +10 bonus on the Disguise check.
    // Assuming SkillType.Disguise exists
    int disguiseCheck = Dice.Roll(1, 20) + context.Caster.GetSkillBonus(SkillType.Disguise) + 10;

    foreach (var target in context.AllTargetsInAoE)
    {
        if (targetSaveResults.ContainsKey(target) && targetSaveResults[target])
        {
            GD.Print($"{target.Name} saved against Veil and negates the effect.");
            continue;
        }

        GD.Print($"{context.Caster.Name} veils {target.Name} to appear as a {finalIllusion.CreatureName}.");
        var effectInstance = (StatusEffect_SO)VeiledStatusEffect.Duplicate();
        target.MyEffects.AddEffect(effectInstance, context.Caster, ability);

        // Find visual child (Assuming Node structure)
        Node3D visualChild = target.GetNodeOrNull<Node3D>("Visuals");
        if(visualChild == null) visualChild = target;

        var veilController = new VeilController();
        veilController.Name = "VeilController";
        target.AddChild(veilController);
        veilController.ApplyVeil(finalIllusion, visualChild, context.Caster, ability);
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var aiController = context.Caster.GetNodeOrNull<AIController>("AIController");
    if (aiController == null) return 0f;

    // Note: AIController methods like FindAllies were refactored to use AISpatialAnalysis static methods.
    // If AIController still exposes them, great. If not, use AISpatialAnalysis directly.
    // Based on AIController conversion, it delegates or exposes.
    // Using static helper for safety.
    var allies = AISpatialAnalysis.FindAllies(context.Caster);
    var enemies = AISpatialAnalysis.FindVisibleTargets(context.Caster);

    var weakAlly = allies.OrderBy(a => a.Template.ChallengeRating).FirstOrDefault();
    if (weakAlly != null && context.AllTargetsInAoE.Contains(weakAlly))
    {
        return 200f; 
    }

    var strongEnemy = enemies.OrderByDescending(e => e.Template.ChallengeRating).FirstOrDefault();
    if (strongEnemy != null && context.AllTargetsInAoE.Contains(strongEnemy))
    {
        if (strongEnemy == aiController.GetPerceivedHighestThreat())
        {
            return 400f;
        }
        return 250f;
    }

    return 0f; 
}
}
// Helper component for passing template data.
public partial class CreatureTemplateHolder : Node3D
{
[Export] public CreatureTemplate_SO Template;
}