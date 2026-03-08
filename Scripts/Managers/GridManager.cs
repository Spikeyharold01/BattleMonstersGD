using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: GridManager.cs
// PURPOSE: Creates and manages the 3D grid data for pathfinding and world analysis.
// REVISED: Now scans for and applies 'environmentalTags' to nodes for use with Dirty Tricks.
// REVISED: Added GetNodesOccupiedByCreature to support advanced flanking and positioning rules.
// REVISED: Node class now stores dimensional data for cover (height, width) for advanced LoS checks.
// ATTACH TO: A persistent "GameManager" Node in the scene.
// =================================================================================================

/// <summary>
/// Represents the influence of a single light source on a single node.
/// </summary>
public struct LightSourceInfluence
{
    public int SourceID;
    public int IntensityChange;
    public int SpellLevel;
    public bool IsMythic;
}


/// <summary>
/// Represents a single point (or cell) in the 3D grid. It holds all the data
/// necessary for pathfinding, world analysis, and agent interaction.
/// </summary>
public class GridNode
{
    // --- Core Node Properties ---
    public TerrainType terrainType;     // The type of terrain this node represents (e.g., Ground, Water).
    public Vector3 worldPosition;       // The center position of this node in world space.
    public int gridX, gridY, gridZ;     // The integer coordinates of this node within the 3D grid array.
    public int movementCost;            // The current cost to move through this node. Can be modified by effects like weather.

    // --- Tactical Properties ---
    public bool providesCover;          // True if this node can provide cover to a character.
    public float coverHeight = 0f;      // The height of the cover object in this node.
    public float coverWidth = 0f;       // The widest horizontal dimension of the cover object in this node.
    public bool blocksLos;              // True if this node blocks Line of Sight (LOS).
    
    // --- REVISED: Light level is now a list of influences ---
    public List<LightSourceInfluence> lightInfluences = new List<LightSourceInfluence>();

    // --- Environmental Properties ---
    public int waterDepth = -1;         // The depth of the water at this node. -1 indicates not calculated or not water.
    //public int lightLevel = 15;       // The light level at this node (e.g., for stealth mechanics). Default is max light.
    public List<string> environmentalTags = new List<string>(); // Tags for interactable features (e.g., "LooseSand", "Debris").

    // --- A* Pathfinding Properties ---
    public int gCost;                   // The cost of the path from the start node to this node.
    public int hCost;                   // The heuristic (estimated) cost from this node to the end node.
    public GridNode parent;                 // The preceding node in the calculated path. Used to reconstruct the path.
    public int fCost => gCost + hCost;  // The total cost (gCost + hCost). This is a calculated property for convenience.
    public int baseMovementCost;        // The default movement cost of this node, before any modifiers (like weather) are applied.
    public TerrainProperties TerrainProperties; // Generic hazard profile used by tactical evaluators.

    // --- Verticality Properties (New Revision) ---
    // [Tooltip("Is this node part of a slope? (Movement cost x2)")] - Tooltips apply to Export fields in Godot
    public bool isSlope = false;        // True if this node is on a slope, which may increase movement cost.

    // [Tooltip("The height difference in grid units (Y) to the node directly below this one.")]
    public int heightOfDropBelow = 0;   // If there's a drop below this node, this stores its height in grid units. 0 means no drop.

    /// <summary>
    /// Constructor to create a new Node.
    /// </summary>
    public GridNode(TerrainType _terrainType, Vector3 _worldPos, int _gridX, int _gridY, int _gridZ, int _cost, bool _providesCover, bool _blocksLos)
    {
        terrainType = _terrainType;
        worldPosition = _worldPos;
        gridX = _gridX;
        gridY = _gridY;
        gridZ = _gridZ;
        baseMovementCost = _cost;
        movementCost = _cost; // Initially, current cost is the same as the base cost.
        this.providesCover = _providesCover;
        this.blocksLos = _blocksLos;
        TerrainProperties = ResolveTerrainProperties(_terrainType, _cost);
    }

    /// <summary>
    /// Builds a terrain profile that tactical logic can reason about without terrain-name branching.
    ///
    /// Expected output:
    /// - Every node exposes movement, visibility, and hazard intensity in one compact model.
    /// - Systems can compare lethality and risk without knowing any authored terrain label or biome name.
    /// </summary>
    private static TerrainProperties ResolveTerrainProperties(TerrainType terrainType, int cost)
    {
        TerrainProperties properties = new TerrainProperties
        {
            MovementCost = Mathf.Max(1f, cost),
            VisibilityModifier = 1f,
            HazardType = TerrainHazardType.None,
            HazardSeverity = 0f,
            LethalityScore = 0f
        };

        switch (terrainType)
        {
            case TerrainType.Water:
                properties.HazardType = TerrainHazardType.DrowningRisk;
                properties.HazardSeverity = 0.65f;
                properties.LethalityScore = 0.7f;
                properties.AppliesTo.Add("NonSwimmer");
                break;
            case TerrainType.Air:
                properties.HazardType = TerrainHazardType.FallRisk;
                properties.HazardSeverity = 0.5f;
                properties.LethalityScore = 0.45f;
                properties.AppliesTo.Add("NonFlying");
                break;
            case TerrainType.Ice:
                properties.HazardType = TerrainHazardType.GenericRisk;
                properties.HazardSeverity = 0.35f;
                properties.LethalityScore = 0.2f;
                properties.VisibilityModifier = 0.95f;
                properties.AppliesTo.Add("LowTraction");
                break;
            case TerrainType.Solid:
                properties.HazardType = TerrainHazardType.GenericRisk;
                properties.HazardSeverity = 1f;
                properties.LethalityScore = 1f;
                break;
        }

        return properties;
    }
}

/// <summary>
/// Manages the creation, storage, and access of the 3D grid of Nodes.
/// Implemented as a Singleton to ensure there's only one grid manager in the scene.
/// </summary>
public partial class GridManager : Node3D
{
	
	// Key: Node, Value: Wind Direction Vector
    private Dictionary<GridNode, Vector3> localWindMap = new Dictionary<GridNode, Vector3>();

    public void RegisterWindAtNode(GridNode node, Vector3 dir)
    {
        if (node == null) return;
        localWindMap[node] = dir;
    }

    public void UnregisterWindAtNode(GridNode node)
    {
        if (node == null) return;
        if (localWindMap.ContainsKey(node)) localWindMap.Remove(node);
    }

    public Vector3 GetWindAtNode(GridNode node)
    {
        if (node != null && localWindMap.ContainsKey(node)) return localWindMap[node];
        if (WeatherManager.Instance != null) return WeatherManager.Instance.CurrentWindDirection;
        return Vector3.Zero;
    }
    // Singleton instance for easy access from other scripts.
    public static GridManager Instance { get; private set; }

    [ExportGroup("Grid Configuration")]
    [Export] public Vector3 gridWorldSize;   // The total size of the grid in world units (e.g., 100x20x100 meters).
    [Export] public float nodeRadius;        // The radius of each individual node. The smaller the radius, the higher the grid resolution.
    public float nodeDiameter { get; private set; } // The diameter of a node, calculated from the radius.

    [ExportGroup("Layer Masks")]
    // LayerMasks are used to efficiently check for certain types of objects during grid creation.
    // In Godot, these are uint bitmasks.
    [Export(PropertyHint.Layers3DPhysics)] public uint unwalkableMask;    // Layer for objects that are completely solid (walls, terrain ground).
    [Export(PropertyHint.Layers3DPhysics)] public uint losBlockerMask;    // Layer for objects that block line of sight but might not be solid (e.g., thick foliage).
    [Export(PropertyHint.Layers3DPhysics)] public uint coverProviderMask; // Layer for objects that provide cover (e.g., low walls, crates).
    [Export(PropertyHint.Layers3DPhysics)] public uint waterMask;         // Layer for water volumes.
    [Export(PropertyHint.Layers3DPhysics)] public uint hazardMask;        // Layer for hazardous terrain that should have a high movement cost.
    [Export(PropertyHint.Layers3DPhysics)] public uint dirtyTrickInteractableMask; // Layer for environmental objects that can be used for dirty tricks (e.g., a "Sand" object).
    
    // The core data structure: a 3D array to store all the Node objects.
    private GridNode[,,] grid;
    // The dimensions of the grid in node counts, calculated from gridWorldSize and nodeDiameter.
    private int gridSizeX, gridSizeY, gridSizeZ;

    // Read-only accessors used by boundary-aware AI calculations.
    public int GridSizeX => gridSizeX;
    public int GridSizeY => gridSizeY;
    public int GridSizeZ => gridSizeZ;

    // --- CORRECTION ---
    // The declaration of this dictionary has been corrected to match its usage in the methods below.
    // It now correctly uses LightSourceController as the key and is named 'lightSourceAffectedNodesMap'.
    private Dictionary<LightSourceController, List<GridNode>> lightSourceAffectedNodesMap = new Dictionary<LightSourceController, List<GridNode>>();
    
    /// <summary>
    /// Awake/Ready is called when the script instance is being loaded.
    /// Used here to implement the Singleton pattern.
    /// </summary>
    public override void _Ready()
    {
        // If an instance already exists and it's not this one, destroy this one.
        if (Instance != null && Instance != this) 
        {
            QueueFree(); // Godot equivalent of Destroy(gameObject)
        }
        else // Otherwise, set the instance to this object.
        {
            Instance = this;
        }
    }

    /// <summary>
    /// The main function to generate the entire grid. It should be called once at the start of the game or level.
    /// </summary>
    public void CreateGrid()
    {
        // --- 1. Initialize Grid Dimensions ---
        nodeDiameter = nodeRadius * 2;
        gridSizeX = Mathf.RoundToInt(gridWorldSize.X / nodeDiameter);
        gridSizeY = Mathf.RoundToInt(gridWorldSize.Y / nodeDiameter);
        gridSizeZ = Mathf.RoundToInt(gridWorldSize.Z / nodeDiameter);
        grid = new GridNode[gridSizeX, gridSizeY, gridSizeZ];
        
        // Godot Coordinate Logic: 
        // Right is +X, Up is +Y, Forward is -Z (by default) or +Z depending on setup.
        // To maintain Unity Logic (Bottom Left corner start), we calculate from center.
        // Assuming GlobalPosition is center.
        Vector3 worldBottomLeft = GlobalPosition - Vector3.Right * gridWorldSize.X / 2 - Vector3.Up * gridWorldSize.Y / 2 - Vector3.Back * gridWorldSize.Z / 2;

        // --- 2. First Pass: Node Creation and Terrain Type Identification ---
        for (int x = 0; x < gridSizeX; x++)
        for (int y = 0; y < gridSizeY; y++)
        for (int z = 0; z < gridSizeZ; z++)
        {
            // Note: In Godot, Vector3.Forward is (0,0,-1) and Vector3.Back is (0,0,1).
            // Unity Vector3.forward is (0,0,1). 
            // We use Vector3.Back here to represent +Z movement to match Unity's loop logic if gridWorldSize.z corresponds to +Z axis.
            Vector3 worldPoint = worldBottomLeft + Vector3.Right * (x * nodeDiameter + nodeRadius) + Vector3.Up * (y * nodeDiameter + nodeRadius) + Vector3.Back * (z * nodeDiameter + nodeRadius);
            
            TerrainType nodeType = TerrainType.Air;
            int cost = 1;
            bool providesCover = false;
            bool blocksLos = false;

            // Physics Checks replacement
            if (CheckSphere(worldPoint, nodeRadius, unwalkableMask)) {
                nodeType = TerrainType.Solid;
                blocksLos = true;
            } else if (CheckSphere(worldPoint, nodeRadius, waterMask)) {
                nodeType = TerrainType.Water;
            } else if (CheckSphere(worldPoint, nodeRadius, losBlockerMask)) {
                nodeType = TerrainType.Canopy;
                blocksLos = true;
                providesCover = true;
            }
            else if (CheckRaycast(worldPoint, Vector3.Down, nodeRadius + 0.1f, unwalkableMask | waterMask))
            {
                nodeType = TerrainType.Ground;
            }

            if (CheckSphere(worldPoint, 0.2f, hazardMask)) {
                cost = 100;
            }
            
            grid[x, y, z] = new GridNode(nodeType, worldPoint, x, y, z, cost, providesCover, blocksLos);
            
            // --- REVISED: Scan for Cover Dimensions ---
            var coverColliders = OverlapSphere(worldPoint, nodeRadius, coverProviderMask);
            if (coverColliders.Count > 0)
            {
                var coverCollider = coverColliders[0]; // Assume first one is the main cover object
                grid[x, y, z].providesCover = true;
                
                // Get AABB from the visual instance or collision shape
                Aabb bounds = new Aabb();
                if (coverCollider is VisualInstance3D vis) bounds = vis.GetAabb();
                else if (coverCollider is CollisionObject3D colObj) 
                {
                    // Approximating bounds from the CollisionObject is tricky without specific shape info.
                    // We assume it has children shapes or we treat it as 1 unit for safety if complex.
                    // Ideally, pass the MeshInstance.
                    // For exact logic:
                    bounds = new Aabb(Vector3.Zero, Vector3.One); // Fallback
                }

                grid[x, y, z].coverHeight = bounds.Size.Y;
                grid[x, y, z].coverWidth = Mathf.Max(bounds.Size.X, bounds.Size.Z);
            }

            // --- Scan for Dirty Trick Interactables ---
            var interactables = OverlapSphere(worldPoint, nodeRadius, dirtyTrickInteractableMask);
            foreach(var interactable in interactables)
            {
                // In Godot, nodes act as the object. We check Groups instead of Tags usually, or Name.
                // Assuming "Tags" logic is implemented via Groups in Godot for this conversion.
                var groups = interactable.GetGroups();
                foreach(var g in groups)
                {
                    string tag = g.ToString();
                    if (!grid[x, y, z].environmentalTags.Contains(tag))
                    {
                        grid[x, y, z].environmentalTags.Add(tag);
                    }
                }
            }
        }
        
        // --- 3. Second Pass: Verticality Analysis (Slopes and Drops) ---
        for (int x = 0; x < gridSizeX; x++)
        for (int z = 0; z < gridSizeZ; z++)
        {
            for (int y = 1; y < gridSizeY; y++)
            {
                GridNode currentNode = grid[x, y, z];
                if (currentNode.terrainType != TerrainType.Ground) continue;

                GridNode nodeBelow = grid[x, y - 1, z];
                if (nodeBelow.terrainType == TerrainType.Air)
                {
                    int drop = 0;
                    for (int y_scan = y - 1; y_scan >= 0; y_scan--)
                    {
                        if (grid[x, y_scan, z].terrainType != TerrainType.Air) break;
                        drop++;
                    }
                    currentNode.heightOfDropBelow = drop;
                }

                CheckForSlope(currentNode, x - 1, y, z);
                CheckForSlope(currentNode, x + 1, y, z);
                CheckForSlope(currentNode, x, y, z - 1);
                CheckForSlope(currentNode, x, y, z + 1);
            }
        }
        
        // --- 4. Third Pass: Water Depth Calculation ---
        for (int x = 0; x < gridSizeX; x++)
        for (int z = 0; z < gridSizeZ; z++)
        {
            for (int y = gridSizeY - 2; y >= 0; y--)
            {
                GridNode currentNode = grid[x, y, z];
                if (currentNode.terrainType != TerrainType.Water) continue;

                GridNode nodeAbove = grid[x, y + 1, z];
                if (nodeAbove.terrainType != TerrainType.Water) {
                    currentNode.waterDepth = 0;
                } else {
                    if(nodeAbove.waterDepth != -1)
                        currentNode.waterDepth = nodeAbove.waterDepth + 1;
                }
            }
        }
    }
    
    #region Dynamic Lighting System
     public void AddLightSource(LightSourceController source)
    {
        if (lightSourceAffectedNodesMap.ContainsKey(source)) return;
        ApplyLightInfluence(source);
    }

    public void RemoveLightSource(LightSourceController source)
    {
        if (!lightSourceAffectedNodesMap.ContainsKey(source)) return;
        
        foreach (var node in lightSourceAffectedNodesMap[source])
        {
            node.lightInfluences.RemoveAll(i => i.SourceID == source.Info.SourceID);
        }
        lightSourceAffectedNodesMap.Remove(source);
    }

    public void UpdateLightSourcePosition(LightSourceController source)
    {
        RemoveLightSource(source);
        AddLightSource(source);
    }

    private void ApplyLightInfluence(LightSourceController source)
    {
        List<GridNode> affectedNodes = new List<GridNode>();
        var influence = new LightSourceInfluence
        {
            SourceID = source.Info.SourceID,
            IntensityChange = source.Info.Data.IntensityChange,
            SpellLevel = source.Info.SpellLevel,
            IsMythic = source.Info.IsMythic
        };
        
        GridNode centerNode = NodeFromWorldPoint(source.Info.WorldPosition);
        
        // Calculate max radius including outer ring
        int maxRadius = Mathf.CeilToInt(Mathf.Max(source.Info.Data.Radius, source.Info.Data.OuterRadius) / nodeDiameter);

        for (int x = -maxRadius; x <= maxRadius; x++)
        for (int y = -maxRadius; y <= maxRadius; y++)
        for (int z = -maxRadius; z <= maxRadius; z++)
        {
            int checkX = centerNode.gridX + x;
            int checkY = centerNode.gridY + y;
            int checkZ = centerNode.gridZ + z;

            if (checkX >= 0 && checkX < gridSizeX && checkY >= 0 && checkY < gridSizeY && checkZ >= 0 && checkZ < gridSizeZ)
            {
                GridNode targetNode = grid[checkX, checkY, checkZ];
                float distance = centerNode.worldPosition.DistanceTo(targetNode.worldPosition);

                // Skip if outside the largest radius or blocked by a wall
                if (distance > source.Info.Data.OuterRadius && distance > source.Info.Data.Radius) continue;
                if (CheckLinecast(source.Info.WorldPosition, targetNode.worldPosition, unwalkableMask)) continue;

                // Determine which influence to apply (inner or outer)
                LightSourceInfluence influenceToApply;
                if (source.Info.Data.OuterRadius > 0 && distance > source.Info.Data.Radius)
                {
                    // In the outer ring
                    influenceToApply = new LightSourceInfluence {
                        SourceID = source.Info.SourceID, 
                        IntensityChange = source.Info.Data.OuterIntensityChange, 
                        SpellLevel = source.Info.SpellLevel, 
                        IsMythic = source.Info.IsMythic 
                    };
                }
                else if (distance <= source.Info.Data.Radius)
                {
                    // In the inner ring
                    influenceToApply = influence;
                }
                else
                {
                    continue; // In a dead zone between radii, if any
                }

                targetNode.lightInfluences.Add(influenceToApply);
                affectedNodes.Add(targetNode);
            }
        }
        lightSourceAffectedNodesMap[source] = affectedNodes;
    }
    
    /// <summary>
    /// Calculates the effective light level at a node based on overlapping magical effects and global time.
    /// Returns 0 (Darkness), 1 (Dim), 2 (Normal), 3 (Bright).
    /// Handles Time of Day, Low-Light Vision, and Mythic overrides.
    /// </summary>
    public int GetEffectiveLightLevel(GridNode node, CreatureStats observer = null)
    {
        // 1. Get Base Global Light Level from TimeManager
        // Default to Normal (2) if TimeManager isn't set up yet.
        int baseLightLevel = (TimeManager.Instance != null) ? (int)TimeManager.Instance.GlobalLightLevel : 2;

        // 2. Check for Low-Light Vision Interaction with Moonlit Nights
        // Rule: "Characters with low-light vision can see outdoors on a moonlit night as well as they can during the day."
        if (observer != null && observer.Template.HasLowLightVision && TimeManager.Instance != null && TimeManager.Instance.IsMoonlitNight())
        {
            if (baseLightLevel == 1) baseLightLevel = 2; // Treat Dim Moonlight as Normal Day
        }

        // If the node is null or has no effects, return the ambient level immediately.
        if (node == null || !node.lightInfluences.Any())
        {
            return baseLightLevel;
        }

        // 3. Process Magical Light/Darkness Influences
        int highestLightSpellLevel = -1;
        int highestDarkSpellLevel = -1;
        
        LightSourceInfluence winningLightInfluence = default;
        LightSourceInfluence winningDarkInfluence = default;

        foreach (var influence in node.lightInfluences)
        {
            int intensity = influence.IntensityChange;

            // --- LOW-LIGHT VISION CHECK ---
            // Rule: "See twice as far in dim light."
            // If the observer has Low-Light Vision and this is a Dim Light effect (+1),
            // they perceive it as Normal Light (+2) relative to darkness.
            if (observer != null && observer.Template.HasLowLightVision && intensity == 1) 
            {
                intensity = 2; 
            }

            if (intensity > 0) // Light Effect
            {
                // We use >= here so that if levels are equal, we update the influence data 
                // (This ensures we capture the 'IsMythic' flag from the latest source)
                if (influence.SpellLevel >= highestLightSpellLevel)
                {
                    highestLightSpellLevel = influence.SpellLevel;
                    winningLightInfluence = influence;
                    winningLightInfluence.IntensityChange = intensity; // Apply our Low-Light modified intensity
                }
            }
            else if (intensity < 0) // Darkness Effect
            {
                if (influence.SpellLevel >= highestDarkSpellLevel)
                {
                    highestDarkSpellLevel = influence.SpellLevel;
                    winningDarkInfluence = influence;
                }
            }
        }

        // 4. Resolve Conflict (Light vs Darkness)
        
        // CASE A: Light is stronger than Darkness
        if (highestLightSpellLevel > highestDarkSpellLevel)
        {
            int finalLightLevel = baseLightLevel + winningLightInfluence.IntensityChange;

            // --- MYTHIC CHECK ---
            // Rule: Mythic light sources (like Mythic Daylight) often force the light level 
            // to be at least Normal (2), regardless of ambient conditions.
            if (winningLightInfluence.IsMythic)
            {
                finalLightLevel = Mathf.Max(finalLightLevel, 2);
            }

            return Mathf.Clamp(finalLightLevel, 0, 3);
        }
        // CASE B: Darkness is stronger than Light
        else if (highestDarkSpellLevel > highestLightSpellLevel)
        {
            int finalLightLevel = baseLightLevel + winningDarkInfluence.IntensityChange;
            return Mathf.Clamp(finalLightLevel, 0, 3);
        }
        // CASE C: Tie (or both are -1/None)
        else 
        {
            // In Pathfinder, equal level light/darkness spells counter and negate each other,
            // leaving the prevailing ambient light condition.
            return baseLightLevel;
        }
    }

    #endregion
    
    /// <summary>
    /// Checks if a node is currently being illuminated by a Mythic light effect.
    /// </summary>
    public bool IsNodeInMythicDaylight(GridNode node)
    {
        if (node == null || !node.lightInfluences.Any()) return false;
        
        // Check if any active, winning light influence on this node is from a mythic source.
        return node.lightInfluences.Any(i => i.IsMythic && i.IntensityChange > 0);
    }
    
    /// <summary>
    /// Checks if a node is currently within an area of supernatural darkness that blocks darkvision.
    /// </summary>
    public bool IsNodeInSupernaturalDarkness(GridNode node)
    {
        if (node == null || GetEffectiveLightLevel(node) > 0) return false; // Not in darkness

        // Find the most powerful darkness effect on the node
        var winningDarkInfluence = node.lightInfluences
            .Where(i => i.IntensityChange < 0)
            .OrderByDescending(i => i.SpellLevel)
            .FirstOrDefault();

        // Check if that winning effect is from a source marked as supernatural
        var winningLightSource = lightSourceAffectedNodesMap.Keys.FirstOrDefault(s => s.Info.SourceID == winningDarkInfluence.SourceID);

        return winningLightSource != null && winningLightSource.Info.Data.IsSupernaturalDarkness;
    }

    /// <summary>
    /// Checks if a neighboring position creates a slope with the current node.
    /// </summary>
    private void CheckForSlope(GridNode currentNode, int neighborX, int neighborY, int neighborZ)
    {
        if (neighborX < 0 || neighborX >= gridSizeX || neighborZ < 0 || neighborZ >= gridSizeZ) return;

        for (int y = neighborY + 1; y >= neighborY - 1; y--)
        {
            if (y < 0 || y >= gridSizeY) continue;

            GridNode neighborNode = grid[neighborX, y, neighborZ];
            if (neighborNode.terrainType == TerrainType.Ground)
            {
                if (Mathf.Abs(currentNode.gridY - neighborNode.gridY) == 1)
                {
                    currentNode.isSlope = true;
                    currentNode.baseMovementCost = 2;
                    currentNode.movementCost = 2;
                }
                return;
            }
        }
    }
    
    /// <summary>
    /// Updates the movement cost of nodes based on a weather effect.
    /// </summary>
   public void UpdateGridForWeather(Weather_SO weather)
    {
        if (grid == null || weather == null) return;
        
        // 1. Clear old weather tags/costs
        foreach (GridNode node in grid)
        {
            node.movementCost = node.baseMovementCost;
            node.environmentalTags.Remove("Snow");
            node.environmentalTags.Remove("Ice");
            node.environmentalTags.Remove("Rain");
        }

        // 2. Apply new weather
        foreach (GridNode node in grid)
        {
            if (weather.IsPrecipitation && node.terrainType == TerrainType.Ground)
            {
                node.movementCost += weather.MovementCostModifier;
                
                // Add Tags for Arctic Stride / Perception Logic
                if (weather.WeatherName.ToLower().Contains("snow") || weather.WeatherName.ToLower().Contains("blizzard"))
                {
                    node.environmentalTags.Add("Snow");
                }
                else if (weather.WeatherName.ToLower().Contains("ice") || weather.WeatherName.ToLower().Contains("hail"))
                {
                    node.environmentalTags.Add("Ice");
                }
            }
        }
        GD.Print("[GridManager] Updated grid movement costs/tags for new weather.");
    }
    
    /// <summary>
    /// Converts a world space position into the corresponding Node on the grid.
    /// </summary>
    public GridNode NodeFromWorldPoint(Vector3 worldPosition)
    {
        float percentX = (worldPosition.X - GlobalPosition.X + gridWorldSize.X / 2) / gridWorldSize.X;
        float percentY = (worldPosition.Y - GlobalPosition.Y + gridWorldSize.Y / 2) / gridWorldSize.Y;
        float percentZ = (worldPosition.Z - GlobalPosition.Z + gridWorldSize.Z / 2) / gridWorldSize.Z;
        
        percentX = Mathf.Clamp(percentX, 0, 1);
        percentY = Mathf.Clamp(percentY, 0, 1);
        percentZ = Mathf.Clamp(percentZ, 0, 1);

        int x = Mathf.Clamp(Mathf.RoundToInt((gridSizeX - 1) * percentX), 0, gridSizeX - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt((gridSizeY - 1) * percentY), 0, gridSizeY - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt((gridSizeZ - 1) * percentZ), 0, gridSizeZ - 1);
        
        return grid[x, y, z];
    }
    

    /// <summary>
    /// Returns true when a world position lies inside the configured grid world envelope without clamping.
    ///
    /// Expected output:
    /// - true means movement is still inside combat space.
    /// - false means the caller is attempting to cross a combat boundary.
    /// </summary>
    public bool IsWithinWorldBounds(Vector3 worldPosition)
    {
        Vector3 local = worldPosition - GlobalPosition;
        float halfX = gridWorldSize.X * 0.5f;
        float halfY = gridWorldSize.Y * 0.5f;
        float halfZ = gridWorldSize.Z * 0.5f;

        return local.X >= -halfX && local.X <= halfX &&
               local.Y >= -halfY && local.Y <= halfY &&
               local.Z >= -halfZ && local.Z <= halfZ;
    }

    /// <summary>
    /// Returns true when a node sits on the outermost ring of the active combat map.
    ///
    /// Expected output:
    /// - Arena pathfinding can treat this edge ring as blocked to prevent slipping across borders.
    /// - Travel can still use this helper for edge-pressure scoring and flee analysis.
    /// </summary>
    public bool IsEdgeNode(GridNode node)
    {
        if (node == null)
        {
            return false;
        }

        return node.gridX <= 0 || node.gridZ <= 0 || node.gridX >= gridSizeX - 1 || node.gridZ >= gridSizeZ - 1;
    }

    /// <summary>
    /// Gets a list of all 26 neighboring nodes for a given node (including diagonals).
    /// </summary>
    public List<GridNode> GetNeighbours(GridNode node)
    {
        List<GridNode> neighbours = new List<GridNode>();
        
        for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        for (int z = -1; z <= 1; z++)
        {
            if (x == 0 && y == 0 && z == 0) continue;

            int checkX = node.gridX + x;
            int checkY = node.gridY + y;
            int checkZ = node.gridZ + z;
            
            if (checkX >= 0 && checkX < gridSizeX && 
                checkY >= 0 && checkY < gridSizeY && 
                checkZ >= 0 && checkZ < gridSizeZ)
            {
                neighbours.Add(grid[checkX, checkY, checkZ]);
            }
        }
        return neighbours;
    }

    /// <summary>
    /// Gets a list of all nodes a creature currently occupies, based on its position and size.
    /// Crucial for flanking rules with large creatures.
    /// </summary>
    /// <param name="creature">The creature to get the occupied nodes for.</param>
    /// <returns>A list of all Node objects the creature is standing on.</returns>
    public List<GridNode> GetNodesOccupiedByCreature(CreatureStats creature)
    {
        List<GridNode> occupiedNodes = new List<GridNode>();
        if (creature == null || creature.Template == null) return occupiedNodes;

        GridNode centerNode = NodeFromWorldPoint(creature.GlobalPosition);
        occupiedNodes.Add(centerNode);

        // Pathfinder space is based on a side length. 1 square = 5ft.
        // Medium/Small = 5ft (1 square), Large = 10ft (2x2), Huge = 15ft (3x3), etc.
        int squaresPerSide = 1;
        switch (creature.Template.Size)
        {
            case CreatureSize.Large:     squaresPerSide = 2; break;
            case CreatureSize.Huge:      squaresPerSide = 3; break;
            case CreatureSize.Gargantuan: squaresPerSide = 4; break;
            case CreatureSize.Colossal:  squaresPerSide = 6; break;
        }

        if (squaresPerSide > 1)
        {
            // The starting corner of the occupied space. This assumes creature positions are grid-aligned.
            int startX = centerNode.gridX - (squaresPerSide -1) / 2;
            int startZ = centerNode.gridZ - (squaresPerSide - 1) / 2;

            for (int x = 0; x < squaresPerSide; x++)
            {
                for (int z = 0; z < squaresPerSide; z++)
                {
                    int checkX = startX + x;
                    int checkZ = startZ + z;
                    
                    if (checkX >= 0 && checkX < gridSizeX && checkZ >= 0 && checkZ < gridSizeZ)
                    {
                        GridNode occupiedNode = grid[checkX, centerNode.gridY, checkZ];
                        if (!occupiedNodes.Contains(occupiedNode))
                        {
                            occupiedNodes.Add(occupiedNode);
                        }
                    }
                }
            }
        }
        
        return occupiedNodes;
    }
    
    /// <summary>
    /// Changes all Water nodes within a radius to Ice nodes.
    /// </summary>
    public void FreezeWaterInArea(Vector3 center, float radius)
    {
        if (grid == null) return;
        GridNode centerNode = NodeFromWorldPoint(center);
        int radiusInNodes = Mathf.CeilToInt(radius / nodeDiameter);

        for (int x = -radiusInNodes; x <= radiusInNodes; x++)
        for (int y = -radiusInNodes; y <= radiusInNodes; y++)
        for (int z = -radiusInNodes; z <= radiusInNodes; z++)
        {
            int checkX = centerNode.gridX + x;
            int checkY = centerNode.gridY + y;
            int checkZ = centerNode.gridZ + z;

            if (checkX >= 0 && checkX < gridSizeX && checkY >= 0 && checkY < gridSizeY && checkZ >= 0 && checkZ < gridSizeZ)
            {
                GridNode targetNode = grid[checkX, checkY, checkZ];
                if (targetNode.terrainType == TerrainType.Water && center.DistanceTo(targetNode.worldPosition) <= radius)
                {
                    targetNode.terrainType = TerrainType.Ice;
                    targetNode.baseMovementCost = 2; // Spend 2 squares of movement
                    targetNode.movementCost = 2;
                    GD.Print($"Water at {targetNode.worldPosition} has been frozen into ice.");
                }
            }
        }
    }

    /// <summary>
    /// Helper for AI to determine if freezing is a useful tactic.
    /// </summary>
    public bool IsWaterBetween(Vector3 start, Vector3 end)
    {
        Vector3 direction = (end - start).Normalized();
        float distance = start.DistanceTo(end);
        int steps = Mathf.FloorToInt(distance / nodeDiameter);

        for (int i = 0; i < steps; i++)
        {
            GridNode nodeOnLine = NodeFromWorldPoint(start + direction * i * nodeDiameter);
            if (nodeOnLine.terrainType == TerrainType.Water) return true;
        }
        return false;
    }

    /// <summary>
    /// Updates environmental tags in a radius. Helper for effects like "Fog Cloud" or "Slippery".
    /// </summary>
    public void UpdateEnvironmentalTagsInRadius(Vector3 center, float radius, Godot.Collections.Array<string> tags, bool add)
    {
        if (grid == null) return;
        GridNode centerNode = NodeFromWorldPoint(center);
        int radiusInNodes = Mathf.CeilToInt(radius / nodeDiameter);

        for (int x = -radiusInNodes; x <= radiusInNodes; x++)
        for (int y = -radiusInNodes; y <= radiusInNodes; y++)
        for (int z = -radiusInNodes; z <= radiusInNodes; z++)
        {
            int checkX = centerNode.gridX + x;
            int checkY = centerNode.gridY + y;
            int checkZ = centerNode.gridZ + z;

            if (checkX >= 0 && checkX < gridSizeX && checkY >= 0 && checkY < gridSizeY && checkZ >= 0 && checkZ < gridSizeZ)
            {
                GridNode targetNode = grid[checkX, checkY, checkZ];
                if (center.DistanceTo(targetNode.worldPosition) <= radius)
                {
                    foreach(string tag in tags)
                    {
                        if (add)
                        {
                            if(!targetNode.environmentalTags.Contains(tag)) targetNode.environmentalTags.Add(tag);
                        }
                        else
                        {
                            targetNode.environmentalTags.Remove(tag);
                        }
                    }
                }
            }
        }
    }

    #region Physics Helpers (Unity -> Godot mapping)
    // Replicates Physics.CheckSphere
    private bool CheckSphere(Vector3 pos, float radius, uint mask)
    {
        var spaceState = GetWorld3D().DirectSpaceState;
        var shape = new SphereShape3D();
        shape.Radius = radius;
        var query = new PhysicsShapeQueryParameters3D();
        query.Shape = shape;
        query.Transform = new Transform3D(Basis.Identity, pos);
        query.CollisionMask = mask;
        var result = spaceState.IntersectShape(query, 1);
        return result.Count > 0;
    }

    // Replicates Physics.Raycast / Linecast logic for simple blocking check
    private bool CheckRaycast(Vector3 origin, Vector3 dir, float length, uint mask)
    {
        var spaceState = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + dir * length, mask);
        var result = spaceState.IntersectRay(query);
        return result.Count > 0;
    }

    private bool CheckLinecast(Vector3 start, Vector3 end, uint mask)
    {
        var spaceState = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(start, end, mask);
        var result = spaceState.IntersectRay(query);
        return result.Count > 0;
    }

    // Replicates Physics.OverlapSphere
    private List<Node3D> OverlapSphere(Vector3 pos, float radius, uint mask)
    {
        List<Node3D> hits = new List<Node3D>();
        var spaceState = GetWorld3D().DirectSpaceState;
        var shape = new SphereShape3D();
        shape.Radius = radius;
        var query = new PhysicsShapeQueryParameters3D();
        query.Shape = shape;
        query.Transform = new Transform3D(Basis.Identity, pos);
        query.CollisionMask = mask;
        var result = spaceState.IntersectShape(query); // No max limit implies default max or high number

        foreach (var dict in result)
        {
            if (dict.TryGetValue("collider", out var colVariant))
            {
                var collider = (Node3D)colVariant.Obj;
                hits.Add(collider);
            }
        }
        return hits;
    }
    #endregion

    /// <summary>
    /// Visualization logic converted from Unity OnDrawGizmos.
    /// Godot does not draw Gizmos from scripts the same way.
    /// Call this method from a _Process loop in a separate Debug node if visualization is required.
    /// </summary>
    public void VisualizeGrid(DebugDraw3D debugDraw)
    {
        if (grid != null)
        {
            // debugDraw.DrawWireCube(GlobalPosition, gridWorldSize, Colors.White); // Conceptual

            foreach (GridNode n in grid)
            {
                if(n == null) continue;
                Color col = Colors.White;

                if (n.terrainType == TerrainType.Ice) col = Colors.Cyan;
                else if (n.isSlope) col = Colors.Green;
                else if (n.heightOfDropBelow > 0) col = new Color(1, 0.5f, 0); // Orange
                else if (n.terrainType == TerrainType.Water) col = new Color(0, 0.5f, 1, 0.1f + (n.waterDepth * 0.1f));
                else if (n.providesCover) col = Colors.Magenta;
                else if (n.movementCost > n.baseMovementCost) col = Colors.Cyan;
                else if (n.movementCost > 1) col = Colors.Yellow;
                else
                {
                    switch (n.terrainType)
                    {
                        case TerrainType.Ground: col = Colors.White; break;
                        case TerrainType.Canopy: col = new Color(0, 1, 0, 0.4f); break;
                        case TerrainType.Air:    col = new Color(0, 0, 1, 0.05f); break;
                        case TerrainType.Solid:  col = new Color(1, 0, 0, 0.2f); break;
                    }
                }
                
                if(n.terrainType != TerrainType.Air)
                {
                    // debugDraw.DrawBox(n.worldPosition, Vector3.One * (nodeDiameter - 0.1f), col);
                }
            }
        }
    }
}
