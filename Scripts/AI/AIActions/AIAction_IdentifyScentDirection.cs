using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;
// =================================================================================================
// FILE: AIAction_IdentifyScentDirection.cs (GODOT VERSION)
// PURPOSE: AI Action to identify the direction of a scent.
// ATTACH TO: Do not attach (Pure C# Class).
// =================================================================================================
public class AIAction_IdentifyScentDirection : AIAction
{
private List<CreatureStats> targetsToLocate;

public AIAction_IdentifyScentDirection(AIController controller, List<CreatureStats> targets) : base(controller)
{
    this.targetsToLocate = targets;
    Name = "Sniff the air (Determine Direction)";
}

public override void CalculateScore()
{
    // High priority if we have no visible targets but know someone is there.
    if (controller.FindVisibleTargets().Count > 0)
    {
        Score = 10f; // Low priority if we already have someone to fight
    }
    else
    {
        Score = 120f; // High priority to find the hidden enemy
    }
}

public override async Task Execute()
{
    controller.MyActionManager.UseAction(ActionType.Move);
    GD.PrintRich($"<color=yellow>{controller.GetParent().Name} stops to sniff the air...</color>");
    
    CombatMemory.MarkScentDirectionChecked(controller.MyStats);
    var stateController = controller.GetParent().GetNode<CombatStateController>("CombatStateController");


    var focusedScentContacts = AISpatialAnalysis.FindSmelledContacts(controller.MyStats, includeEnvironmentalScents: true);
    if (focusedScentContacts.Count > 0)
    {
        GD.Print($"{controller.GetParent().Name} samples {focusedScentContacts.Count} scent traces (including environmental scents).");
    }

    foreach(var target in targetsToLocate)
    {
        // Rule: Noting the direction of the scent is a move action.
        // Result: We now know the general direction (KnownDirection status).
        stateController.UpdateEnemyLocation(target, LocationStatus.KnownDirection);
        
        // Godot: GlobalPosition is Vector3
        Vector3 myPos = controller.GetParent<Node3D>().GlobalPosition;
        Vector3 targetPos = target.GlobalPosition;
        
        GD.Print($"{controller.GetParent().Name} smells {target.Name} to the {GetCardinalDirection(targetPos - myPos)}.");
    }

    await controller.ToSignal(controller.GetTree().CreateTimer(1.0f), "timeout");
}

private string GetCardinalDirection(Vector3 dir)
{
    // Godot X is Right/Left, Z is Forward/Back
    if (Mathf.Abs(dir.X) > Mathf.Abs(dir.Z)) return dir.X > 0 ? "East" : "West";
    return dir.Z > 0 ? "South" : "North"; // Z+ is South/Back in Godot, Z- is North/Forward
}
}