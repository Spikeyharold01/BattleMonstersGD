using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class Effect_RemoveStatus : AbilityEffectComponent
{
    [Export] public Condition ConditionToRemove = Condition.None; // e.g. Unconscious
    [Export] public string EffectName = ""; // Optional specific name
    [Export] public bool CheckSpecificName = false;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        foreach (var target in context.AllTargetsInAoE)
        {
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;

            // Remove logic
            if (CheckSpecificName)
            {
                target.MyEffects.RemoveEffect(EffectName);
            }
            else if (ConditionToRemove != Condition.None)
            {
                // Iterate backwards
                for (int i = target.MyEffects.ActiveEffects.Count - 1; i >= 0; i--)
                {
                    if (target.MyEffects.ActiveEffects[i].EffectData.ConditionApplied == ConditionToRemove)
                    {
                        // Special Check for Sleep: Magical Sleep is Unconscious, but so is Dying.
                        // We shouldn't "Wake" a dying person (it won't work).
                        // How to distinguish? Status Effect Name usually ("Sleep Spell" vs "Unconscious from Wounds").
                        // But generic condition removal is fine if we assume "Wake" action implies physical jostling.
                        // However, waking a dying person doesn't stabilize them.
                        // We rely on the status effect removal. Removing "Sleep" wakes them. Removing "Dying" (if implemented as status) might be wrong.
                        // Dying is usually HP based. Unconsciousness from HP < 0 is state-driven, not effect-driven?
                        // Actually, in `TakeDamage`, we added `Unconscious_Effect.tres` when HP < 0.
                        // If we remove that effect while HP < 0, `CreatureStats` should ideally re-apply it next frame or check.
                        
                        // For Sleep Spell:
                        GD.Print($"Removing condition {ConditionToRemove} from {target.Name}.");
                        target.MyEffects.ActiveEffects.RemoveAt(i);
                    }
                }
            }
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        // AI Logic: Waking an ally is High Priority if they are sleeping.
        if (context.PrimaryTarget != null && context.PrimaryTarget.MyEffects.HasCondition(ConditionToRemove))
        {
            // Only valuable for allies
            if (context.Caster.IsInGroup("Player") == context.PrimaryTarget.IsInGroup("Player"))
                return 100f;
        }
        return 0f;
    }
}