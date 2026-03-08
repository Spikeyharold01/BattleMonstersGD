using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Runtime data returned after building a travel map.
/// </summary>
public sealed class TravelBiomeMapRuntime
{
    /// <summary>
    /// Root node for all generated map objects.
    /// </summary>
    public Node3D MapRoot;

    /// <summary>
    /// Ally spawn locations.
    /// </summary>
    public List<Vector3> AllySpawnPoints = new List<Vector3>();

    /// <summary>
    /// Player spawn location.
    /// </summary>
    public Vector3 PlayerSpawnPoint;

    /// <summary>
    /// Exit trigger area.
    /// </summary>
    public TravelExitZone ExitZone;

    /// <summary>
    /// Encounter-capable travel zones generated from biome procedural settings.
    /// </summary>
    public List<Aabb> EncounterSpawnZones = new List<Aabb>();

    /// <summary>
    /// Environmental values applied while this runtime is active.
    /// </summary>
    public TravelEnvironmentalModifiers EnvironmentalModifiers;

    /// <summary>
    /// Strategic metadata for the currently loaded travel area.
    /// </summary>
    public StrategicTileData StrategicTile;

    /// <summary>
    /// Full persistent strategic map used for the entire travel session.
    /// </summary>
    public StrategicMapData StrategicMap;

    /// <summary>
    /// Tactical projection generated deterministically from the active strategic tile.
    /// </summary>
    public TacticalGridData TacticalGrid;
}

/// <summary>
/// Builds a simple procedural biome map for Travel mode.
///
/// This is separate from arena map/spawn/win logic.
/// </summary>
public partial class TravelBiomeMapBuilder : Godot.Node
{
    /// <summary>
    /// Build map tiles, spawn points, exit zone, and travel-zone metadata from biome snapshot data.
    /// </summary>
    public TravelBiomeMapRuntime Build(TravelBiomeQuerySnapshot snapshot, Node3D parent)
    {
        if (snapshot == null || snapshot.Procedural == null)
        {
            GD.PrintErr("TravelBiomeMapBuilder.Build called with missing biome snapshot/procedural settings.");
            return null;
        }

        var runtime = new TravelBiomeMapRuntime();

        var mapRoot = new Node3D { Name = "TravelBiomeMap" };
        parent.AddChild(mapRoot);

        TravelProceduralGenerationSettings settings = snapshot.Procedural;

        var rng = new RandomNumberGenerator();
        if (settings.Seed == 0)
        {
            rng.Randomize();
        }
        else
        {
            rng.Seed = (ulong)settings.Seed;
        }

        // Create a tile grid with subtle vertical variation based on the biome settings.
        for (int x = 0; x < settings.Width; x++)
        {
            for (int z = 0; z < settings.Height; z++)
            {
                float yOffset = (rng.Randf() - 0.5f) * settings.HeightVariance;
                var tile = CreateTile(settings.TileSize, rng);
                tile.Position = new Vector3(x * settings.TileSize, yOffset, z * settings.TileSize);
                mapRoot.AddChild(tile);
            }
        }

        runtime.MapRoot = mapRoot;

        // Basic spawn layout.
        runtime.PlayerSpawnPoint = new Vector3(settings.TileSize, 0.5f, settings.TileSize);
        for (int i = 0; i < settings.AllySpawnCount; i++)
        {
            runtime.AllySpawnPoints.Add(new Vector3(settings.TileSize * (2 + i), 0.5f, settings.TileSize));
        }

        // Put exit at far corner.
        runtime.ExitZone = CreateExitZone(settings, mapRoot);

        // Build encounter-capable zones as travel metadata only.
        runtime.EncounterSpawnZones = CreateEncounterZones(settings, rng);

        // Pass through environmental values so travel systems can apply ambiance and visibility effects.
        runtime.EnvironmentalModifiers = snapshot.Environmental;

        // Build deterministic world hierarchy payloads.
        runtime.StrategicTile = BuildStrategicTile(snapshot, settings);
        runtime.StrategicMap = BuildStrategicMap(snapshot, rng);
        runtime.TacticalGrid = BuildDeterministicTacticalGrid(snapshot, runtime.StrategicTile, settings);

        return runtime;
    }

    /// <summary>
    /// Creates the full 24x24 strategic map at travel start.
    ///
    /// Expected output:
    /// - Every tile has biome reference, disturbance memory, and entity list.
    /// - Ecology-validated creatures are distributed across the entire map at generation time.
    /// - Entity count is capped for predictable performance and no runtime proximity spawning is required.
    /// </summary>
    private static StrategicMapData BuildStrategicMap(TravelBiomeQuerySnapshot snapshot, RandomNumberGenerator rng)
    {
        var map = new StrategicMapData
        {
            Tiles = new StrategicMapTileData[TravelScaleDefinitions.StrategicMapTilesPerSide, TravelScaleDefinitions.StrategicMapTilesPerSide],
            PlayerTileCoord = new Vector2I(0, 0),
            ExitTileCoord = new Vector2I(TravelScaleDefinitions.StrategicMapTilesPerSide - 1, TravelScaleDefinitions.StrategicMapTilesPerSide - 1)
        };

        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                map.Tiles[x, y] = new StrategicMapTileData
                {
                    Coordinate = new Vector2I(x, y),
                    BiomeDefinition = snapshot?.Definition,
                    Disturbance = Mathf.Clamp((snapshot?.EncounterDensity ?? 1f) * 0.1f, 0f, 0.6f),
                    IsExitTile = x == map.ExitTileCoord.X && y == map.ExitTileCoord.Y
                };
            }
        }

        Godot.Collections.Array<CreatureTemplate_SO> ecologyPool = snapshot?.EcologyValidatedEncounterPool;
        if (ecologyPool == null || ecologyPool.Count == 0)
        {
            return map;
        }

        int targetEntities = Mathf.Clamp(Mathf.RoundToInt(TravelScaleDefinitions.StrategicMapTotalTiles * Mathf.Clamp(snapshot.EncounterDensity, 0.4f, 1.5f) * 0.2f), 30, 180);
        for (int i = 0; i < targetEntities; i++)
        {
            CreatureTemplate_SO selectedTemplate = ecologyPool[rng.RandiRange(0, ecologyPool.Count - 1)];
            if (selectedTemplate == null)
            {
                continue;
            }

            Vector2I tile = new Vector2I(rng.RandiRange(0, map.Width - 1), rng.RandiRange(0, map.Height - 1));
            StrategicMapTileData tileData = map.Tiles[tile.X, tile.Y];

            var entity = new StrategicEntity
            {
                EntityId = $"strategic_{i}_{selectedTemplate.ResourcePath.GetHashCode()}",
                CreatureDefinition = selectedTemplate,
                TileCoord = tile,
                Hunger = rng.RandfRange(0.1f, 0.55f),
                InjuryState = 0f,
                HasHomeTile = rng.Randf() < 0.35f,
                HomeTile = tile,
                IsPlayerParty = false
            };

            tileData.StrategicEntities.Add(entity);
            map.AllEntities.Add(entity);
        }

        return map;
    }

    private static StrategicTileData BuildStrategicTile(TravelBiomeQuerySnapshot snapshot, TravelProceduralGenerationSettings settings)
    {
        string biome = snapshot?.Definition?.BiomeName ?? "UnknownBiome";

        return new StrategicTileData
        {
            StrategicCoordinate = new Vector2I(settings.StrategicCoordinateX, settings.StrategicCoordinateY),
            BiomeType = biome,
            ElevationBand = ResolveElevationBand(snapshot?.Environmental),
            MoistureLevel = snapshot?.Environmental?.WaterPresence ?? 0f,
            FeatureFlags = ResolveFeatureFlags(snapshot?.Environmental)
        };
    }

    private static TacticalGridData BuildDeterministicTacticalGrid(TravelBiomeQuerySnapshot snapshot, StrategicTileData strategicTile, TravelProceduralGenerationSettings settings)
    {
        // Tactical scope is intentionally fixed. We always project a localized 20x20 window,
        // never the full strategic tile, so travel resolution stays stable and predictable.
        int tacticalWidth = TravelScaleDefinitions.TacticalWindowSquaresPerSide;
        int tacticalHeight = TravelScaleDefinitions.TacticalWindowSquaresPerSide;

        // The event anchor is expressed in strategic-tile feet. We clamp to keep the 1,200 ft
        // tactical window fully inside the 12,000 ft strategic tile.
        float tacticalHalfSpan = TravelScaleDefinitions.TacticalWindowFeetPerSide * 0.5f;
        float anchorFeetX = Mathf.Clamp(settings.TacticalEventFeetX, tacticalHalfSpan, settings.StrategicTileFeet - tacticalHalfSpan);
        float anchorFeetZ = Mathf.Clamp(settings.TacticalEventFeetZ, tacticalHalfSpan, settings.StrategicTileFeet - tacticalHalfSpan);

        var grid = new TacticalGridData
        {
            Width = tacticalWidth,
            Height = tacticalHeight,
            StrategicOriginFeetX = anchorFeetX - tacticalHalfSpan,
            StrategicOriginFeetZ = anchorFeetZ - tacticalHalfSpan,
            ElevationMap = new float[tacticalWidth, tacticalHeight],
            ObstacleMap = new bool[tacticalWidth, tacticalHeight],
            CoverMap = new float[tacticalWidth, tacticalHeight],
            VisibilityMap = new float[tacticalWidth, tacticalHeight]
        };

        // Seed uses world seed + strategic coordinate + biome/elevation identity.
        int stableSeed = settings.Seed;
        stableSeed = (stableSeed * 397) ^ strategicTile.StrategicCoordinate.X;
        stableSeed = (stableSeed * 397) ^ strategicTile.StrategicCoordinate.Y;
        stableSeed = (stableSeed * 397) ^ (strategicTile.BiomeType?.GetHashCode() ?? 0);
        stableSeed = (stableSeed * 397) ^ (strategicTile.ElevationBand?.GetHashCode() ?? 0);

        var rng = new RandomNumberGenerator { Seed = (ulong)Mathf.Abs(stableSeed == 0 ? 1 : stableSeed) };

        for (int x = 0; x < tacticalWidth; x++)
        {
            for (int z = 0; z < tacticalHeight; z++)
            {
                float elevation = (rng.Randf() - 0.5f) * Mathf.Max(0.05f, settings.HeightVariance);
                bool obstacle = rng.Randf() < Mathf.Clamp((snapshot?.Environmental?.WaterPresence ?? 0.2f) * 0.35f, 0.05f, 0.55f);
                float cover = obstacle ? rng.RandfRange(0.4f, 0.9f) : rng.RandfRange(0.0f, 0.3f);
                float visibility = Mathf.Clamp(1f - cover, 0.05f, 1f);

                grid.ElevationMap[x, z] = elevation;
                grid.ObstacleMap[x, z] = obstacle;
                grid.CoverMap[x, z] = cover;
                grid.VisibilityMap[x, z] = visibility;
            }
        }

        return grid;
    }

    private static string ResolveElevationBand(TravelEnvironmentalModifiers environmental)
    {
        float variance = environmental?.ElevationVariance ?? 0f;
        if (variance >= 0.65f) return "Highland";
        if (variance >= 0.35f) return "Rolling";
        return "Lowland";
    }

    private static uint ResolveFeatureFlags(TravelEnvironmentalModifiers environmental)
    {
        uint flags = 0u;
        if (environmental == null)
        {
            return flags;
        }

        if (environmental.WaterPresence > 0.35f) flags |= 1u;
        if (environmental.FogDensity > 0.35f) flags |= 1u << 1;
        if (environmental.VegetationDensity > 0.35f) flags |= 1u << 2;
        return flags;
    }

    /// <summary>
    /// Create one visible tile.
    /// </summary>
    private static Node3D CreateTile(float tileSize, RandomNumberGenerator rng)
    {
        var tile = new Node3D { Name = "BiomeTile" };
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(tileSize, 0.25f, tileSize) } };

        var color = new Color(
            0.25f + rng.Randf() * 0.35f,
            0.45f + rng.Randf() * 0.25f,
            0.2f + rng.Randf() * 0.2f);

        mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = color };
        tile.AddChild(mesh);
        return tile;
    }

    /// <summary>
    /// Create the travel exit trigger.
    /// </summary>
    private static TravelExitZone CreateExitZone(TravelProceduralGenerationSettings settings, Node3D mapRoot)
    {
        var exitZone = new TravelExitZone { Name = "TravelExitZone" };

        var collision = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(settings.TileSize, 2f, settings.TileSize) }
        };

        exitZone.Position = new Vector3((settings.Width - 1) * settings.TileSize, 1f, (settings.Height - 1) * settings.TileSize);
        exitZone.AddChild(collision);
        mapRoot.AddChild(exitZone);

        return exitZone;
    }

    /// <summary>
    /// Produces rectangular zone metadata describing where travel encounters are allowed to appear.
    ///
    /// Expected output:
    /// - A list of Aabb zones in world-space aligned to the travel map.
    /// - The zones are informational data for TravelPhase systems; no combat spawn is performed here.
    /// </summary>
    private static List<Aabb> CreateEncounterZones(TravelProceduralGenerationSettings settings, RandomNumberGenerator rng)
    {
        var zones = new List<Aabb>();
        int zoneCount = Math.Max(1, settings.EncounterZoneCount);

        float mapWidth = settings.Width * settings.TileSize;
        float mapHeight = settings.Height * settings.TileSize;
        float padding = Mathf.Max(0f, settings.EncounterZoneBorderPadding);

        float minX = padding;
        float minZ = padding;
        float maxX = Mathf.Max(minX + settings.TileSize, mapWidth - padding);
        float maxZ = Mathf.Max(minZ + settings.TileSize, mapHeight - padding);

        for (int i = 0; i < zoneCount; i++)
        {
            float centerX = rng.RandfRange(minX, maxX);
            float centerZ = rng.RandfRange(minZ, maxZ);

            float sizeX = rng.RandfRange(settings.TileSize, settings.TileSize * 3f);
            float sizeZ = rng.RandfRange(settings.TileSize, settings.TileSize * 3f);

            var position = new Vector3(centerX - (sizeX * 0.5f), 0f, centerZ - (sizeZ * 0.5f));
            var size = new Vector3(sizeX, 2f, sizeZ);
            zones.Add(new Aabb(position, size));
        }

        return zones;
    }
}
