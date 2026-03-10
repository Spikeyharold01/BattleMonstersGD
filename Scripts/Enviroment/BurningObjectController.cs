using Godot;
// =================================================================================================
// FILE: BurningObjectController.cs (GODOT VERSION)
// PURPOSE: Controls logic for an individual burning object (hazard area).
// ATTACH TO: Objects that are burning (as a Child Node).
// =================================================================================================
public partial class BurningObjectController : Node
{
private float tickTimer = 1.0f; // Deal damage every second
private float duration = 12f; // Burn for 2 rounds
private bool fireStarted = false;

public override void _Process(double delta)
{
    if (!fireStarted)
    {
        FireManager.Instance?.StartNewFire(GetParent<Node3D>().GlobalPosition);
        fireStarted = true;
    }
    
    duration -= (float)delta;
    if (duration <= 0)
    {
        QueueFree(); // The fire goes out
        return;
    }

    tickTimer -= (float)delta;
    if (tickTimer <= 0)
    {
        tickTimer = 1.0f;
        // Find creatures in a 5ft radius (2.5f units radius if 1 unit = 1 meter? GridManager used 5ft node. 
        // In Pathfinder, 5ft is ~1.5m. If NodeDiameter is 1.5m (standard 3D), 2.5f is correct. 
        // Godot Physics OverlapSphere logic:
        
        var spaceState = GetParent<Node3D>().GetWorld3D().DirectSpaceState;
        var shape = new SphereShape3D { Radius = 2.5f };
        var query = new PhysicsShapeQueryParameters3D 
        { 
            Shape = shape, 
            Transform = new Transform3D(Basis.Identity, GetParent<Node3D>().GlobalPosition), 
            CollisionMask = 2 // Assuming Layer 2 is creatures
        };
        
        var hits = spaceState.IntersectShape(query);

        foreach (var dict in hits)
        {
            var hitNode = (Node3D)dict["collider"];
            var creature = hitNode as CreatureStats ?? hitNode.GetNodeOrNull<CreatureStats>("CreatureStats");
            
            if (creature != null)
            {  
                // Instead of direct damage, being near a burning object forces a Catch on Fire check.
                creature.GetNodeOrNull<StatusEffectController>("StatusEffectController")?.CatchOnFire(null);
            }
        }
    }
}
}