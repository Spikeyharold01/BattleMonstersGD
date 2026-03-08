using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: ScryingSensorRegistry.cs
// PURPOSE: Stores temporary clairvoyance sensor anchors that can extend an observer's vision.
// =================================================================================================
public static class ScryingSensorRegistry
{
    private static readonly Dictionary<CreatureStats, List<ClairvoyanceSensorController>> sensorsByCaster = new();

    public readonly struct ScryingSensorSnapshot
    {
        public CreatureStats Owner { get; }
        public Vector3 Position { get; }
        public bool IsMythic { get; }

        public ScryingSensorSnapshot(CreatureStats owner, Vector3 position, bool isMythic)
        {
            Owner = owner;
            Position = position;
            IsMythic = isMythic;
        }
    }

	public static void Register(CreatureStats caster, ClairvoyanceSensorController sensor)
    {
        if (caster == null || sensor == null) return;

        if (!sensorsByCaster.TryGetValue(caster, out var sensors))
        {
            sensors = new List<ClairvoyanceSensorController>();
            sensorsByCaster[caster] = sensors;
        }

        sensors.RemoveAll(s => !GodotObject.IsInstanceValid(s));
        sensors.Add(sensor);
    }

    public static void Unregister(CreatureStats caster, ClairvoyanceSensorController sensor)
    {
        if (caster == null || sensor == null) return;
        if (!sensorsByCaster.TryGetValue(caster, out var sensors)) return;

        sensors.RemoveAll(s => !GodotObject.IsInstanceValid(s) || s == sensor);
        if (sensors.Count == 0) sensorsByCaster.Remove(caster);
    }

    public static List<Vector3> GetActiveVisionOrigins(CreatureStats caster)
    {
        var origins = new List<Vector3>();
        if (caster == null) return origins;

        origins.Add(caster.GlobalPosition);

        if (!sensorsByCaster.TryGetValue(caster, out var sensors)) return origins;

        sensors.RemoveAll(s => !GodotObject.IsInstanceValid(s) || !s.IsActiveVisionAnchor());
        origins.AddRange(sensors.Select(s => s.GlobalPosition));

        if (sensors.Count == 0) sensorsByCaster.Remove(caster);
        return origins;
    }

    public static List<ScryingSensorSnapshot> GetActiveSensorsInRadius(Vector3 center, float radiusFeet)
    {
        float radiusSquared = radiusFeet * radiusFeet;
        var results = new List<ScryingSensorSnapshot>();
        var castersToRemove = new List<CreatureStats>();

        foreach (var kvp in sensorsByCaster)
        {
            var owner = kvp.Key;
            var sensors = kvp.Value;

            sensors.RemoveAll(s => !GodotObject.IsInstanceValid(s) || !s.IsActiveVisionAnchor());
            if (sensors.Count == 0)
            {
                castersToRemove.Add(owner);
                continue;
            }

            foreach (var sensor in sensors)
            {
                if (center.DistanceSquaredTo(sensor.GlobalPosition) > radiusSquared) continue;
                results.Add(new ScryingSensorSnapshot(owner, sensor.GlobalPosition, sensor.IsMythicSensor));
            }
        }

        foreach (var owner in castersToRemove) sensorsByCaster.Remove(owner);
        return results;
    }
}