using Godot;

// =================================================================================================
// FILE: ClairvoyanceSensorController.cs
// PURPOSE: Tracks lifetime/range constraints for Clairaudience/Clairvoyance remote sensor.
// =================================================================================================
public partial class ClairvoyanceSensorController : Node3D
{
    public CreatureStats Caster { get; private set; }
	public bool IsMythicSensor { get; private set; }

    private float remainingDurationSeconds;
    private float maxDistanceFromCaster;

    public void Initialize(CreatureStats caster, float durationSeconds, float maxDistanceFeet, bool isMythicSensor = false)
    {
        Caster = caster;
        remainingDurationSeconds = durationSeconds;
        maxDistanceFromCaster = maxDistanceFeet;
		IsMythicSensor = isMythicSensor;

        ScryingSensorRegistry.Register(caster, this);
    }

    public bool IsActiveVisionAnchor()
    {
        if (!GodotObject.IsInstanceValid(Caster)) return false;

        float distanceFromCaster = Caster.GlobalPosition.DistanceTo(GlobalPosition);
        return distanceFromCaster <= maxDistanceFromCaster;
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(Caster))
        {
            QueueFree();
            return;
        }

        remainingDurationSeconds -= (float)delta;
        if (remainingDurationSeconds <= 0f)
        {
            QueueFree();
            return;
        }

        float distanceFromCaster = Caster.GlobalPosition.DistanceTo(GlobalPosition);
        if (distanceFromCaster > maxDistanceFromCaster)
        {
            GD.PrintRich($"[color=orange]{Caster.Name} moves too far from their clairvoyance sensor, and it fades.[/color]");
            QueueFree();
        }
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Caster))
        {
            ScryingSensorRegistry.Unregister(Caster, this);
        }
    }
}