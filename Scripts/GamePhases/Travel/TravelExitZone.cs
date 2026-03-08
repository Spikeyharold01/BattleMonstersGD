using Godot;

/// <summary>
/// Exit trigger for Travel mode.
///
/// When the player enters this area, TravelPhase can finish.
/// </summary>
public partial class TravelExitZone : Area3D
{
    /// <summary>
    /// Sent when player enters the zone.
    /// </summary>
    [Signal] public delegate void PlayerReachedExitEventHandler();

    /// <summary>
    /// Connect local body-enter event.
    /// </summary>
    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
    }

    /// <summary>
    /// If entering body is in Player group, emit completion signal.
    /// </summary>
    private void OnBodyEntered(Node3D body)
    {
        if (body != null && body.IsInGroup("Player"))
        {
            EmitSignal(SignalName.PlayerReachedExit);
        }
    }
}
