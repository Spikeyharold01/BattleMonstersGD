using Godot;
// =================================================================================================
// FILE: FireManager.cs (GODOT VERSION)
// PURPOSE: Global manager for handling fire propagation.
// ATTACH TO: A persistent "GameManager" Node.
// =================================================================================================
public partial class FireManager : GridNode
{
public static FireManager Instance { get; private set; }

[Export] public PackedScene FireControllerPrefab;
private FireController activeFire;

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

public void StartNewFire(Vector3 position)
{
    if (activeFire == null)
    {
        var fireObj = FireControllerPrefab.Instantiate<GridNode>();
        GetTree().CurrentScene.AddChild(fireObj); // Add to scene root
        activeFire = fireObj as FireController ?? fireObj.GetNode<FireController>("FireController"); // Adjust path if script is on child
        // Since FireController IS a script, if attached to root of prefab, fireObj is it.
        // If prefab root is Node3D with script, cast works.
        if (activeFire == null)
        {
            // Assuming the prefab has the script on the root
            activeFire = (FireController)fireObj;
        }
    }
    GridNode startNode = GridManager.Instance.NodeFromWorldPoint(position);
    activeFire.StartFireAt(startNode);
}

public bool IsPositionOnFire(Vector3 position)
{
    return activeFire != null && activeFire.IsNodeOnFire(GridManager.Instance.NodeFromWorldPoint(position));
}

public bool IsPositionSmoky(Vector3 position)
{
    return activeFire != null && activeFire.IsNodeSmoky(GridManager.Instance.NodeFromWorldPoint(position));
}

public void ExtinguishFireInArea(Vector3 center, float radius)
{
    if (activeFire != null)
    {
        activeFire.ExtinguishFireInArea(center, radius);
    }
}
}