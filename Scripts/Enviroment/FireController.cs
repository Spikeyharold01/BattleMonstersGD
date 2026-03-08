using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: FireController.cs (GODOT VERSION)
// PURPOSE: Controls fire spread and smoke logic for a specific fire instance or zone.
// =================================================================================================

public partial class FireController : Godot.Node
{
    // A set of all nodes currently on fire. Using a HashSet for fast lookups.
    private HashSet<GridNode> burningNodes = new HashSet<GridNode>();
    private HashSet<GridNode> smokedNodes = new HashSet<GridNode>();
    private HashSet<GridNode> extinguishedNodes = new HashSet<GridNode>();

    private float spreadTimer = 6.0f; // Fire spreads once per round (6 seconds)

    public void StartFireAt(GridNode startNode)
    {
        if (IsFlammable(startNode))
        {
            IgniteNode(startNode);
        }
    }

    public override void _Process(double delta)
    {
        spreadTimer -= (float)delta;
        if (spreadTimer <= 0)
        {
            spreadTimer = 6.0f;
            SpreadFire();
            UpdateSmoke();
        }
    }

    private void SpreadFire()
    {
        int roll = Dice.Roll(1, 20);
        
        if ((roll >= 7 && roll <= 8) || (roll >= 14 && roll <= 18) || roll == 1)
        {
            GD.Print("Fire does not grow this round.");
            return;
        }

        var growthInstructions = new List<(Vector3 direction, int squares)>();

        if (roll >= 2 && roll <= 5) growthInstructions.Add((GetDirectionFromRoll(roll), 1));
        else if (roll == 6) growthInstructions.Add((Vector3.Zero, 1)); // All directions flag
        else if (roll >= 9 && roll <= 12) growthInstructions.Add((GetDirectionFromRoll(roll), 2));
        else if (roll == 13) growthInstructions.Add((Vector3.Zero, 2));
        else if (roll == 19) growthInstructions.Add((Vector3.Zero, 3));
        else if (roll == 20) growthInstructions.Add((Vector3.Zero, 4));

        var nodesToIgnite = new HashSet<GridNode>();
        var currentFlames = new List<GridNode>(burningNodes);

        foreach (var instruction in growthInstructions)
        {
            foreach (var flameNode in currentFlames)
            {
                if (instruction.direction == Vector3.Zero) // All directions
                {
                    // Get all 26 neighbors (up, down, sideways, diagonals)
                    foreach (var neighbor in GridManager.Instance.GetNeighbours(flameNode))
                    {
                        if (CanFireSpreadTo(neighbor))
                        {
                            nodesToIgnite.Add(neighbor);
                        }
                    }
                }
                else // Specific direction
                {
                    GridNode currentNode = flameNode;
                    for (int i = 0; i < instruction.squares; i++)
                    {
                        GridNode nextNode = GridManager.Instance.NodeFromWorldPoint(currentNode.worldPosition + instruction.direction * 5f);
                        if (nextNode != null && CanFireSpreadTo(nextNode))
                        {
                            nodesToIgnite.Add(nextNode);
                            currentNode = nextNode; // Continue spreading from the new point
                        }
                        else
                        {
                            break; // Path is blocked
                        }
                    }
                }
            }
        }

        foreach (var node in nodesToIgnite)
        {
            IgniteNode(node);
        }
    }

    private bool CanFireSpreadTo(GridNode node)
    {
        if (node == null || burningNodes.Contains(node) || extinguishedNodes.Contains(node))
            return false;
        
        // Fire cannot spread to Ground or empty Air. It needs a flammable object.
        if (node.terrainType == TerrainType.Ground || node.terrainType == TerrainType.Air)
        {
            // Check if there's an object in that node that is flammable
            var obj = GetObjectInNode(node);
            return obj != null && obj.IsFlammable;
        }
        
        // Canopies, Solid (if it's a flammable wall), etc. are inherently flammable.
        return true;
    }
    
    private Vector3 GetDirectionFromRoll(int roll)
    {
        // Remap rolls to directions: 2=N, 3=E, 4=S, 5=W, 9=Up, 10=Down
        // Godot: Forward is -Z (North), Right is +X (East), Back is +Z (South), Left is -X (West)
        // Up is +Y, Down is -Y
        switch (roll)
        {
            case 2: return Vector3.Forward;  // North (-Z)
            case 3: return Vector3.Right;    // East (+X)
            case 4: return Vector3.Back;     // South (+Z)
            case 5: return Vector3.Left;     // West (-X)
            case 9: return Vector3.Up;       // Up (+Y)
            case 10: return Vector3.Down;    // Down (-Y)
            default: return Vector3.Zero;
        }
    }
    
    private void IgniteNode(GridNode node)
    {
        burningNodes.Add(node);
        // Damage object in the node
        var objectDurability = GetObjectInNode(node);
        if (objectDurability != null)
        {
            objectDurability.TakeDamage(Dice.Roll(3, 6), "Fire"); // Fire deals damage to structures
        }
    }
    
    private void UpdateSmoke()
    {
        smokedNodes.Clear();
        foreach (var flameNode in burningNodes)
        {
            foreach (var neighbor in GridManager.Instance.GetNeighbours(flameNode))
            {
                if (!burningNodes.Contains(neighbor))
                {
                    smokedNodes.Add(neighbor);
                }
            }
        }
    }

    public bool IsNodeOnFire(GridNode node) => burningNodes.Contains(node);
    public bool IsNodeSmoky(GridNode node) => smokedNodes.Contains(node);
    
    // Helper methods stub logic
    private bool IsFlammable(GridNode node) 
    { 
        // Real implementation would check GridManager tags or Object properties
        return true; 
    }

    private ObjectDurability GetObjectInNode(GridNode node) 
    { 
        // In Godot, you would query the Grid or Physics space at the Node's position
        // Placeholder return
        return null; 
    }

    /// <summary>
    /// Extinguishes fire in a given area. Called by water/cold spells.
    /// </summary>
    public void ExtinguishFireInArea(Vector3 center, float radius)
    {
        var nodesToExtinguish = new List<GridNode>();
        foreach (var node in burningNodes)
        {
            if (center.DistanceTo(node.worldPosition) <= radius)
            {
                nodesToExtinguish.Add(node);
            }
        }

        foreach (var node in nodesToExtinguish)
        {
            burningNodes.Remove(node);
            extinguishedNodes.Add(node); // Mark as doused so it can't reignite
            GD.Print($"Fire at {node.worldPosition} was extinguished.");
        }
    }
}