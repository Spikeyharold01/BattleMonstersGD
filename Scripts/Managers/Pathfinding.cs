using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: Pathfinding.cs (GODOT VERSION)
// PURPOSE: A static-like A* algorithm to find paths on the grid.
// ATTACH TO: A persistent "GameManager" Node or Autoload.
// =================================================================================================

/// <summary>
/// Provides a centralized, static-like A* pathfinding service for the game.
/// It finds paths on the 3D grid managed by GridManager, taking into account
/// the specific movement capabilities of the creature requesting the path.
/// </summary>
public partial class Pathfinding : GridNode
{
    public static Pathfinding Instance { get; private set; }

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

    /// <summary>
    /// The main public method to find a path from a start to a target position.
    /// Implements the A* pathfinding algorithm.
    /// </summary>
    public List<Vector3> FindPath(CreatureStats mover, Vector3 startPos, Vector3 targetPos, bool ignoreCosts = false)
    {
        if (mover.IsMounted)
        {
            // Assuming MyMount is a CreatureStats reference
            // Property access should be PascalCase in C# standard, assuming conversion
            mover = mover.MyMount;
        }

        // 1. INITIALIZATION
        if (CombatBoundaryService.CurrentMode == CombatBoundaryMode.ArenaLocked && !CombatBoundaryService.IsInsideCombatBounds(targetPos))
        {
            // Arena borders are absolute: no path can be produced when the requested destination lies outside the map.
            return null;
        }

        GridNode startNode = GridManager.Instance.NodeFromWorldPoint(startPos);
        GridNode targetNode = GridManager.Instance.NodeFromWorldPoint(targetPos);

        List<GridNode> openSet = new List<GridNode>();
        HashSet<GridNode> closedSet = new HashSet<GridNode>();
        
        openSet.Add(startNode);

        // 2. MAIN LOOP
        while (openSet.Count > 0)
        {
            GridNode currentNode = openSet[0];
            for (int i = 1; i < openSet.Count; i++)
            {
                if (openSet[i].fCost < currentNode.fCost || openSet[i].fCost == currentNode.fCost && openSet[i].hCost < currentNode.hCost)
                {
                    currentNode = openSet[i];
                }
            }

            openSet.Remove(currentNode);
            closedSet.Add(currentNode);

            // 3. TARGET FOUND
            if (currentNode == targetNode)
            {
                return RetracePath(startNode, targetNode);
            }

            // 4. NEIGHBOR PROCESSING
            foreach (GridNode neighbour in GridManager.Instance.GetNeighbours(currentNode))
            {
                if (!IsNodeWalkable(mover, currentNode, neighbour) || closedSet.Contains(neighbour) || IsSquareOccupied(neighbour, mover))
                {
                    continue;
                }

                if (CombatBoundaryService.CurrentMode == CombatBoundaryMode.ArenaLocked && GridManager.Instance.IsEdgeNode(neighbour))
                {
                    // Arena mode treats edge tiles as blocked so pathing naturally stays away from permeable boundaries.
                    continue;
                }

                int cost = GetMovementCost(mover, currentNode, neighbour);
                
                if (!ignoreCosts)
                {
                    int terrainCost = neighbour.movementCost;
                    
                    // --- ARCTIC STRIDE & SNOW WALKING LOGIC ---
                    if (mover.Template.SpecialQualities.Contains("Arctic Stride") || mover.MyEffects.IgnoresSnowAndIceMovementPenalty())
                    {
                        bool isArctic = neighbour.environmentalTags.Contains("Snow") || neighbour.environmentalTags.Contains("Ice") || neighbour.terrainType == TerrainType.Ice;
                        bool isMagical = neighbour.environmentalTags.Contains("Magical");
                        
                        if (isArctic && !isMagical)
                        {
                            terrainCost = neighbour.baseMovementCost;
                        }
                    }
                    
                    // --- WATER WALKING LOGIC ---
                    if (mover.MyEffects.HasCondition(Condition.WaterWalking) && neighbour.terrainType == TerrainType.Water)
                    {
                        terrainCost = neighbour.baseMovementCost; // Treat water surface as firm ground
                    }

                    cost += terrainCost;
					
					// Freedom of Movement: Ignore magical impedance (movement cost penalties)
                    if (mover.MyEffects.HasCondition(Condition.FreedomOfMovement))
                    {
                         // If the terrain cost is higher than base (e.g. Web, Solid Fog), ignore the extra.
                         // neighbor.baseMovementCost is usually 0 in the Node constructor I provided earlier? 
                         // Check Node constructor: baseMovementCost = _cost. Default cost is 1? 
                         // Actually, in CreateGrid, hazardMask sets cost = 100.
                         // Web/Solid Fog usually increase cost.
                         
                         // Logic: If current cost > base cost, remove the difference.
                         int penalty = neighbour.movementCost - neighbour.baseMovementCost;
                         if (penalty > 0)
                         {
                             cost -= penalty;
                         }
                    }
                }
                
                int newMovementCostToNeighbour = currentNode.gCost + cost;

                if (newMovementCostToNeighbour < neighbour.gCost || !openSet.Contains(neighbour))
                {
                    neighbour.gCost = newMovementCostToNeighbour;
                    neighbour.hCost = GetDistance(neighbour, targetNode);
                    neighbour.parent = currentNode;

                    if (!openSet.Contains(neighbour))
                        openSet.Add(neighbour);
                }
            }
        }
        
        return null;
    }

    private float GetMaxLongJumpDistance(CreatureStats mover)
    {
        int acrobaticsCheckResult = 20 + mover.GetSkillBonus(SkillType.Acrobatics);
        if (mover.Template.Speed_Land > 30f)
            acrobaticsCheckResult += 4 * Mathf.FloorToInt((mover.Template.Speed_Land - 30f) / 10f);
        else if (mover.Template.Speed_Land < 30f)
            acrobaticsCheckResult -= 4 * Mathf.FloorToInt((30f - mover.Template.Speed_Land) / 10f);

        return acrobaticsCheckResult;
    }

    private float GetMaxHighJumpHeight(CreatureStats mover)
    {
        int acrobaticsCheckResult = 20 + mover.GetSkillBonus(SkillType.Acrobatics);
        if (mover.Template.Speed_Land > 30f)
            acrobaticsCheckResult += 4 * Mathf.FloorToInt((mover.Template.Speed_Land - 30f) / 10f);
        else if (mover.Template.Speed_Land < 30f)
            acrobaticsCheckResult -= 4 * Mathf.FloorToInt((30f - mover.Template.Speed_Land) / 10f);
        
        return acrobaticsCheckResult / 4f;
    }

    public List<Vector3> FindChargePath(CreatureStats mover, Vector3 startPos, CreatureStats target)
    {
        CreatureStats pathingMover = mover.IsMounted ? mover.MyMount : mover;
        
        GridNode startNode = GridManager.Instance.NodeFromWorldPoint(startPos);
        GridNode targetNode = GridManager.Instance.NodeFromWorldPoint(target.GlobalPosition);

        GridNode destinationNode = null;
        var neighbours = GridManager.Instance.GetNeighbours(targetNode);
        
        foreach (GridNode neighbour in neighbours.OrderBy(n => n.worldPosition.DistanceTo(startPos)))
        {
            if (IsNodeWalkable(pathingMover, startNode, neighbour))
            {
                destinationNode = neighbour;
                break;
            }
        }

        if (destinationNode == null) return null;

        float chargeDistance = startNode.worldPosition.DistanceTo(destinationNode.worldPosition);
        if (chargeDistance < 10f) return null;

        // Assuming CreatureMover component exists
        var moverComp = mover.GetNodeOrNull<CreatureMover>("CreatureMover");
        float effectiveSpeed = moverComp != null ? moverComp.GetEffectiveMovementSpeed() : mover.Template.Speed_Land;
        
        var sprintAbility = mover.Template.KnownAbilities.FirstOrDefault(a => a.AbilityName.Contains("Sprint"));
        if (sprintAbility != null && (mover.MyUsage == null || mover.MyUsage.HasUsesRemaining(sprintAbility)))
        {
            var cooldownCtrl = mover.GetNodeOrNull<AbilityCooldownController>("AbilityCooldownController");
            if (cooldownCtrl == null || !cooldownCtrl.IsOnCooldown(sprintAbility))
            {
                var buff = sprintAbility.EffectComponents.OfType<ApplyStatusEffect>().FirstOrDefault()?.EffectToApply;
                var speedMod = buff?.Modifications.FirstOrDefault(m => m.StatToModify == StatToModify.Speed);
                if (speedMod != null) effectiveSpeed += speedMod.ModifierValue;
            }
        }
        
        float maxChargeDistance = effectiveSpeed * 2f;
        if (chargeDistance > maxChargeDistance) return null;
        
        Vector3 direction = (destinationNode.worldPosition - startNode.worldPosition).Normalized();
        int numSteps = Mathf.RoundToInt(chargeDistance / GridManager.Instance.nodeDiameter);

        for(int i = 1; i < numSteps; i++) 
        {
            Vector3 checkPoint = startNode.worldPosition + direction * (i * GridManager.Instance.nodeDiameter);
            GridNode nodeOnLine = GridManager.Instance.NodeFromWorldPoint(checkPoint);

            if (nodeOnLine.movementCost > 1 || nodeOnLine.terrainType == TerrainType.Solid || IsSquareOccupiedForCharge(nodeOnLine, mover, target))
            {
                return null;
            }
        }
        
        if (IsSquareOccupiedForCharge(destinationNode, mover, target))
        {
            return null;
        }

        List<Vector3> waypoints = new List<Vector3>();
        for (int i = 1; i <= numSteps; i++)
        {
             Vector3 waypoint = startNode.worldPosition + direction * (i * GridManager.Instance.nodeDiameter);
             waypoints.Add(GridManager.Instance.NodeFromWorldPoint(waypoint).worldPosition);
        }
        
        if (waypoints.Any())
        {
            waypoints[waypoints.Count-1] = destinationNode.worldPosition;
        } else {
             waypoints.Add(destinationNode.worldPosition);
        }

        return waypoints;
    }

    public static bool IsNodeWalkable(CreatureStats mover, GridNode fromNode, GridNode toNode)
    {
		// Check Restricted Zones
        var zones = GridManager.Instance.GetTree().GetNodesInGroup("RestrictedZone");
        foreach(GridNode n in zones)
        {
            if (n is RestrictedZoneController zone)
            {
                if (zone.IsPointRestricted(toNode.worldPosition, mover)) return false;
            }
        }
        if (IllusionManager.Instance != null && IllusionManager.Instance.IsNodeBlockedByIllusion(toNode, mover))
        {
            return false;
        }

        bool isSwarm = mover.Template.SubTypes.Contains("Swarm");

        MovementType moverType = mover.IsMounted ? mover.MyMount.Template.MovementType : mover.Template.MovementType;

        switch(moverType)
        {
            case MovementType.Ground:
            case MovementType.Swimming:
                if (toNode.terrainType == TerrainType.Solid && !mover.MyEffects.HasCondition(Condition.Incorporeal)) return false;
                if (toNode.terrainType == TerrainType.Air && fromNode.heightOfDropBelow == 0 && toNode.heightOfDropBelow > 0) return false;
                break;
            case MovementType.Flying:
                if (toNode.terrainType == TerrainType.Solid || toNode.terrainType == TerrainType.Water) return false;
                break;
            case MovementType.Burrowing:
                if (toNode.terrainType == TerrainType.Air || toNode.terrainType == TerrainType.Water) return false;
                break;
        }

        int dy = toNode.gridY - fromNode.gridY;

        if (dy > 0) // Moving UP
        {
            if (dy == 1) return true;
            if (mover.Template.Speed_Climb > 0) return true;
            
            float jumpHeight = dy * GridManager.Instance.nodeDiameter;
            if (jumpHeight <= Instance.GetMaxHighJumpHeight(mover)) return true;
            
            return false;
        }
        
        if (dy < 0) // Moving DOWN
        {
            float dropDistance = Mathf.Abs(dy) * GridManager.Instance.nodeDiameter;
            if (dropDistance <= mover.Template.Reach) return true;
            if (moverType == MovementType.Flying) return true;
            return true; 
        }

        if (dy == 0 && fromNode.terrainType == TerrainType.Ground && toNode.terrainType == TerrainType.Ground && toNode.heightOfDropBelow > 0)
        {
            float gapDistance = fromNode.worldPosition.DistanceTo(toNode.worldPosition);
            if (gapDistance <= mover.Template.Space) return true;
            if (gapDistance <= Instance.GetMaxLongJumpDistance(mover)) return true;
            return false;
        }

        if (isSwarm) return true;

        if (IsSquareOccupied(toNode, mover)) return false;

        return true;
    }
    
    private int GetMovementCost(CreatureStats mover, GridNode fromNode, GridNode toNode)
    {
        int dx = Mathf.Abs(fromNode.gridX - toNode.gridX);
        int dy = Mathf.Abs(fromNode.gridY - toNode.gridY);
        int dz = Mathf.Abs(fromNode.gridZ - toNode.gridZ);
        
        int distance = (dx + dy + dz) * 10;
        
        if (dx == 1 && dy == 1 && dz == 1) distance = 17;
        else if ((dx + dy + dz) > 1) distance = 14;

        if (toNode.isSlope && toNode.gridY > fromNode.gridY)
        {
            return distance + 10;
        }

        return distance;
    }

    List<Vector3> RetracePath(GridNode startNode, GridNode endNode)
    {
        List<GridNode> path = new List<GridNode>();
        GridNode currentNode = endNode;
        
        while (currentNode != startNode)
        {
            path.Add(currentNode);
            currentNode = currentNode.parent;
        }
        path.Reverse();

        List<Vector3> waypoints = new List<Vector3>();
        foreach (GridNode node in path) waypoints.Add(node.worldPosition);
        return waypoints;
    }

    int GetDistance(GridNode nodeA, GridNode nodeB)
    {
        int dstX = Mathf.Abs(nodeA.gridX - nodeB.gridX);
        int dstY = Mathf.Abs(nodeA.gridY - nodeB.gridY);
        int dstZ = Mathf.Abs(nodeA.gridZ - nodeB.gridZ);
        
        return (dstX + dstY + dstZ) * 10;
    }

    public static bool IsSquareOccupied(GridNode node, CreatureStats mover)
    {
        // Replicating OverlapSphere Logic
        uint creatureMask = 2; // Assuming Layer 2 is creatures, needs correct layer bit
        // Helper function for overlap check
        var spaceState = Instance.GetWorld3D().DirectSpaceState;
        var shape = new SphereShape3D { Radius = GridManager.Instance.nodeRadius * 0.9f };
        var query = new PhysicsShapeQueryParameters3D { Shape = shape, Transform = new Transform3D(Basis.Identity, node.worldPosition), CollisionMask = creatureMask };
        var occupants = spaceState.IntersectShape(query);

        foreach (var dict in occupants)
        {
            var collider = (Node3D)dict["collider"];
            CreatureStats occupantStats = collider.GetNodeOrNull<CreatureStats>("CreatureStats"); // Or get parent if collider is child
            // If collider is the creature root:
            if (occupantStats == null) occupantStats = collider as CreatureStats;

            if (occupantStats == null || occupantStats == mover || occupantStats == mover.MyMount) continue;

            // Faction Logic: "Player" vs "Enemy" groups
            bool moverIsPlayer = mover.IsInGroup("Player");
            bool occupantIsPlayer = occupantStats.IsInGroup("Player");

            // Pathing is blocked ONLY by opponents.
            if (moverIsPlayer != occupantIsPlayer)
            {
                var effects = occupantStats.GetNodeOrNull<StatusEffectController>("StatusEffectController");
                if (effects != null && effects.HasCondition(Condition.Helpless))
                {
                    continue; 
                }

                return true;
            }
        }
        return false;
    }
    
    private bool IsSquareOccupiedForCharge(GridNode node, CreatureStats mover, CreatureStats chargeTarget)
    {
        uint creatureMask = 2; // Needs actual mask
        var spaceState = GetWorld3D().DirectSpaceState;
        var shape = new SphereShape3D { Radius = GridManager.Instance.nodeRadius * 0.9f };
        var query = new PhysicsShapeQueryParameters3D { Shape = shape, Transform = new Transform3D(Basis.Identity, node.worldPosition), CollisionMask = creatureMask };
        var occupants = spaceState.IntersectShape(query);
        
        foreach (var dict in occupants)
        {
            var collider = (Node3D)dict["collider"];
            CreatureStats occupantStats = collider as CreatureStats ?? collider.GetNodeOrNull<CreatureStats>("CreatureStats");
            if (occupantStats == null) continue;

            if (occupantStats == mover || occupantStats == mover.MyMount || occupantStats == chargeTarget) continue;

            var effects = occupantStats.GetNodeOrNull<StatusEffectController>("StatusEffectController");
            if (effects != null && effects.HasCondition(Condition.Helpless))
            {
                continue;
            }

            return true;
        }
        
        return false;
    }

    public static float CalculatePathCost(List<Vector3> path, CreatureStats mover)
    {
        if (path == null || !path.Any()) return 0f;

        float totalCost = 0;
        GridNode previousNode = GridManager.Instance.NodeFromWorldPoint(mover.GlobalPosition);

        foreach (Vector3 waypoint in path)
        {
            GridNode currentNode = GridManager.Instance.NodeFromWorldPoint(waypoint);
            totalCost += Instance.GetMovementCost(mover, previousNode, currentNode) / 2f;
            previousNode = currentNode;
        }
        return totalCost;
    }
}