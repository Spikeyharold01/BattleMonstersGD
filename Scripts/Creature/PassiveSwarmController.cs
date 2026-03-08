using Godot;
// =================================================================================================
// FILE: PassiveSwarmController.cs (GODOT VERSION)
// PURPOSE: Handles swarm auto-damage at end of turn.
// ATTACH TO: Creature Scene (Child Node).
// =================================================================================================
public partial class PassiveSwarmController : GridNode
{
[Export]
[Tooltip("The ability that defines the swarm's automatic damage (e.g., 3d6 + Distraction).")]
public Ability_SO SwarmAttackAbility;

// We need a hook for OnTurnEnd.
// TurnManager.EndTurn calls many things, but doesn't emit a specific signal for individual creature end yet in converted code.
// However, ActionManager/TurnManager handles turn flow.
// Solution: Add a method `OnTurnEnd()` and ensure it's called by TurnManager/ActionManager logic,
// OR have this component check state.
// Given the previous pattern (ActionManager updates on Start), we need an End hook.
// I will add the method `OnTurnEnd()` and assume the integration is handled similarly to `OnTurnStart`.
// Actually, `TurnManager.EndTurn` calculates index and starts next.
// It calls `TickDownEffects` on the ACTIVE combatant before switching.
// We can hook into that or add a specific call.

// I will implement `OnTurnEnd` and note the required adjustment in TurnManager.

public void OnTurnEnd()
{
    var creature = GetParent() as CreatureStats ?? GetParent().GetNode<CreatureStats>("CreatureStats");
    if (SwarmAttackAbility == null) return;

    GD.PrintRich($"<color=orange>{creature.Name} (Swarm) finishes moving and swarms victims!</color>");

    // Trigger the effect via CombatManager
    _ = CombatManager.ResolveAbility(
        creature, 
        null, 
        null, 
        GetParent<Node3D>().GlobalPosition, 
        SwarmAttackAbility, 
        false
    );
}
}