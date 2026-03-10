using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: IllusionManager.cs (GODOT VERSION)
// PURPOSE: Manages illusions and their visibility/interaction logic.
// ATTACH TO: A persistent "GameManager" Node.
// =================================================================================================

public partial class IllusionManager : Node
{
    public static IllusionManager Instance { get; private set; }

    private List<IllusionController> activeIllusions = new List<IllusionController>();

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

    public void Register(IllusionController illusion)
    {
        if (!activeIllusions.Contains(illusion))
        {
            activeIllusions.Add(illusion);
        }
    }

    public void Unregister(IllusionController illusion)
    {
        activeIllusions.Remove(illusion);
    }

    /// <summary>
    /// Checks if a line between two points is blocked by an illusion from a specific viewer's perspective.
    /// </summary>
    /// <returns>The illusion that blocks LoS, or null if none do.</returns>
    public IllusionController GetIllusionBlockingLoS(Vector3 start, Vector3 end, CreatureStats viewer)
    {
        foreach (var illusion in activeIllusions)
        {
            // The caster can always see through their own illusions.
            if (viewer == illusion.Caster) continue;
            
            // Godot AABB Intersection Logic
            // GetBounds() must return Godot.Aabb
            Aabb bounds = illusion.GetBounds();
            
            // Aabb.IntersectsSegment returns true/false and (optionally) the intersection point.
            // We verify intersection.
            if (bounds.IntersectsSegment(start, end)) 
            {
                // In Unity, we checked distance < Vector3.Distance(start, end).
                // IntersectsSegment in Godot specifically checks the segment *between* start and end.
                // So if it returns a value (Vector3 point), it IS on the line segment.
                return illusion;
            }
        }
        return null;
    }

    /// <summary>
    /// Checks if a grid node is considered impassable due to an illusion for a specific creature.
    /// </summary>
    public bool IsNodeBlockedByIllusion(GridNode node, CreatureStats mover)
    {
        foreach (var illusion in activeIllusions)
        {
            // The caster can always move through their own illusions.
            if (mover == illusion.Caster) continue;

            // If the mover has disbelieved the illusion, they can pass through.
            if (illusion.HasDisbelieved(mover)) continue;

            // Check if the node's position is within the illusion's bounds.
            if (illusion.GetBounds().HasPoint(node.worldPosition))
            {
                return true; // The path is blocked for this creature.
            }
        }
        return false;
    }
}