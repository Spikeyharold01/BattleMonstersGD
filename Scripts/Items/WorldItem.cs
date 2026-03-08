using Godot;

// =================================================================================================
// FILE: WorldItem.cs
// PURPOSE: A simple component to attach to an item's world prefab, allowing it to hold its data.
// ATTACH TO: Prefabs representing items on the ground (Node3D).
// =================================================================================================

/// <summary>
/// This component is placed on the Node prefab that represents a dropped item.
/// Its sole purpose is to hold a reference to the item's Resource data.
/// </summary>
public partial class WorldItem : Node3D
{
    // A public reference to the Resource that defines this item.
    // This is set by the ItemManager when the item is dropped.
    [Export] public Item_SO ItemData;
}