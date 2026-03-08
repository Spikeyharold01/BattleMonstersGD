using Godot;
using System.Linq;
using System.Collections.Generic;
// =================================================================================================
// FILE: PassiveGazeController.cs (GODOT VERSION)
// PURPOSE: Manages passive Gaze attacks. Triggers at start of turn for all valid targets.
// ATTACH TO: Creature Scenes (Child Node).
// =================================================================================================
public partial class PassiveGazeController : Godot.Node
{
private CreatureStats myStats;
private Ability_SO gazeAbility;

[ExportGroup("Vision Settings")]
[Export]
[Tooltip("The total angle of the field of view. 120 means +/- 60 degrees from forward.")]
public float FieldOfViewAngle = 120f;

public override void _Ready()
{
	AddToGroup("GazeControllers"); 
    myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");

    // Find the Gaze ability in known abilities
    if (myStats != null && myStats.Template != null && myStats.Template.KnownAbilities != null)
    {
        gazeAbility = myStats.Template.KnownAbilities.FirstOrDefault(a => a.AbilityName.Contains("Gaze"));
    }
}

// Called by ActionManager at the start of ANY creature's turn.
// TurnManager tracks current combatant. We need to hook into when *other* creatures start their turn.
// In Godot, we can use a global signal or just have TurnManager iterate all Gaze controllers.
// Since TurnManager.cs in converted code doesn't emit a global signal for every turn start to all listeners,
// we need to inject this check into the active creature's turn start logic?
// NO, Gaze works when it's the *opponent's* turn.
// "At the start of the creature's turn, if they are within range/sight of gaze..."

// Proposal: Add a `CheckGazeExposure(CreatureStats activeCreature)` method to `TurnManager` or `CombatManager` that iterates all `PassiveGazeController`s.
// Or simpler: Have this controller register itself to a global list in TurnManager/CombatManager.
// I will use Group "GazeControllers" and assume TurnManager calls `HandleGazeCheck` on all of them at start of turn.
// This requires updating TurnManager.

// For this script, I expose the method.

public void HandleGazeCheck(CreatureStats activeCreature)
{
    // 1. Validate Basic State
    if (gazeAbility == null || myStats.CurrentHP <= 0) return;
    if (activeCreature == myStats) return; 

    // 2. Check Range
    float range = gazeAbility.Range.GetRange(myStats);
    float dist = GetParent<Node3D>().GlobalPosition.DistanceTo(activeCreature.GlobalPosition);
    if (dist > range) return;

    // 3. Check Visibility
    var visibility = LineOfSightManager.GetVisibility(activeCreature, myStats);
    if (!visibility.HasLineOfSight) return;

    // 4. Check Facing
    var myBody = GetParent<Node3D>();
    var targetBody = activeCreature; // CreatureStats is Node3D

    // Check A: Is the Gazer facing the Enemy?
    if (!IsFacing(myBody, targetBody.GlobalPosition)) 
    {
        return; 
    }

    // Check B: Is the Enemy facing the Gazer?
    if (!IsFacing(targetBody, myBody.GlobalPosition)) 
    {
        // Debug.Log($"{activeCreature.name} is safe; they have turned away.");
        return;
    }

    // 5. Check Immunity
    if (activeCreature.MyEffects.HasEffect($"Immunity to Gaze ({myStats.Name})")) return;

    // 6. Trigger Effect
    GD.PrintRich($"<color=cyan>{activeCreature.Name} meets the gaze of {myStats.Name}!</color>");
    var context = new EffectContext { Caster = myStats, PrimaryTarget = activeCreature };
    
    foreach(var effect in gazeAbility.EffectComponents)
    {
        effect.ExecuteEffect(context, gazeAbility, new Dictionary<CreatureStats, bool>());
    }
}

private bool IsFacing(Node3D viewer, Vector3 targetPos)
{
    Vector3 directionToTarget = (targetPos - viewer.GlobalPosition).Normalized();
    directionToTarget.Y = 0; 
    
    // Godot Forward is usually -Z.
    Vector3 forward = -viewer.GlobalTransform.Basis.Z;
    forward.Y = 0;

    float angle = Mathf.RadToDeg(forward.AngleTo(directionToTarget));
    
    return angle < (FieldOfViewAngle * 0.5f);
}
}