using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: ScryingAwarenessController.cs
// PURPOSE: Generic long-duration controller that monitors nearby scrying sensors and reports them.
// =================================================================================================
public partial class ScryingAwarenessController : Godot.Node
{
    private CreatureStats owner;
    private float remainingDurationSeconds;
    private float detectionRadiusFeet;
    private bool revealScrierIdentity;
    private bool alwaysRevealDirectionDistance;

    private readonly Dictionary<CreatureStats, Vector3> knownSensors = new();

    public void Initialize(
        CreatureStats source,
        float durationSeconds,
        float radiusFeet,
        bool revealIdentity,
        bool revealDirectionAndDistance)
    {
        owner = source;
        remainingDurationSeconds = durationSeconds;
        detectionRadiusFeet = radiusFeet;
        revealScrierIdentity = revealIdentity;
        alwaysRevealDirectionDistance = revealDirectionAndDistance;

        PerformScan();
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(owner))
        {
            QueueFree();
            return;
        }

        remainingDurationSeconds -= (float)delta;
        if (remainingDurationSeconds <= 0f)
        {
            GD.Print($"{owner.Name}'s anti-scrying ward expires.");
            QueueFree();
            return;
        }

        PerformScan();
    }

    private void PerformScan()
    {
        if (owner == null) return;

        var activeSensors = ScryingSensorRegistry.GetActiveSensorsInRadius(owner.GlobalPosition, detectionRadiusFeet);

        foreach (var sensor in activeSensors)
        {
            if (!GodotObject.IsInstanceValid(sensor.Owner) || sensor.Owner == owner) continue;

            bool isNewOrMoved = !knownSensors.TryGetValue(sensor.Owner, out var lastPos) || lastPos.DistanceSquaredTo(sensor.Position) > 0.25f;
            if (!isNewOrMoved) continue;

            knownSensors[sensor.Owner] = sensor.Position;
            AnnounceSensor(sensor);
        }

        var staleOwners = new List<CreatureStats>();
        foreach (var entry in knownSensors)
        {
            bool stillDetected = activeSensors.Exists(s => s.Owner == entry.Key);
            if (!stillDetected) staleOwners.Add(entry.Key);
        }

        foreach (var stale in staleOwners) knownSensors.Remove(stale);
    }

    private void AnnounceSensor(ScryingSensorRegistry.ScryingSensorSnapshot sensor)
    {
        Vector3 relative = sensor.Position - owner.GlobalPosition;
        float distance = relative.Length();

        GD.PrintRich($"[color=cyan]{owner.Name} senses a magical sensor at {distance:0.#} ft.[/color]");

        bool revealedDirectionAndDistance = alwaysRevealDirectionDistance;

        if (revealScrierIdentity)
        {
            revealedDirectionAndDistance = true;
            GD.PrintRich($"[color=purple]{owner.Name} receives a clear image of {sensor.Owner.Name}, the scrier.[/color]");
        }
        else
        {
            int ownerCheck = Dice.Roll(1, 20) + owner.Template.CasterLevel;
            int scrierCheck = Dice.Roll(1, 20) + sensor.Owner.Template.CasterLevel;
            if (ownerCheck >= scrierCheck)
            {
                revealedDirectionAndDistance = true;
                GD.PrintRich($"[color=cyan]{owner.Name} pierces the sensor and glimpses {sensor.Owner.Name}. ({ownerCheck} vs {scrierCheck})[/color]");
            }
        }

        if (revealedDirectionAndDistance)
        {
            string direction = GetDirectionLabel(relative);
            GD.Print($"Direction: {direction}, distance: {distance:0.#} ft.");
        }
    }

    private static string GetDirectionLabel(Vector3 offset)
    {
        if (offset.LengthSquared() < 0.01f) return "here";

        if (Mathf.Abs(offset.X) > Mathf.Abs(offset.Z))
            return offset.X >= 0 ? "east" : "west";

        return offset.Z >= 0 ? "south" : "north";
    }
}