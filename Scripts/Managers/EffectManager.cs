using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: EffectManager.cs (GODOT VERSION)
// PURPOSE: A singleton manager to track all persistent spell effects on the battlefield.
// ATTACH TO: A persistent "GameManager" Node.
// =================================================================================================

public partial class EffectManager : Node
{
    public static EffectManager Instance { get; private set; }

    private List<PersistentEffectController> activeEffects = new List<PersistentEffectController>();

    public override void _Ready()
    {
        if (Instance != null && Instance != this) 
        {
            QueueFree();
        }
        else 
        {
            Instance = this;
        }
    }

    public void RegisterEffect(PersistentEffectController effect)
    {
        if (!activeEffects.Contains(effect))
        {
            activeEffects.Add(effect);
        }
    }

    public void UnregisterEffect(PersistentEffectController effect)
    {
        activeEffects.Remove(effect);
    }

    // This should be called every frame to handle creatures moving in and out of effect areas.
    public override void _Process(double delta)
    {
        // To avoid modifying collection while iterating if TickAreaCheck causes unregister
        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            if (GodotObject.IsInstanceValid(activeEffects[i]))
            {
                activeEffects[i].TickAreaCheck();
            }
            else
            {
                activeEffects.RemoveAt(i);
            }
        }
    }
    
    /// <summary>
    /// Finds a persistent effect at a given world position.
    /// </summary>
    /// <returns>The effect controller at the position, or null if none is found.</returns>
    public PersistentEffectController GetEffectAtPosition(Vector3 position)
    {
        foreach (var effect in activeEffects)
        {
            if (effect.GlobalPosition.DistanceTo(position) <= effect.Area.Range)
            {
                return effect;
            }
        }
        return null;
    }
}