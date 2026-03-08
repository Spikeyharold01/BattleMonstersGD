using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Keeps track of creature objects so they can move between phases.
///
/// Main idea:
/// - reuse the same creature instances,
/// - do not clone them,
/// - keep their state/progress intact.
/// </summary>
public sealed class CreaturePersistenceService
{
    public static CreaturePersistenceService Active { get; private set; }

    // Hidden storage parent used while switching phases.
    private readonly GridNode _storageRoot;

    // All creatures managed by phase transitions.
    private readonly List<CreatureStats> _persistentCreatures = new List<CreatureStats>();

    /// <summary>
    /// Create the service with a storage node.
    /// </summary>
    public CreaturePersistenceService(GridNode storageRoot)
    {
        _storageRoot = storageRoot;
        Active = this;
    }

    /// <summary>
    /// Read-only view of tracked creatures.
    /// </summary>
    public IReadOnlyList<CreatureStats> PersistentCreatures => _persistentCreatures;

    /// <summary>
    /// Find all nodes in group "Creature" and register them.
    /// </summary>
    public void RegisterFromSceneTree(SceneTree tree)
    {
        if (tree == null) return;

        foreach (var node in tree.GetNodesInGroup("Creature"))
        {
            if (node is CreatureStats stats && !_persistentCreatures.Contains(stats))
            {
                _persistentCreatures.Add(stats);
            }
        }
    }

    /// <summary>
    /// Register one creature manually.
    /// </summary>
    public void RegisterCreature(CreatureStats creature)
    {
        if (creature == null || _persistentCreatures.Contains(creature)) return;
        _persistentCreatures.Add(creature);
    }

    /// <summary>
    /// Move all tracked creatures to storage.
    ///
    /// Used during phase shutdown.
    /// </summary>
    public void ParkAllCreatures()
    {
        foreach (var creature in _persistentCreatures)
        {
            if (!GodotObject.IsInstanceValid(creature) || creature.GetParent() == _storageRoot) continue;
            ReparentPreservingTransform(creature, _storageRoot);
        }
    }

    /// <summary>
    /// Move matching creatures to a target parent and place them.
    /// </summary>
    public void SpawnCreatures(Node3D parent, Func<CreatureStats, bool> selector, Func<CreatureStats, Vector3> positionResolver)
    {
        if (parent == null) return;

        foreach (var creature in _persistentCreatures.Where(selector))
        {
            if (!GodotObject.IsInstanceValid(creature)) continue;
            ReparentPreservingTransform(creature, parent);
            if (creature is Node3D creatureNode)
            {
                creatureNode.GlobalPosition = positionResolver(creature);
            }
        }
    }

    /// <summary>
    /// Move all tracked creatures to a target parent.
    /// </summary>
    public void RestoreAllCreatures(Node3D parent)
    {
        if (parent == null) return;

        foreach (var creature in _persistentCreatures)
        {
            if (!GodotObject.IsInstanceValid(creature)) continue;
            ReparentPreservingTransform(creature, parent);
        }
    }

    /// <summary>
    /// Reparent a node but keep its world position/rotation.
    /// </summary>
    private static void ReparentPreservingTransform(GridNode child, GridNode newParent)
    {
        if (child == null || newParent == null || child.GetParent() == null) return;

        Transform3D previousTransform = Transform3D.Identity;
        bool hasTransform = child is Node3D;

        if (hasTransform)
        {
            previousTransform = ((Node3D)child).GlobalTransform;
        }

        child.GetParent().RemoveChild(child);
        newParent.AddChild(child);

        if (hasTransform)
        {
            ((Node3D)child).GlobalTransform = previousTransform;
        }
    }
}
