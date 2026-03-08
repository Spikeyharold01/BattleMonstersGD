using Godot;
using System.Linq;

// =================================================================================================
// FILE: SoulEngineController.cs
// PURPOSE: Generic controller for constructs powered by a trapped soul/crucified body.
// =================================================================================================
public partial class SoulEngineController : GridNode
{
    private CreatureStats myStats;
    
    public bool IsBodyAttached { get; private set; } = true;
    public bool IsDeactivated { get; private set; } = false;
    public int DeactivationRoundsRemaining { get; private set; } = -1;
    public float DeactivationTimeRemainingSeconds { get; private set; } = -1f;

    public override void _Ready()
    {
        myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
        AddToGroup("SoulEngines");
    }

    /// <summary>
    /// Called when an enemy successfully grapples the body or uses teleportation magic on it.
    /// </summary>
    public void RemoveBody(CreatureStats remover)
    {
        if (!IsBodyAttached) return;

        IsBodyAttached = false;
        DeactivationRoundsRemaining = Dice.Roll(1, 4);
        DeactivationTimeRemainingSeconds = DeactivationRoundsRemaining * 6f; // 6 seconds per round

        GD.PrintRich($"[color=red]CRITICAL VULNERABILITY![/color] {remover?.Name ?? "A spell"} has removed the body from {myStats.Name}'s Soul Engine!");
        GD.PrintRich($"[color=orange]{myStats.Name} will deactivate in {DeactivationRoundsRemaining} rounds![/color]");

        var dyingEffect = new StatusEffect_SO
        {
            EffectName = "Soul Engine Deactivating",
            Description = "The crucified body has been removed. This construct will collapse when the duration expires.",
            DurationInRounds = DeactivationRoundsRemaining,
            ConditionApplied = Condition.Staggered 
        };
        myStats.MyEffects.AddEffect(dyingEffect, remover);
    }

    /// <summary>
    /// Called when an ally (or the construct itself) binds a new victim to the engine.
    /// </summary>
    public void AttachBody(CreatureStats victim)
    {
        IsBodyAttached = true;
        IsDeactivated = false;
        DeactivationRoundsRemaining = -1;
        DeactivationTimeRemainingSeconds = -1f;

        myStats.MyEffects.RemoveEffect("Inactive Soul Engine");
        myStats.MyEffects.RemoveEffect("Soul Engine Deactivating");

        GD.PrintRich($"[color=red]The Soul Engine roars to life, violently consuming the soul of {victim.Name}![/color]");
        
        // The victim's soul is consumed. They die instantly and cannot be resurrected while the engine runs.
        victim.TakeDamage(99999, "Death", myStats, null, null, null, true);
    }

    /// <summary>
    /// Arena Phase Integration: Ticks down the timer at the start of the construct's turn.
    /// </summary>
    public void OnTurnStart()
    {
        if (IsBodyAttached || myStats.CurrentHP <= 0) return;

        // If it's already deactivated, check if any allies are left to save it.
        if (IsDeactivated)
        {
            if (!HasActiveAllies())
            {
                GD.PrintRich($"[color=red]No allies remain to supply a body to {myStats.Name}. The construct crumbles to dust.[/color]");
                myStats.TakeDamage(99999, "True", null, null, null, null, true);
            }
            return;
        }

        DeactivationRoundsRemaining--;
        if (DeactivationRoundsRemaining <= 0)
        {
            TriggerDeactivation();
        }
        else
        {
            GD.Print($"{myStats.Name}'s Soul Engine sputters. {DeactivationRoundsRemaining} round(s) until total shutdown.");
        }
    }

    /// <summary>
    /// Travel Phase Integration: Ticks down the timer in real-time if out of combat.
    /// </summary>
    public override void _Process(double delta)
    {
        if (IsBodyAttached || myStats.CurrentHP <= 0 || IsDeactivated) return;

        if (TurnManager.Instance == null || TurnManager.Instance.GetAllCombatants().Count == 0)
        {
            DeactivationTimeRemainingSeconds -= (float)delta;
            if (DeactivationTimeRemainingSeconds <= 0f)
            {
                TriggerDeactivation();
            }
        }
    }

    private void TriggerDeactivation()
    {
        IsDeactivated = true;
        GD.PrintRich($"[color=red]{myStats.Name}'s Soul Engine violently shuts down. It goes inert.[/color]");
        
        // Apply a permanent helpless state until a new body is attached
        var inactiveEffect = new StatusEffect_SO
        {
            EffectName = "Inactive Soul Engine",
            ConditionApplied = Condition.Helpless,
            DurationInRounds = 0 
        };
        myStats.MyEffects.AddEffect(inactiveEffect, myStats);
    }

    private bool HasActiveAllies()
    {
        if (TurnManager.Instance == null) return false;
        string myGroup = myStats.IsInGroup("Player") ? "Player" : "Enemy";

        foreach (var c in TurnManager.Instance.GetAllCombatants())
        {
            if (c == myStats) continue;
            // An ally must be alive and capable of taking actions (not helpless) to bind a body
            if (c.IsInGroup(myGroup) && c.CurrentHP > 0 && !c.MyEffects.HasCondition(Condition.Helpless))
            {
                return true; 
            }
        }
        return false;
    }
}