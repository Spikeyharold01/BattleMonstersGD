using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// =================================================================================================
// FILE: ReadyActionManager.cs (GODOT VERSION)
// PURPOSE: A global manager to track creatures who are Delaying or have a Readied Action.
// ATTACH TO: A persistent "GameManager" Node.
// =================================================================================================

/// <summary>
/// Defines the possible trigger conditions a creature can set for a readied action.
/// </summary>
public enum ReadyTriggerType {
    // Generic Triggers
    CreatureMoves,
    CreatureAttacks,
    CreatureUsesAbility,
    // Specific Triggers
    TargetMovesIntoThreatenedArea,
    TargetStartsCasting,
    AllyIsDamaged
}

/// <summary>
/// A data container holding all information about a single creature's readied action.
/// </summary>
public class ReadiedActionInfo
{
    public CreatureStats Combatant;
    public ReadyTriggerType TriggerType;
    public CreatureStats TriggerTarget; // e.g., "If THIS goblin moves..." (can be null for generic triggers)
    public Ability_SO ReadiedAbility;   // The specific attack or spell being readied.
    public CreatureStats ActionTarget;  // The intended target of the readied ability.
}

/// <summary>
/// Manages the state of all delaying or readying combatants. It acts as the central
/// authority for resolving these interruptions to the normal turn order.
/// </summary>
public partial class ReadyActionManager : GridNode
{
    public static ReadyActionManager Instance { get; private set; }

    // List of all actions currently being readied by any combatant.
    private List<ReadiedActionInfo> readiedActions = new List<ReadiedActionInfo>();
    // A fast-lookup set of all creatures who have chosen to Delay.
    private HashSet<CreatureStats> delayingCreatures = new HashSet<CreatureStats>();

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

    /// <summary>
    /// Called by a creature to declare they are readying an action.
    /// This consumes their Standard action for the turn.
    /// </summary>
    public void ReadyAction(ReadiedActionInfo info)
    {
        var actionManager = info.Combatant.GetNodeOrNull<ActionManager>("ActionManager");
        if (actionManager != null && actionManager.CanPerformAction(ActionType.Standard))
        {
            actionManager.UseAction(ActionType.Standard);
            readiedActions.Add(info);
            GD.PrintRich($"[color=orange]{info.Combatant.Name} readies an action: '{info.TriggerType}' against {info.TriggerTarget?.Name ?? "anyone"}.[/color]");
            
            // If the readying creature is the one whose turn it is, end their turn now.
            if(info.Combatant == TurnManager.Instance.GetCurrentCombatant())
            {
                TurnManager.Instance.EndTurn();
            }
        }
    }

    /// <summary>
    /// Called by a creature to declare they are delaying their turn.
    /// </summary>
    public void Delay(CreatureStats combatant)
    {
        delayingCreatures.Add(combatant);
        GD.PrintRich($"[color=orange]{combatant.Name} is delaying.[/color]");

        // If the delaying creature is the one whose turn it is, end their turn.
        if (combatant == TurnManager.Instance.GetCurrentCombatant())
        {
            TurnManager.Instance.EndTurn();
        }
    }
    
    /// <summary>
    /// Checks if a creature is currently delaying.
    /// </summary>
    public bool IsDelaying(CreatureStats combatant) => delayingCreatures.Contains(combatant);

    /// <summary>
    /// Allows a delaying creature to jump back into the turn order at the current position.
    /// </summary>
    public void ActFromDelay(CreatureStats combatant)
    {
        if (delayingCreatures.Contains(combatant))
        {
            delayingCreatures.Remove(combatant);
            GD.PrintRich($"[color=orange]{combatant.Name} acts from delay![/color]");
            TurnManager.Instance.InsertAndAct(combatant);
        }
    }

    /// <summary>
    /// This is the core method, called by other systems whenever a notable event occurs.
    /// It checks if this event triggers any readied actions.
    /// </summary>
    /// <param name="triggerType">The type of event that just happened.</param>
    /// <param name="actor">The creature who caused the event.</param>
    /// <param name="targetOfEvent">The target of the event, if any (e.g., who was attacked).</param>
    public void CheckTriggers(ReadyTriggerType triggerType, CreatureStats actor, CreatureStats targetOfEvent = null)
    {
        // Iterate backwards to safely remove items from the list while iterating.
        for (int i = readiedActions.Count - 1; i >= 0; i--)
        {
            var readyInfo = readiedActions[i];
            
            // A creature can't trigger their own readied action.
            if (readyInfo.Combatant == actor) continue;

            bool triggerMet = false;
            // Check if the event type matches the trigger type.
            if (readyInfo.TriggerType == triggerType)
            {
                // If the trigger has a specific target (e.g., "if this goblin moves"), check if the actor matches.
                // If the trigger target is null, it means the trigger is generic ("if anyone moves").
                if (readyInfo.TriggerTarget == null || readyInfo.TriggerTarget == actor)
                {
                    triggerMet = true;
                }
            }
            
            // Special Rule: Brace weapons are specifically for readied attacks against a charge.
            var inv = readyInfo.Combatant.GetNodeOrNull<InventoryController>("InventoryController");
            var weapon = inv?.GetEquippedItem(EquipmentSlot.MainHand);
            if(weapon != null && weapon.HasBraceFeature && actor.MyEffects.HasCondition(Condition.Charging))
            {
                triggerMet = true;
            }

            if (triggerMet)
            {
                GD.Print($"Event '{triggerType}' by {actor.Name} has triggered {readyInfo.Combatant.Name}'s readied action!");
                // Consume the readied action, whether used or not. It's lost once the trigger condition occurs.
                readiedActions.RemoveAt(i); 
                
                // Fire and forget execution
                _ = ExecuteReadiedAction(readyInfo);
            }
        }
    }

    /// <summary>
    /// Executes the readied action, interrupting the current turn.
    /// </summary>
    private async Task ExecuteReadiedAction(ReadiedActionInfo info)
    {
        // Pause the game briefly to show the interruption.
        await ToSignal(GetTree().CreateTimer(0.5f), "timeout");

        GD.PrintRich($"[color=red]INTERRUPT![/color] {info.Combatant.Name} uses their readied action.");

        // Pathfinder Rule: A readied action uses up the creature's immediate action for the round.
        info.Combatant.GetNode<ActionManager>("ActionManager").UseAction(ActionType.Immediate);
        
        // Resolve the action based on its type.
        if (info.ReadiedAbility.AttackRollType == AttackRollType.Melee || info.ReadiedAbility.AttackRollType == AttackRollType.Melee_Touch)
        {
            CombatManager.ResolveMeleeAttack(info.Combatant, info.ActionTarget);
        }
        else if (info.ReadiedAbility.AttackRollType == AttackRollType.Ranged || info.ReadiedAbility.AttackRollType == AttackRollType.Ranged_Touch)
        {
            var inv = info.Combatant.GetNodeOrNull<InventoryController>("InventoryController");
            var weapon = inv?.GetEquippedItem(EquipmentSlot.MainHand);
            if (weapon != null)
            {
               CombatManager.ResolveRangedAttack(info.Combatant, info.ActionTarget, weapon);
            }
        }
        // NOTE: Spell casting logic could be added here similar to CombatMagic.ResolveAbility

        // Pathfinder Rule: After acting, the creature's initiative count changes.
        TurnManager.Instance.ChangeInitiative(info.Combatant, TurnManager.Instance.GetCurrentTurnIndex());

        await ToSignal(GetTree().CreateTimer(1.0f), "timeout"); // Wait for animations
    }

    /// <summary>
    /// Wipes all state at the start of a new combat encounter.
    /// </summary>
    public void OnCombatStart()
    {
        readiedActions.Clear();
        delayingCreatures.Clear();
    }
    
    /// <summary>
    /// Clears any unused readied actions at the end of a full round.
    /// In Pathfinder, a readied action is lost if not triggered by your next turn.
    /// </summary>
    public void OnNewRound()
    {
        if (readiedActions.Any())
        {
            GD.Print("Clearing unused readied actions at the end of the round.");
            readiedActions.Clear();
        }
    }
}