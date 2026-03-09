using Godot;
// =================================================================================================
// FILE: VeilController.cs (GODOT VERSION - UPDATED)
// PURPOSE: Manages the visual state of a creature under a Veil illusion.
// ATTACH TO: Creature Scene (Child Node).
// =================================================================================================
public partial class VeilController : Node
{
public CreatureTemplate_SO ApparentTemplate { get; private set; }
private Node3D currentAppearance;
private Node3D originalAppearance;

public CreatureStats Caster { get; set; }
public Ability_SO SourceAbility { get; set; }

public void ApplyVeil(CreatureTemplate_SO newAppearanceTemplate, Node3D originalVisuals, CreatureStats caster, Ability_SO sourceAbility)
{
    this.ApparentTemplate = newAppearanceTemplate;
    this.originalAppearance = originalVisuals;
    this.Caster = caster;
    this.SourceAbility = sourceAbility;

    // Deactivate the original visuals
    originalAppearance.Visible = false;

    // Instantiate the new appearance and parent it to this creature
    if (newAppearanceTemplate.CharacterPrefab != null)
    {
        currentAppearance = newAppearanceTemplate.CharacterPrefab.Instantiate<Node3D>();
        GetParent().AddChild(currentAppearance);
        currentAppearance.Position = Vector3.Zero;
        currentAppearance.Rotation = Vector3.Zero;
        
        // The new appearance shouldn't have its own stats or controllers.
        // A robust system would strip these components.
        var stats = currentAppearance.GetNodeOrNull<CreatureStats>("CreatureStats");
        if (stats != null) stats.QueueFree();
        
        var ai = currentAppearance.GetNodeOrNull<AIController>("AIController");
        if (ai != null) ai.QueueFree();
        
        var player = currentAppearance.GetNodeOrNull<PlayerActionController>("PlayerActionController");
        if (player != null) player.QueueFree();
    }
    else
    {
        GD.PrintErr($"CreatureTemplate {newAppearanceTemplate.CreatureName} missing CharacterPrefab for Veil.");
    }
}

public void RemoveVeil()
{
    if (originalAppearance != null)
    {
        originalAppearance.Visible = true;
    }
    if (currentAppearance != null)
    {
        currentAppearance.QueueFree();
    }
    QueueFree(); // The controller is no longer needed.
}
}