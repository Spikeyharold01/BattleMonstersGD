using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: InvisibilityPurgeController.cs (GODOT VERSION)
// PURPOSE: Suppresses Invisibility on any creature within radius.
// ATTACH TO: Caster (Child Node).
// =================================================================================================
public partial class InvisibilityPurgeController : GridNode
{
    private CreatureStats caster;
    private float radius;
    private float duration;
    
    // Track who we are currently suppressing so we can restore them if they leave
    private HashSet<ActiveStatusEffect> suppressedEffects = new HashSet<ActiveStatusEffect>();

    public void Initialize(CreatureStats source, float dur, float r)
    {
        caster = source;
        duration = dur;
        radius = r;
    }

    public override void _Process(double delta)
    {
        duration -= (float)delta;
        if (duration <= 0)
        {
            QueueFree();
            return;
        }

        var currentFrameEffects = new HashSet<ActiveStatusEffect>();
        
        // 1. Find all creatures in range
        // Since we are attached to Caster, origin is GlobalPosition
        var allCreatures = TurnManager.Instance.GetAllCombatants(); // Optimization: Use GridManager spatial query? 
        // For Sim: Iterating list is fine.
        
        foreach (var creature in allCreatures)
        {
            if (caster.GlobalPosition.DistanceTo(creature.GlobalPosition) <= radius)
            {
                // 2. Suppress Invisibility
                if (creature.MyEffects != null)
                {
                    foreach(var effect in creature.MyEffects.ActiveEffects)
                    {
                        if (effect.EffectData.ConditionApplied == Condition.Invisible)
                        {
                            // Rule: Natural Invisibility (ImmuneToPurge) is not affected.
                            if (effect.EffectData.ImmuneToPurge) continue;

                            if (!effect.IsSuppressed)
                            {
                                effect.IsSuppressed = true;
                                GD.Print($"{creature.Name}'s Invisibility is revealed by Purge!");
                            }
                            currentFrameEffects.Add(effect);
                        }
                    }
                }
            }
        }

        // 3. Restore effects that are no longer in range
        foreach(var oldEffect in suppressedEffects)
        {
            if (!currentFrameEffects.Contains(oldEffect))
            {
                // Verify the effect still exists (wasn't dispelled/expired naturally)
                // Actually, since we hold a reference to the class instance, it's fine.
                // But if it was removed from the list, this reference is stale? 
                // ActiveStatusEffect is a class, but if it's removed from Controller list, setting Suppressed false does nothing harmfull.
                oldEffect.IsSuppressed = false;
                GD.Print($"Invisibility restored (left Purge area).");
            }
        }
        
        suppressedEffects = currentFrameEffects;
    }

    public override void _ExitTree()
    {
        // Restore all when spell ends
        foreach(var effect in suppressedEffects) effect.IsSuppressed = false;
    }
}