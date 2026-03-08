using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// =================================================================================================
// FILE: AIDominationLogic.cs (GODOT VERSION)
// PURPOSE: Dedicated turn logic for a creature that is currently dominated.
// ATTACH TO: Do not attach (Static Class).
// =================================================================================================
public static class AIDominationLogic
{
/// <summary>
/// The dedicated turn logic for a creature that is currently dominated.
/// This is called by the TurnManager via AIController or directly.
/// </summary>
public static async Task ExecuteDominatedTurn(CreatureStats creature, CreatureMover mover)
{
var domController = creature.MyDomination;
if (domController == null || domController.CurrentCommand == null)
{
GD.Print($"{creature.Name} is dominated but has no command. Ending turn.");
TurnManager.Instance.EndTurn();
return;
}

DominateCommand command = domController.CurrentCommand;

    // In Godot, ActionManager is a persistent component.
    // The original script creates a TEMPORARY ActionManager.
    // We will reset the existing one instead, or assume TurnManager called OnTurnStart() already.
    // TurnManager.StartTurn calls ActionManager.OnTurnStart() before calling ExecuteDominatedTurn.
    // So we can just use the existing one.
    
    var actionManager = creature.GetNode<ActionManager>("ActionManager");
    // No need to OnTurnStart(), already done by TurnManager.

    // Core Dominated Logic:
    // 1. Check for immediate threats. If any are visible, attack them regardless of the command.
    var enemiesInView = AISpatialAnalysis.FindVisibleTargets(creature);
    
    if (enemiesInView.Any())
    {
        var nearestEnemy = enemiesInView.OrderBy(e => creature.GlobalPosition.DistanceTo(e.GlobalPosition)).First();
        GD.PrintRich($"[color=magenta]{creature.Name} is dominated but sees {nearestEnemy.Name} and will attack![/color]");
        await MoveAndAttackRoutine(creature, mover, nearestEnemy, actionManager);
    }
    // 2. If no enemies are visible, follow the master's command.
    else
    {
        switch (command.CommandType)
        {
            case DominateCommandType.StandStill:
                // Do nothing.
                break;

            case DominateCommandType.MoveAndAttack:
                // Move along the pre-calculated path.
                await FollowPathRoutine(creature, mover, command, actionManager);
                break;
        }
    }

    TurnManager.Instance.EndTurn();
}

/// <summary>
/// A helper routine for dominated creatures to move towards a specific target and attack it.
/// </summary>
private static async Task MoveAndAttackRoutine(CreatureStats creature, CreatureMover mover, CreatureStats target, ActionManager actionManager)
{
    // 1. Use Move Action if not in melee range
    if (actionManager.CanPerformAction(ActionType.Move) && creature.GlobalPosition.DistanceTo(target.GlobalPosition) > creature.GetEffectiveReach((Item_SO)null).max)
    {
        actionManager.UseAction(ActionType.Move);
        await mover.MoveToAsync(target.GlobalPosition);
        if (!GodotObject.IsInstanceValid(creature) || creature.CurrentHP <= 0) return; // Died during move
    }

    // 2. Use remaining actions to attack.
    if (creature.GlobalPosition.DistanceTo(target.GlobalPosition) <= creature.GetEffectiveReach((Item_SO)null).max)
    {
        if (actionManager.CanPerformAction(ActionType.FullRound))
        {
            actionManager.UseAction(ActionType.FullRound);
            CombatManager.ResolveFullAttack(creature, target);
        }
        else if (actionManager.CanPerformAction(ActionType.Standard))
        {
            actionManager.UseAction(ActionType.Standard);
            CombatManager.ResolveMeleeAttack(creature, target);
        }
    }
}

/// <summary>
/// A helper routine for dominated creatures to follow their master's long-term path.
/// It consumes one move action's worth of path steps per turn.
/// </summary>
private static async Task FollowPathRoutine(CreatureStats creature, CreatureMover mover, DominateCommand command, ActionManager actionManager)
{
    if (command.PathToDestination == null || command.PathToDestination.Count == 0)
    {
        // Path is complete, so the creature now stands still.
        command.CommandType = DominateCommandType.StandStill;
        return;
    }

    float moveBudget = mover.GetEffectiveMovementSpeed();
    int stepsTaken = 0;
    
    // Move along the path for one move action.
    while (stepsTaken * 5f < moveBudget && command.PathToDestination.Count > 0)
    {
        Vector3 nextWaypoint = command.PathToDestination[0];
        
        // Simplified movement logic: Instant teleport for this specialized routine
        // In a real game, use Mover.MoveTo logic, but we need to step through the list.
        // Godot: We can lerp or just snap for turn-based logic if no animation.
        creature.GlobalPosition = nextWaypoint; 
        command.PathToDestination.RemoveAt(0); 
        stepsTaken++;
        
        // Simulate traversal time
        await creature.ToSignal(creature.GetTree().CreateTimer(0.1f), "timeout");
    }

    actionManager.UseAction(ActionType.Move);
    GD.PrintRich($"[color=magenta]{creature.Name} follows its master's command, moving {stepsTaken*5f} feet.</color>");

    // If the path is now empty, the destination has been reached.
    if (command.PathToDestination.Count == 0)
    {
        GD.PrintRich($"[color=magenta]{creature.Name} has reached its commanded destination.</color>");
        command.CommandType = DominateCommandType.StandStill;
    }
}
}