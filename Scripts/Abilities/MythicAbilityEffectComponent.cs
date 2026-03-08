using Godot;
using System.Collections.Generic;

[GlobalClass]
public abstract partial class MythicAbilityEffectComponent : AbilityEffectComponent
{
    // Allow mythic components to be reused as standard effect components when needed.
    public sealed override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        ExecuteMythicEffect(context, ability);
    }

    // Default AI value for mythic-only components unless overridden.
    public override float GetAIEstimatedValue(EffectContext context) => 0f;

    public abstract void ExecuteMythicEffect(EffectContext context, Ability_SO ability);
    public abstract string GetMythicDescription();
}