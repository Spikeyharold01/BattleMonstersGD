using Godot;
// =================================================================================================
// FILE: FireManager.cs (GODOT VERSION)
// PURPOSE: Global manager for handling fire propagation.
// ATTACH TO: A persistent "GameManager" Node.
// =================================================================================================
public partial class FireManager : Node
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
        var fireObj = FireControllerPrefab.Instantiate<Godot.Node>();
        GetTree().CurrentScene.AddChild(fireObj); // Add to scene root
        activeFire = fireObj as FireController ?? fireObj.GetNodeOrNull<FireController>("."); 
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