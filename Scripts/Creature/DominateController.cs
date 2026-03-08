using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: DominateController.cs (GODOT VERSION)
// PURPOSE: Manages the state of a creature dominated by another.
// ATTACH TO: Creature Scene (Child Node).
// =================================================================================================
public enum DominateCommandType { None, MoveAndAttack, StandStill }
public class DominateCommand
{
public DominateCommandType CommandType;
public Vector3 TargetPosition;
public List<Vector3> PathToDestination;
}
public partial class DominateController : Godot.Node
{
public CreatureStats Master { get; private set; }
public DominateCommand CurrentCommand { get; private set; }

// In Godot, we use Groups instead of Tags.
// We will track the original group ("Player" or "Enemy") to restore it.
public string OriginalGroup { get; private set; }

private CreatureStats myStats;
private AIController myAI;

public override void _Ready()
{
    myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
    myAI = GetParent().GetNodeOrNull<AIController>("AIController");
}

public void ApplyDomination(CreatureStats master)
{
    this.Master = master;
    
    // Find Original Group
    if (myStats.IsInGroup("Player")) OriginalGroup = "Player";
    else if (myStats.IsInGroup("Enemy")) OriginalGroup = "Enemy";
    else OriginalGroup = "Neutral"; // Fallback

    // Change Allegiance: Remove old group, add Master's group
    myStats.RemoveFromGroup(OriginalGroup);
    
    string masterGroup = master.IsInGroup("Player") ? "Player" : "Enemy";
    myStats.AddToGroup(masterGroup);

    // Give a default command to stand still until a new one is issued.
    CurrentCommand = new DominateCommand { CommandType = DominateCommandType.StandStill };

    GD.PrintRich($"[color=red]{myStats.Name} has been DOMINATED by {Master.Name}!</color>");
}

public void SetNewCommand(DominateCommand newCommand)
{
    this.CurrentCommand = newCommand;
    GD.Print($"{Master.Name} issues a new command to {myStats.Name}: {newCommand.CommandType} to position {newCommand.TargetPosition}");
}

public void RemoveDomination()
{
    // Restore Allegiance
    string currentGroup = myStats.IsInGroup("Player") ? "Player" : "Enemy";
    myStats.RemoveFromGroup(currentGroup);
    myStats.AddToGroup(OriginalGroup);

    GD.PrintRich($"[color=green]{myStats.Name} has broken free from domination![/color]");
    QueueFree();
}
}