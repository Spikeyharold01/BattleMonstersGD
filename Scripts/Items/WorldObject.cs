using Godot;

// =================================================================================================
// FILE: WorldObject.cs (NEW FILE)
// PURPOSE: A component for interactable world objects that can be picked up as improvised weapons.
// ATTACH TO: Prefabs for rocks, logs, bricks, etc. (Node3D).
// =================================================================================================

public partial class WorldObject : Node3D
{
    [Export]
    [Tooltip("The Item_SO asset this object becomes when it is picked up.")]
    public Item_SO BecomesItemOnPickup;
}