using Godot;
// =================================================================================================
// FILE: MirrorImageController.cs (GODOT VERSION)
// PURPOSE: Manages the state of an active Mirror Image spell on a creature.
// ATTACH TO: A creature at runtime when they cast Mirror Image (Child Node).
// =================================================================================================
public partial class MirrorImageController : Node
{
public int ImageCount { get; private set; }
private float duration;

/// <summary>
/// Initializes the controller with the number of images and duration.
/// </summary>
public void Initialize(int numberOfImages, float durationInSeconds)
{
    this.ImageCount = numberOfImages;
    this.duration = durationInSeconds;
    GD.Print($"{GetParent().Name} creates {ImageCount} mirror images.");
    // TODO: Hook into a visual effect controller to make the character shimmer.
}

public override void _Process(double delta)
{
    duration -= (float)delta;
    if (duration <= 0)
    {
        GD.Print($"Mirror Image on {GetParent().Name} has expired.");
        QueueFree();
    }
}

/// <summary>
/// Destroys one image and checks if the spell should end.
/// </summary>
public void DestroyImage(string reason)
{
    if (ImageCount > 0)
    {
        ImageCount--;
        GD.Print($"An image of {GetParent().Name} was destroyed by {reason}. {ImageCount} images remain.");
        if (ImageCount <= 0)
        {
            GD.Print($"All mirror images of {GetParent().Name} have been destroyed.");
            QueueFree();
        }
    }
}
}