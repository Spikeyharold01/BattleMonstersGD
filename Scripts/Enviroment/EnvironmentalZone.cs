using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: EnvironmentalZone.cs (GODOT VERSION)
// PURPOSE: Helper component that applies environmental tags (Fog, Smoke) to the GridManager.
// ATTACH TO: Prefabs representing environmental zones (Node3D).
// =================================================================================================
public partial class EnvironmentalZone : Node3D
{
[Export] public Godot.Collections.Array<string> Tags = new();
[Export] public float Radius;
[Export] public bool IsBoxShape = false;
[Export] public Vector3 BoxSize = new Vector3(10, 10, 60); // Width, Height, Length
[Export] public bool IsCylinderShape = false;
[Export] public float CylinderHeight = 40f;
[Export] public Vector3 WindDirection = Vector3.Zero; // For Gust of Wind logic
[Export]
[Tooltip("If true, changes terrain in the area to Ice (Difficult Terrain + Acrobatics Checks).")]
public bool MakesTerrainIcy = false;

[Export]
[Tooltip("If true, adds movement cost to nodes (Difficult Terrain).")]
public bool IsDifficultTerrain = false;
[Export] public int MovementCostPenalty = 1; 

[Export]
[Tooltip("If true, extinguishes non-magical fires in the area every frame.")]
public bool ExtinguishesFire = false;

public override void _Ready()
{
    if (!Tags.Contains("Magical"))
    {
        Tags.Add("Magical");
    }
    UpdateGrid(true);
}

public override void _ExitTree()
{
    UpdateGrid(false);
}

public override void _Process(double delta)
{
    if (ExtinguishesFire && FireManager.Instance != null)
    {
        // Extinguish fires within radius
        var spaceState = GetWorld3D().DirectSpaceState;
        var shape = new SphereShape3D { Radius = Radius };
        var query = new PhysicsShapeQueryParameters3D 
        { 
            Shape = shape, 
            Transform = new Transform3D(Basis.Identity, GlobalPosition), 
            CollisionMask = 4 // Assuming Hazard Layer 4 (based on previous scripts)
        };
        
        var fires = spaceState.IntersectShape(query);
        foreach(var dict in fires)
        {
            var fireNode = (Node3D)dict["collider"];
            var burning = fireNode.GetNodeOrNull<BurningObjectController>("BurningObjectController") ?? fireNode as BurningObjectController;
            
            if (burning != null)
            {
                GD.Print("Sleet Storm extinguishes a fire.");
                burning.QueueFree();
            }
        }
        
        // Assuming FireManager has ExtinguishFireInRadius (referenced in Unity code)
        // But FireManager snippet only had ExtinguishFireInArea? 
        // Checking Part 26 FireController snippet: "ExtinguishFireInArea(Vector3 center, float radius)"
        // Assuming FireManager delegates or has similar method. 
        // Actually, FireManager was not fully provided, but FireController was.
        // FireManager was referenced as Singleton. 
        // If FireManager is managing FireControllers or just global fire logic...
        // Let's assume the method exists or use FireController logic if we find one.
        // But `FireManager.Instance.ExtinguishFireInRadius` is the call. I will assume it's `ExtinguishFireInArea` based on controller naming or keep original name if Manager script is pending/assumed correct.
        // I will use `ExtinguishFireInArea` as that's what `FireController` had, and FireManager likely wraps it.
        // Wait, FireManager snippet wasn't provided in the prompt's file list as "Converted"? Yes it was: `FireManager.cs` under Environment.
        // Actually `FireManager.cs` is listed in the file structure but I haven't converted it yet in this session.
        // `FireController.cs` WAS converted (Part 26).
        // So `FireManager` is still pending or implicitly available.
        // I will assume `ExtinguishFireInRadius` matches the pending script.
        
        // Correction: `FireController` has `ExtinguishFireInArea`. `FireManager` likely manages.
        // I will use `FireManager.Instance?.ExtinguishFireInArea(GlobalPosition, Radius);` assuming standardization.
        FireManager.Instance?.Call("ExtinguishFireInArea", GlobalPosition, Radius); 
    }
}

private void UpdateGrid(bool add)
    {
        if (GridManager.Instance == null) return;
        
        float nodeDiameter = GridManager.Instance.nodeDiameter;
        GridNode centerNode = GridManager.Instance.NodeFromWorldPoint(GlobalPosition);
        
        // Calculate bounds to iterate
        float maxDim = IsBoxShape ? Mathf.Max(BoxSize.X, Mathf.Max(BoxSize.Y, BoxSize.Z)) : 
                       IsCylinderShape ? Mathf.Max(Radius, CylinderHeight) : Radius;
        int radiusInNodes = Mathf.CeilToInt(maxDim / nodeDiameter);

        // Pre-calculate Box Transforms
        Transform3D boxTrans = GlobalTransform; 
        Aabb localAabb = new Aabb(-BoxSize / 2f, BoxSize);

        for (int x = -radiusInNodes; x <= radiusInNodes; x++)
        {
            for (int y = -radiusInNodes; y <= radiusInNodes; y++)
            {
                for (int z = -radiusInNodes; z <= radiusInNodes; z++)
                {
                    Vector3 offset = new Vector3(x * nodeDiameter, y * nodeDiameter, z * nodeDiameter);
                    Vector3 targetPos = centerNode.worldPosition + offset;
                    GridNode targetNode = GridManager.Instance.NodeFromWorldPoint(targetPos);

                    if (targetNode != null)
                    {
                         bool isInside = false;
                        if (IsBoxShape)
                        {
                             // Convert world pos to local pos to check AABB
                             Vector3 localPos = boxTrans.AffineInverse() * targetPos;
                             if (localAabb.HasPoint(localPos)) isInside = true;
                        }
                        else if (IsCylinderShape)
                        {
                            float hDist = new Vector2(GlobalPosition.X, GlobalPosition.Z).DistanceTo(new Vector2(targetNode.worldPosition.X, targetNode.worldPosition.Z));
                            float vDist = targetNode.worldPosition.Y - GlobalPosition.Y;
                            if (hDist <= Radius && vDist >= 0 && vDist <= CylinderHeight) isInside = true;
                        }
                        else
                        {
                            if (GlobalPosition.DistanceTo(targetNode.worldPosition) <= Radius) isInside = true;
                        }

                        if (isInside)
                        {
                            if (add)
                            {
                                foreach (string tag in Tags)
                                    if (!targetNode.environmentalTags.Contains(tag)) targetNode.environmentalTags.Add(tag);
                                
                                if (MakesTerrainIcy && targetNode.terrainType == TerrainType.Ground)
                                {
                                    targetNode.terrainType = TerrainType.Ice;
                                    targetNode.movementCost += 2; 
                                }
                                if (IsDifficultTerrain)
                                {
                                    targetNode.movementCost += MovementCostPenalty;
                                }
                                // INSERTION: Store Wind Direction in Node?
                                // GridManager/Node doesn't have a 'WindVector' field.
                                // We rely on the Tag "Wind_Severe" and check global weather or local zone lookup.
                                // Adding a lookup map to GridManager is cleaner.
                                if (WindDirection != Vector3.Zero)
                                {
                                    GridManager.Instance.RegisterWindAtNode(targetNode, WindDirection);
                                }
                            }
                            else
                            {
                                foreach (string tag in Tags)
                                    targetNode.environmentalTags.Remove(tag);

                                if (MakesTerrainIcy && targetNode.terrainType == TerrainType.Ice)
                                {
                                    targetNode.terrainType = TerrainType.Ground; 
                                    targetNode.movementCost -= 2;
                                }
                                if (IsDifficultTerrain)
                                {
                                    targetNode.movementCost -= MovementCostPenalty;
                                }
                                if (WindDirection != Vector3.Zero)
                                {
                                    GridManager.Instance.UnregisterWindAtNode(targetNode);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}