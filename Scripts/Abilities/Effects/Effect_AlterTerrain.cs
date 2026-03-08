using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_AlterTerrain.cs (GODOT VERSION)
// PURPOSE: Instantly changes terrain properties (Type, Cost, Tags) in an area.
// USED BY: Create Water, Transmute Rock to Mud, Soften Earth and Stone.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_AlterTerrain : AbilityEffectComponent
{
    [ExportGroup("Terrain Changes")]
    [Export] public TerrainType NewTerrainType = TerrainType.Ground; // Or Water if deep enough
    [Export] public bool ChangeTerrainType = false;
    
    [Export] public int MovementCostChange = 0; // +1 for difficult terrain
    [Export] public bool ApplyMovementCost = false;

    [Export] public Godot.Collections.Array<string> TagsToAdd = new();
    [Export] public Godot.Collections.Array<string> TagsToRemove = new();

    [ExportGroup("Hazard Logic")]
    [Export] public bool ExtinguishesFire = false;
    [Export] public int WaterDepthChange = 0; // +1 depth

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (GridManager.Instance == null) return;
        
        GridNode centerNode = GridManager.Instance.NodeFromWorldPoint(context.AimPoint);
        if (centerNode == null) return;

        float radius = ability.AreaOfEffect.Range;
        int radiusInNodes = Mathf.CeilToInt(radius / GridManager.Instance.nodeDiameter);

        // Iterate area
        // Reusing GridManager neighbor logic or simple loop? 
        // Simple loop is best for burst area.
        
        for (int x = -radiusInNodes; x <= radiusInNodes; x++)
        for (int y = -radiusInNodes; y <= radiusInNodes; y++) // Height check? Usually planar or sphere.
        for (int z = -radiusInNodes; z <= radiusInNodes; z++)
        {
            Vector3 offset = new Vector3(x, y, z) * GridManager.Instance.nodeDiameter;
            Vector3 checkPos = centerNode.worldPosition + offset;
            
            // Check distance
            if (checkPos.DistanceTo(context.AimPoint) > radius) continue;

            GridNode node = GridManager.Instance.NodeFromWorldPoint(checkPos);
            if (node == null || node.terrainType == TerrainType.Solid) continue;

            // APPLY CHANGES
            if (ChangeTerrainType) node.terrainType = NewTerrainType;
            if (ApplyMovementCost) node.movementCost += MovementCostChange;
            
            foreach (string tag in TagsToAdd) 
                if (!node.environmentalTags.Contains(tag)) node.environmentalTags.Add(tag);
                
            foreach (string tag in TagsToRemove) 
                node.environmentalTags.Remove(tag);

            if (WaterDepthChange != 0)
            {
                if (node.terrainType == TerrainType.Water || node.terrainType == TerrainType.Ground)
                {
                    if (node.waterDepth == -1) node.waterDepth = 0; // Start pool
                    node.waterDepth += WaterDepthChange;
                    if (node.waterDepth >= 0) node.terrainType = TerrainType.Water; // Turn ground to water if depth accumulates
                }
            }
        }

        // Extinguish Fire Logic
        if (ExtinguishesFire && FireManager.Instance != null)
        {
            FireManager.Instance.ExtinguishFireInArea(context.AimPoint, radius);
        }
        
        GD.Print($"{context.Caster.Name} altered terrain at {context.AimPoint}.");
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        // AI Logic:
        float score = 0;

        if (ExtinguishesFire && FireManager.Instance != null)
        {
            // 1. Is there fire in the target area?
            // We sample the aim point and radius.
            bool isTargetAreaBurning = false;
            
            // Check a few points or center
            if (FireManager.Instance.IsPositionOnFire(context.AimPoint)) isTargetAreaBurning = true;
            
            if (isTargetAreaBurning)
            {
                // 2. Does this fire threaten me or allies?
                // Check if any ally is IN or ADJACENT to the fire.
                var allies = AISpatialAnalysis.FindAllies(context.Caster);
                allies.Add(context.Caster); // Include self

                foreach(var ally in allies)
                {
                    float dist = ally.GlobalPosition.DistanceTo(context.AimPoint);
                    if (dist <= ability.AreaOfEffect.Range + 5f) // Adjacent or inside
                    {
                        score += 50f; // High value to clear fire near friends
                    }
                }

                // 3. Is the fire blocking a path?
                // Hard to calc exactly without pathfinding query, but we can guess.
                // If fire is between me and my target?
                var primaryTarget = context.Caster.GetNode<AIController>("AIController")?.GetPerceivedHighestThreat();
                if (primaryTarget != null)
                {
                    float distToTarget = context.Caster.GlobalPosition.DistanceTo(primaryTarget.GlobalPosition);
                    float distToFire = context.Caster.GlobalPosition.DistanceTo(context.AimPoint);
                    float fireToTarget = context.AimPoint.DistanceTo(primaryTarget.GlobalPosition);
                    
                    // Simple line check: Dist(Me->Fire) + Dist(Fire->Target) ~ Dist(Me->Target)
                    if (Mathf.Abs((distToFire + fireToTarget) - distToTarget) < 5f)
                    {
                        score += 30f; // Clearing a path
                    }
                }
            }
        }
        
        return score;
    }
}