using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: PersistentEffect_ControlWater.cs (GODOT VERSION)
// PURPOSE: A runtime controller that alters the GridManager's terrain types for the duration
//          of a Control Water spell, then reverts the map to normal when it expires.
// =================================================================================================

public partial class PersistentEffect_ControlWater : Node3D
{
    private struct SavedNodeState
    {
        public TerrainType OriginalType;
        public int OriginalDepth;
    }

    private float _durationSeconds;
    private Dictionary<GridNode, SavedNodeState> _modifiedNodes = new Dictionary<GridNode, SavedNodeState>();

    public void Initialize(float widthAndLength, float height, bool isLowerWater, float durationSeconds)
    {
        _durationSeconds = durationSeconds;

        if (GridManager.Instance == null)
        {
            QueueFree();
            return;
        }

        // Create visual feedback
        CreateVisualBox(widthAndLength, height, isLowerWater);

        float nodeDiameter = GridManager.Instance.nodeDiameter;
        Vector3 minBounds = GlobalPosition - new Vector3(widthAndLength / 2f, height / 2f, widthAndLength / 2f);
        Vector3 maxBounds = GlobalPosition + new Vector3(widthAndLength / 2f, height / 2f, widthAndLength / 2f);

        HashSet<GridNode> uniqueNodes = new HashSet<GridNode>();

        // Step through the volume to gather unique grid nodes
        for (float x = minBounds.X; x <= maxBounds.X; x += nodeDiameter)
        {
            for (float y = minBounds.Y; y <= maxBounds.Y; y += nodeDiameter)
            {
                for (float z = minBounds.Z; z <= maxBounds.Z; z += nodeDiameter)
                {
                    GridNode gridNode = GridManager.Instance.NodeFromWorldPoint(new Vector3(x, y, z));
                    if (gridNode != null && gridNode.terrainType != TerrainType.Solid)
                    {
                        uniqueNodes.Add(gridNode);
                    }
                }
            }
        }

        // Apply changes and save original states
        foreach (GridNode node in uniqueNodes)
        {
            _modifiedNodes[node] = new SavedNodeState 
            { 
                OriginalType = node.terrainType, 
                OriginalDepth = node.waterDepth 
            };

            if (isLowerWater)
            {
                // Evaporate / Part the water
                if (node.terrainType == TerrainType.Water)
                {
                    node.terrainType = TerrainType.Air; // They will fall to the ground via CreatureMover if swimming
                    node.waterDepth = -1;
                }
            }
            else
            {
                // Flood the area
                node.terrainType = TerrainType.Water;
                node.waterDepth += Mathf.Max(1, Mathf.RoundToInt(height / nodeDiameter));
            }
        }
        
        AddToGroup("PersistentEffect");
    }

    private void CreateVisualBox(float width, float height, bool isLowerWater)
    {
        var meshInstance = new MeshInstance3D();
        var boxMesh = new BoxMesh { Size = new Vector3(width, height, width) };
        meshInstance.Mesh = boxMesh;

        var material = new StandardMaterial3D();
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        
        if (isLowerWater)
        {
            material.AlbedoColor = new Color(1f, 1f, 1f, 0.1f); // Faint white outline for void
        }
        else
        {
            material.AlbedoColor = new Color(0f, 0.4f, 0.8f, 0.4f); // Semi-transparent water blue
        }

        meshInstance.MaterialOverride = material;
        AddChild(meshInstance);
    }

    public override void _Process(double delta)
    {
        _durationSeconds -= (float)delta;
        if (_durationSeconds <= 0)
        {
            RevertGridAndDestroy();
        }
    }

    private void RevertGridAndDestroy()
    {
        if (GridManager.Instance != null)
        {
            foreach (var kvp in _modifiedNodes)
            {
                GridNode node = kvp.Key;
                SavedNodeState originalState = kvp.Value;

                node.terrainType = originalState.OriginalType;
                node.waterDepth = originalState.OriginalDepth;
            }
        }
        
        GD.Print("Control Water duration expired. Terrain reverted.");
        QueueFree();
    }

    public override void _ExitTree()
    {
        // Safety catch in case the scene ends or the node is forcefully deleted
        if (_durationSeconds > 0)
        {
            RevertGridAndDestroy();
        }
    }
}