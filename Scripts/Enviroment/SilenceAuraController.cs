using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: SilenceAuraController.cs
// PURPOSE: A runtime 3D zone that suppresses sound events and applies the Silenced condition.
// =================================================================================================
public partial class SilenceAuraController : Node3D
{
    private CreatureStats caster;
    private float radius;
    private float duration;
    private StatusEffect_SO silenceStatus;
    
    private HashSet<CreatureStats> affectedCreatures = new HashSet<CreatureStats>();
    private string uniqueEffectName;

    public void Initialize(CreatureStats source, float dur, float r, StatusEffect_SO status)
    {
        caster = source;
        duration = dur;
        radius = r;
        silenceStatus = status;
        uniqueEffectName = $"{silenceStatus.EffectName}_{GetInstanceId()}";
        
        // This group is polled directly by SoundSystem to extinguish noise inside the aura
        AddToGroup("SilenceAuras");
    }

    public bool IsPointInside(Vector3 point)
    {
        return GlobalPosition.DistanceTo(point) <= radius;
    }

    public override void _Process(double delta)
    {
        duration -= (float)delta;
        if (duration <= 0)
        {
            ClearAll();
            QueueFree();
            return;
        }

        var currentFrameCreatures = new HashSet<CreatureStats>();
        var allCreatures = TurnManager.Instance.GetAllCombatants(); 
        
        foreach (var creature in allCreatures)
        {
            if (GodotObject.IsInstanceValid(creature) && GlobalPosition.DistanceTo(creature.GlobalPosition) <= radius)
            {
                currentFrameCreatures.Add(creature);
                
                // Add the effect to newly entered creatures
                if (!affectedCreatures.Contains(creature))
                {
                    if (silenceStatus != null)
                    {
                        GD.Print($"{creature.Name} enters the Silence aura.");
                        var instance = (StatusEffect_SO)silenceStatus.Duplicate();
                        instance.EffectName = uniqueEffectName;
                        instance.DurationInRounds = 0; // Duration is tied to their presence in the aura
                        creature.MyEffects.AddEffect(instance, caster);
                    }
                }
            }
        }

        // Clean up creatures that walked out
        foreach (var oldCreature in affectedCreatures)
        {
            if (!currentFrameCreatures.Contains(oldCreature) && GodotObject.IsInstanceValid(oldCreature))
            {
                if (silenceStatus != null)
                {
                    GD.Print($"{oldCreature.Name} leaves the Silence aura.");
                    oldCreature.MyEffects.RemoveEffect(uniqueEffectName);
                }
            }
        }
        
        affectedCreatures = currentFrameCreatures;
    }

    private void ClearAll()
    {
        foreach (var creature in affectedCreatures)
        {
            if (GodotObject.IsInstanceValid(creature) && silenceStatus != null)
            {
                creature.MyEffects.RemoveEffect(uniqueEffectName);
            }
        }
        affectedCreatures.Clear();
    }

    public override void _ExitTree()
    {
        ClearAll();
    }
}