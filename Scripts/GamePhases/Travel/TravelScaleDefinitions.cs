using Godot;
using System.Collections.Generic;

/// <summary>
/// Travel-only time resolution states.
///
/// Expected output:
/// - StrategicHour always means 3600 seconds per turn.
/// - TacticalMinute always means 60 seconds per turn.
/// - CombatSixSeconds always means 6 seconds per turn.
/// - Values are intentionally locked to preserve deterministic spell timing.
/// </summary>
public enum TravelResolutionState
{
    StrategicHour,
    TacticalMinute,
    CombatSixSeconds
}

/// <summary>
/// Fixed spatial/time conversion values for Travel mode hierarchy.
///
/// Expected output:
/// - Tactical squares always cleanly subdivide into 10x10 combat squares.
/// - Strategic tile span approximates one hour of movement at baseline speed.
/// - Consumers can use one source of truth without sprinkling hardcoded constants.
/// </summary>
public static class TravelScaleDefinitions
{
    public const int StrategicMapTilesPerSide = 24;
    public const int StrategicMapTotalTiles = StrategicMapTilesPerSide * StrategicMapTilesPerSide;

    public const int StrategicTurnSeconds = 3600;
    public const int TacticalTurnSeconds = 60;
    public const int CombatTurnSeconds = 6;

    public const float CombatSquareFeet = 5f;
    public const float TacticalSquareFeet = 50f;
    public const float StrategicTileFeet = 12000f;

    // Tactical projection is intentionally fixed so travel-to-combat transitions stay deterministic.
    public const int TacticalWindowSquaresPerSide = 20;
    public const float TacticalWindowFeetPerSide = TacticalWindowSquaresPerSide * TacticalSquareFeet;

    // Unified combat map limits. Travel clamps to tactical bounds; Arena uses a configurable upper bound.
    public const float MinimumCombatMapSizeFeet = 120f;
    public const float MaximumTravelCombatMapSizeFeet = TacticalWindowFeetPerSide;

    public const int TacticalToCombatSubdivision = 10;

    public static int SecondsFor(TravelResolutionState state)
    {
        return state switch
        {
            TravelResolutionState.StrategicHour => StrategicTurnSeconds,
            TravelResolutionState.TacticalMinute => TacticalTurnSeconds,
            _ => CombatTurnSeconds
        };
    }
}

/// <summary>
/// Social tone used by strategic interactions.
/// </summary>
public enum StrategicDisposition
{
    Friendly,
    Suspicious,
    Hostile,
    Animal
}

/// <summary>
/// Lightweight strategic entity that points to full creature data.
/// </summary>
public sealed class StrategicEntity
{
    public string EntityId;
    public CreatureTemplate_SO CreatureDefinition;
    public CreatureStats CreatureRuntimeReference;
    public Vector2I TileCoord;
    public float Hunger;
    public float InjuryState;
    public bool HasHomeTile;
    public Vector2I HomeTile;
    public bool IsPlayerParty;
	public int GroupSize; // Tracks the exact number of creatures in this roaming pack
}

/// <summary>
/// One tile in the persistent strategic travel map.
/// </summary>
public sealed class StrategicMapTileData
{
    public Vector2I Coordinate;
    public BiomeTravelDefinition BiomeDefinition;
    public List<StrategicEntity> StrategicEntities = new List<StrategicEntity>();
    public float Disturbance;
    public bool IsExitTile;
}

/// <summary>
/// Complete strategic map runtime used while travel is active.
/// </summary>
public sealed class StrategicMapData
{
    public int Width = TravelScaleDefinitions.StrategicMapTilesPerSide;
    public int Height = TravelScaleDefinitions.StrategicMapTilesPerSide;
    public StrategicMapTileData[,] Tiles;
    public Vector2I PlayerTileCoord;
    public Vector2I ExitTileCoord;
    public List<StrategicEntity> AllEntities = new List<StrategicEntity>();

    public bool TryGetTile(Vector2I coord, out StrategicMapTileData tile)
    {
        tile = null;
        if (coord.X < 0 || coord.Y < 0 || coord.X >= Width || coord.Y >= Height || Tiles == null)
        {
            return false;
        }

        tile = Tiles[coord.X, coord.Y];
        return tile != null;
    }
}

/// <summary>
/// Deterministic strategic tile snapshot used by Travel map hierarchy.
/// </summary>
public sealed class StrategicTileData
{
    public Vector2I StrategicCoordinate;
    public string BiomeType;
    public string ElevationBand;
    public float MoistureLevel;
    public uint FeatureFlags;
    public List<StrategicEntity> StrategicEntities = new List<StrategicEntity>();
    public float Disturbance;
}

/// <summary>
/// Tactical map payload generated from one strategic tile.
/// </summary>
public sealed class TacticalGridData
{
    public int Width;
    public int Height;
    public float SquareFeet = TravelScaleDefinitions.TacticalSquareFeet;
    public float StrategicOriginFeetX;
    public float StrategicOriginFeetZ;
    public float[,] ElevationMap;
    public bool[,] ObstacleMap;
    public float[,] CoverMap;
    public float[,] VisibilityMap;
}
