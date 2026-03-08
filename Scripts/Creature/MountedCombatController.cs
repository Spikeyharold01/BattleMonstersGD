using Godot;
// =================================================================================================
// FILE: MountedCombatController.cs (GODOT VERSION)
// PURPOSE: Manages states and reactive checks related to mounted combat.
// ATTACH TO: All creature prefabs (Child Node).
// =================================================================================================
public partial class MountedCombatController : Godot.Node
{
// --- CACHED COMPONENTS ---
private CreatureStats myStats;

// --- TURN-BASED STATE ---
[ExportGroup("Current Turn State")]
[Export]
[Tooltip("Is the rider successfully guiding their mount with their knees this turn?")]
public bool IsGuidingWithKnees { get; set; }

[Export]
[Tooltip("Is the rider currently using their mount for cover?")]
public bool IsUsingMountAsCover { get; set; }

// --- CROSS-TURN STATE ---
[ExportGroup("Cross-Turn State")]
[Export]
[Tooltip("How many consecutive rounds has the mount been spurred?")]
public int RoundsSpurred { get; set; } = 0;

public override void _Ready()
{
    myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
}

/// <summary>
/// Called at the start of the creature's turn to handle automatic checks.
/// Should be called by ActionManager.OnTurnStart.
/// </summary>
public void OnTurnStart()
{
    if (myStats.IsMounted)
    {
        // Automatically attempt to guide with knees at the start of the turn. This is a "no action" event.
        int rideCheck = Dice.Roll(1, 20) + myStats.GetSkillBonus(SkillType.Ride);
        if (rideCheck >= 5)
        {
            IsGuidingWithKnees = true;
        }
        else
        {
            IsGuidingWithKnees = false;
            GD.Print($"{myStats.Name} fails to guide with knees (Ride check: {rideCheck} vs DC 5). One hand is occupied.");
        }
    }
    else
    {
        RoundsSpurred = 0; // Not mounted, so reset spur count.
    }
}
}