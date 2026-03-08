using Godot;
using System.Threading.Tasks;
// =================================================================================================
// FILE: ProjectedImageController.cs (GODOT VERSION)
// PURPOSE: Manages a Projected Image spell entity.
// ATTACH TO: Projected Image prefab (Root Node).
// =================================================================================================
public partial class ProjectedImageController : GridNode
{
public CreatureStats Caster { get; private set; }
private CreatureStats myStats; // The stats of this image.
private CreatureMover myMover;

public override void _Ready()
{
    myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
    myMover = GetParent().GetNodeOrNull<CreatureMover>("CreatureMover");
}

public void Initialize(CreatureStats caster)
{
    this.Caster = caster;
    
    // Ensure stats and mover are cached if Initialize is called immediately after instantiation
    if (myStats == null) myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
    if (myMover == null) myMover = GetParent().GetNodeOrNull<CreatureMover>("CreatureMover");

    // Set the image's stats to be a proxy.
    myStats.IsProjectedImage = true;
    myStats.Caster = caster;

    // An image is intangible and immune to most things.
    // In a full implementation, you'd add traits for this.
    // For now, we'll rely on ApplyTemplate.
    myStats.ApplyTemplate(); // Apply the template to get the look
}

public override void _Process(double delta)
{
    // Rule: You must maintain line of effect to the projected image at all times.
    if (!GodotObject.IsInstanceValid(Caster))
    {
        // Caster died or vanished
        GetParent().QueueFree();
        return;
    }

    // LoS Manager expects Node3D context for World3D access. Caster is a CharacterBody3D (Node3D).
    // The image itself is also a Node3D parent.
    var myBody = GetParent<Node3D>();
    
    if (!LineOfSightManager.HasLineOfEffect(Caster, Caster.GlobalPosition, myBody.GlobalPosition))
    {
        GD.PrintRich($"[color=orange]Line of Effect to {GetParent().Name} was broken. The spell ends.[/color]");
        GetParent().QueueFree();
    }
}

/// <summary>
/// Called by the AI or Player to command the image to move.
/// </summary>
public async Task DirectMovement(Vector3 destination)
{
    if (myMover != null)
    {
        GD.Print($"{Caster.Name} directs their Projected Image to move.");
        await myMover.MoveToAsync(destination, true); // Assuming fly = true for image usually? Or walk.
    }
}
}