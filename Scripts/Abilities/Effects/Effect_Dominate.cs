using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_Dominate.cs (GODOT VERSION)
// PURPOSE: Applies Dominate status and controller.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_Dominate : AbilityEffectComponent
{
[Export]
[Tooltip("The StatusEffect to represent the ongoing domination.")]
public StatusEffect_SO DominateStatusEffect;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    if (DominateStatusEffect == null) return;

    CreatureStats target = context.PrimaryTarget;
    if (target == null) return;
    
    if (targetSaveResults.ContainsKey(target) && targetSaveResults[target])
    {
        GD.Print($"{target.Name} resists the Dominate effect.");
        return;
    }

    // Apply the domination controller dynamically
    var domController = new DominateController();
    domController.Name = "DominateController";
    target.AddChild(domController);
    domController.ApplyDomination(context.Caster);

    // Apply the status effect which will manage duration and future saves
    var effectInstance = (StatusEffect_SO)DominateStatusEffect.Duplicate();
    target.MyEffects.AddEffect(effectInstance, context.Caster, ability);
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var caster = context.Caster;
    var target = context.PrimaryTarget;
    if (target == null) return 0f;

    float score = target.Template.ChallengeRating * 50f;

    // Factor in success chance (Will save + SR).
    var ability = context.Ability;
    
    // If ability is null in context, fallback to 0 or safe default, but context should have it now.
    int spellLevel = ability != null ? ability.SpellLevel : 0;
    
    int dc = 10 + spellLevel + caster.ChaModifier; 
    float chanceToFailSave = Mathf.Clamp((dc - target.GetWillSave(caster, ability) - 1) / 20f, 0f, 1f);
    
    var ai = caster.GetNodeOrNull<AIController>("AIController");
    float srSuccessChance = ai != null && ability != null ? ai.PredictSuccessChanceVsSR(target, ability) : 1.0f;

    // Don't attempt if chance is too low.
    if (chanceToFailSave * srSuccessChance < 0.25f) return 0f;

    return score * chanceToFailSave * srSuccessChance;
}
}