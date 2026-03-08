using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: AoEHelper.cs (GODOT VERSION)
// PURPOSE: Static helper class for Area of Effect calculations.
// DO NOT ATTACH: This is a static class.
// =================================================================================================
public static class AoEHelper
{
// Layer mask assumed for "Creature" collision layer.
// In Godot 4, Layer 2 is commonly used for characters/creatures.
// Adjust this mask if your project settings use different layers.
private const uint CREATURE_MASK = 2;
private const uint OBSTACLE_MASK = 1; // Assuming Layer 1 is Walls/Obstacles

public static Godot.Collections.Array<CreatureStats> GetTargetsInCone(Node3D caster, Vector3 direction, AreaOfEffect aoeData, string targetGroup)
{
    var targetsFound = new Godot.Collections.Array<CreatureStats>();
    var spaceState = caster.GetWorld3D().DirectSpaceState;

    // OverlapSphere logic using ShapeQuery
    var shape = new SphereShape3D { Radius = aoeData.Range };
    var query = new PhysicsShapeQueryParameters3D 
    { 
        Shape = shape, 
        Transform = new Transform3D(Basis.Identity, caster.GlobalPosition), 
        CollisionMask = CREATURE_MASK 
    };
    
    var potentialTargets = spaceState.IntersectShape(query);

    foreach (var dict in potentialTargets)
    {
        var col = (Node3D)dict["collider"];
        // Check group membership (replacing Tag)
        if (!col.IsInGroup(targetGroup)) continue;
        
        var targetStats = col as CreatureStats ?? col.GetNodeOrNull<CreatureStats>("CreatureStats");
        if (targetStats == null) continue;

        Vector3 vectorToTarget = targetStats.GlobalPosition - caster.GlobalPosition;
        
        // Angle Check (Degrees)
        if (Mathf.RadToDeg(direction.AngleTo(vectorToTarget.Normalized())) > aoeData.Angle / 2) continue;
        
        // Raycast for obstacles (Line of Effect)
        var rayQuery = PhysicsRayQueryParameters3D.Create(caster.GlobalPosition, targetStats.GlobalPosition, OBSTACLE_MASK);
        var hit = spaceState.IntersectRay(rayQuery);
        if (hit.Count > 0) continue; // Blocked
        
        targetsFound.Add(targetStats);
    }
    return targetsFound;
}

public static Godot.Collections.Array<CreatureStats> GetTargetsInBurst(Vector3 center, AreaOfEffect aoeData, string targetGroup)
{
    var targetsFound = new Godot.Collections.Array<CreatureStats>();
    
    // We need context to access Physics Server.
    // Since this is a static utility, we assume `GridManager.Instance` or passed context is available.
    // Using `GridManager.Instance` as it's a Node in the scene tree.
    if (GridManager.Instance == null) return targetsFound;
    var spaceState = GridManager.Instance.GetWorld3D().DirectSpaceState;

    var shape = new SphereShape3D { Radius = aoeData.Range };
    var query = new PhysicsShapeQueryParameters3D 
    { 
        Shape = shape, 
        Transform = new Transform3D(Basis.Identity, center), 
        CollisionMask = CREATURE_MASK 
    };

    var potentialTargets = spaceState.IntersectShape(query);

    foreach (var dict in potentialTargets)
    {
        var col = (Node3D)dict["collider"];
        if (!col.IsInGroup(targetGroup)) continue;

        var targetStats = col as CreatureStats ?? col.GetNodeOrNull<CreatureStats>("CreatureStats");
        if (targetStats != null)
        {
            targetsFound.Add(targetStats);
        }
    }
    return targetsFound;
}

 public static Godot.Collections.Array<CreatureStats> GetTargetsInCylinder(Vector3 center, AreaOfEffect aoeData, string targetGroup)
{
    var targetsFound = new Godot.Collections.Array<CreatureStats>();
    if (GridManager.Instance == null) return targetsFound;
    var spaceState = GridManager.Instance.GetWorld3D().DirectSpaceState;
    
    // Find all creatures in horizontal radius
    // Godot CylinderShape3D is centered. Height is total height.
    // We need to position the shape center at `center.y + height/2`.
    
    var shape = new CylinderShape3D { Radius = aoeData.Range, Height = aoeData.Height };
    Vector3 shapeCenter = center + Vector3.Up * (aoeData.Height / 2f);
    
    var query = new PhysicsShapeQueryParameters3D 
    { 
        Shape = shape, 
        Transform = new Transform3D(Basis.Identity, shapeCenter), 
        CollisionMask = CREATURE_MASK 
    };

    var potentialTargets = spaceState.IntersectShape(query);

    foreach (var dict in potentialTargets)
    {
        var col = (Node3D)dict["collider"];
        if (!col.IsInGroup(targetGroup)) continue;

        var targetStats = col as CreatureStats ?? col.GetNodeOrNull<CreatureStats>("CreatureStats");
        if (targetStats != null)
        {
            // Strict Height Check not strictly needed if physics shape matches, 
            // but good for flat-top logic vs rounded caps if using Capsule. 
            // CylinderShape3D is flat, so physics check is accurate.
            targetsFound.Add(targetStats);
        }
    }
    return targetsFound;
}

/// <summary>
/// Finds all creatures along a straight line path, used for spells like Lightning Bolt or Whirling Blade.
/// </summary>
public static Godot.Collections.Array<CreatureStats> GetTargetsAlongLinePath(Vector3 start, Vector3 end, float width)
{
    var targetsFound = new Godot.Collections.Array<CreatureStats>();
    if (GridManager.Instance == null) return targetsFound;
    var spaceState = GridManager.Instance.GetWorld3D().DirectSpaceState;

    // ShapeCast in Godot 4
    var shape = new SphereShape3D { Radius = width / 2f }; // Sweeping a sphere creates a capsule volume
    var query = new PhysicsShapeQueryParameters3D 
    { 
        Shape = shape, 
        Transform = new Transform3D(Basis.Identity, start), 
        Motion = end - start, // The sweep vector
        CollisionMask = CREATURE_MASK 
    };
    
    // CastMotion just checks collision. For getting colliders, use IntersectShape along steps or use specific Cast logic?
    // Godot 4 has `IntersectShape` (static overlap) and `CastMotion` (move check).
    // `PhysicsDirectSpaceState3D.IntersectRay` is a line.
    // For volumetric sweep (CapsuleCast equivalent), Godot 4.0 doesn't have a direct "SweepAll" returning all hits along path easily in one call.
    // Workaround: Use a Cylinder or Box shape positioned and rotated to cover the line.
    
    // Creating a Box/Cylinder representing the line
    float length = start.DistanceTo(end);
    Vector3 midPoint = (start + end) / 2f;
    
    // Create a transform looking at end from start
    Transform3D transform = new Transform3D(Basis.Identity, midPoint);
    if (length > 0.001f)
    {
        transform = transform.LookingAt(end, Vector3.Up);
    }
    
    // Box approximation: Width x Height x Length
    // Z is forward, so length is Z.
    var boxShape = new BoxShape3D { Size = new Vector3(width, width, length) };
    
    var boxQuery = new PhysicsShapeQueryParameters3D
    {
        Shape = boxShape,
        Transform = transform,
        CollisionMask = CREATURE_MASK
    };
    
    var hits = spaceState.IntersectShape(boxQuery);

    foreach (var dict in hits)
    {
        var col = (Node3D)dict["collider"];
        var stats = col as CreatureStats ?? col.GetNodeOrNull<CreatureStats>("CreatureStats");
        if (stats != null && !targetsFound.Contains(stats))
        {
            targetsFound.Add(stats);
        }
    }
    return targetsFound;
}
}