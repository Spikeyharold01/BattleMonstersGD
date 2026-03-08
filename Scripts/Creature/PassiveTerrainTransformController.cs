using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: PassiveTerrainTransformController.cs
// PURPOSE: Monitors the grid and automatically applies TerrainTransformRules (visuals, stats, buffs).
// ATTACH TO: Creature prefabs that change form based on environment (e.g., Akhlut).
// =================================================================================================
public partial class PassiveTerrainTransformController : Godot.Node
{
    [Export]
    [Tooltip("List of terrain transformation rules this creature obeys.")]
    public Godot.Collections.Array<TerrainTransformRule_SO> Rules = new();

    private CreatureStats myStats;
    private TerrainType currentTerrain = (TerrainType)(-1); // Uninitialized state
    
    // State tracking for restoration
    private CreatureTemplate_SO originalTemplate;
    private Node3D originalVisuals;
    private Node3D currentVisualOverride;
    private TerrainTransformRule_SO activeRule;

    public override void _Ready()
    {
        myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
        
        // Cache original state for when we leave the terrain
        if (myStats != null)
        {
            originalTemplate = myStats.Template;
            originalVisuals = myStats.GetNodeOrNull<Node3D>("Visuals") ?? myStats.GetNodeOrNull<Node3D>("MeshInstance3D");
        }
    }

    public override void _Process(double delta)
    {
        if (myStats == null || GridManager.Instance == null || Rules.Count == 0) return;

        GridNode currentNode = GridManager.Instance.NodeFromWorldPoint(myStats.GlobalPosition);
        if (currentNode == null) return;

        TerrainType newTerrain = currentNode.terrainType;

        // Did the terrain change?
        if (newTerrain != currentTerrain)
        {
            HandleTerrainTransition(currentTerrain, newTerrain);
            currentTerrain = newTerrain;
        }
    }

    private void HandleTerrainTransition(TerrainType oldTerrain, TerrainType newTerrain)
    {
        // 1. Process Exit of old terrain
        if (activeRule != null && activeRule.TargetTerrain == oldTerrain)
        {
            GD.PrintRich($"[color=cyan]{myStats.Name} exits {oldTerrain} and reverts to its natural form.[/color]");
            
            // Revert Template
            if (activeRule.TemplateModifier != null)
            {
                myStats.Template = originalTemplate;
                myStats.ApplyTemplate();
            }

            // Revert Visuals
            if (currentVisualOverride != null)
            {
                currentVisualOverride.QueueFree();
                currentVisualOverride = null;
                if (originalVisuals != null) originalVisuals.Visible = true;
            }

            // Apply Exit Buff
            if (activeRule.BuffOnExit != null)
            {
                myStats.MyEffects.AddEffect((StatusEffect_SO)activeRule.BuffOnExit.Duplicate(), myStats);
            }

            activeRule = null;
        }

        // 2. Process Entry into new terrain
        var newRule = Rules.FirstOrDefault(r => r.TargetTerrain == newTerrain);
        if (newRule != null)
        {
            GD.PrintRich($"[color=cyan]{myStats.Name} enters {newTerrain} and transforms![/color]");
            activeRule = newRule;

            // Apply Template Modifier
            if (activeRule.TemplateModifier != null)
            {
                myStats.ApplyTemplateModifier(activeRule.TemplateModifier);
            }

            // Apply Visuals
            if (activeRule.VisualPrefabOverride != null)
            {
                if (originalVisuals != null) originalVisuals.Visible = false;
                
                currentVisualOverride = activeRule.VisualPrefabOverride.Instantiate<Node3D>();
                myStats.AddChild(currentVisualOverride);
                currentVisualOverride.Position = Vector3.Zero;
            }

            // Apply Enter Buff
            if (activeRule.BuffOnEnter != null)
            {
                myStats.MyEffects.AddEffect((StatusEffect_SO)activeRule.BuffOnEnter.Duplicate(), myStats);
            }
        }
    }

    public override void _ExitTree()
    {
        // Clean up visual overrides to prevent memory leaks if destroyed abruptly
        if (currentVisualOverride != null && GodotObject.IsInstanceValid(currentVisualOverride))
        {
            currentVisualOverride.QueueFree();
        }
    }
}