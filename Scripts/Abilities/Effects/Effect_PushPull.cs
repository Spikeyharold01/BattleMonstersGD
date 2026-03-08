using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_PushPull.cs (GODOT VERSION)
// PURPOSE: Moves targets towards or away from the caster (or a point).
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
public enum DirectionMode { AwayFromCaster, TowardsCaster, AwayFromPoint, TowardsPoint }
[GlobalClass]
public partial class Effect_PushPull : AbilityEffectComponent
{
[ExportGroup("Movement")]
[Export] public DirectionMode Direction = DirectionMode.AwayFromCaster;
[Export] public int DistanceInFeet = 30;

[ExportGroup("Constraints")]
[Export]
[Tooltip("If true, only works if caster and target are in Water terrain.")]
public bool RequiresWater = false;

[Export]
[Tooltip("If true, stops movement if they hit a wall/obstacle.")]
public bool StopAtObstacles = true;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    var caster = context.Caster;
    // Determine the origin of the force
    Vector3 origin = (Direction == DirectionMode.AwayFromPoint || Direction == DirectionMode.TowardsPoint) ? context.AimPoint : caster.GlobalPosition;

    // --- NEW: COMMAND OVERRIDE LOGIC ---
    DirectionMode finalMode = Direction;
    if (context.SelectedCommand == CommandWord.Pull) finalMode = DirectionMode.TowardsCaster;
    if (context.SelectedCommand == CommandWord.Push) finalMode = DirectionMode.AwayFromCaster;

    // 1. Validate Environment
    if (RequiresWater)
    {
        GridNode casterNode = GridManager.Instance.NodeFromWorldPoint(caster.GlobalPosition);
        if (casterNode.terrainType != TerrainType.Water)
        {
            GD.Print($"{ability.AbilityName} failed: Caster is not in water.");
            return;
        }
    }

    foreach (var target in context.AllTargetsInAoE)
    {
        if (target == caster) continue;

        // 2. Check Save
        if (targetSaveResults.ContainsKey(target) && targetSaveResults[target])
        {
            GD.Print($"{target.Name} resists the current.");
            continue;
        }

        // 3. Calculate Direction
        Vector3 pushDir = (target.GlobalPosition - origin).Normalized();
        // Reverse direction if we are pulling (Towards)
        if (finalMode == DirectionMode.TowardsCaster || finalMode == DirectionMode.TowardsPoint)
        {
            pushDir = -pushDir;
        }

        // 4. Calculate Destination
        Vector3 finalPos = target.GlobalPosition;
        int steps = Mathf.FloorToInt(DistanceInFeet / 5f); 
        
        var spaceState = target.GetParent<Node3D>().GetWorld3D().DirectSpaceState;

        for (int i = 1; i <= steps; i++)
        {
            Vector3 nextPos = target.GlobalPosition + (pushDir * (i * 5f));
            
            // Check Collision
            if (StopAtObstacles)
            {
                // Mask 1 (Unwalkable/Walls) + 2 (Creature) assumed
                var query = PhysicsRayQueryParameters3D.Create(finalPos, nextPos, 3);
                var result = spaceState.IntersectRay(query);
                if (result.Count > 0)
                {
                     GD.Print("Push stopped by obstacle.");
                     break;
                }
            }
            
            finalPos = nextPos;
        }

        // 5. Apply Movement
        GD.Print($"{target.Name} is moved to {finalPos}.");
        target.GlobalPosition = finalPos;
		if (finalPos != target.GlobalPosition) // Only if actually moved
        {
            target.TriggerForcedMovement(caster);
        }
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    return 30f * context.AllTargetsInAoE.Count;
}
}