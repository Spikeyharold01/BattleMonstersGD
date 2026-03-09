using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: ItemManager.cs
// PURPOSE: A global manager for handling items that exist in the game world.
// ATTACH TO: An Autoload Singleton named "ItemManager" in Project Settings.
// =================================================================================================

/// <summary>
/// This singleton class is responsible for spawning, tracking, and removing items
/// that are physically present in the game world (i.e., not in an inventory).
/// </summary>
public partial class ItemManager : Node
{
    public static ItemManager Instance { get; private set; }

    [ExportGroup("Prefabs")]
    [Export]
    [Tooltip("The generic prefab to use for any dropped item. It should have a WorldItem component.")]
    public PackedScene GenericWorldItemPrefab;

    // A list of all active item Nodes in the scene.
    private List<WorldItem> worldItems = new List<WorldItem>();

    public override void _Ready()
    {
        if (Instance != null && Instance != this) 
        {
            QueueFree();
        }
        else 
        {
            Instance = this;
        }
    }

    /// <summary>
    /// Spawns an item into the world at a specific position.
    /// </summary>
    /// <param name="itemData">The ScriptableObject data of the item to drop.</param>
    /// <param name="position">The world-space position to spawn the item.</param>
    public void DropItem(Item_SO itemData, Vector3 position)
    {
        if (itemData == null || GenericWorldItemPrefab == null) return;

        // Use the item's specific prefab if it has one, otherwise use the generic default.
        PackedScene prefabToSpawn = itemData.WorldPrefab != null ? itemData.WorldPrefab : GenericWorldItemPrefab;
        
        Node3D itemGO = prefabToSpawn.Instantiate<Node3D>();
        
        // In Godot, we must add the child to the scene tree. 
        // Adding to the current scene root is the closest equivalent to Unity's root instantiation.
        GetTree().CurrentScene.AddChild(itemGO);
        itemGO.GlobalPosition = position;

        // We check for the WorldItem component (Godot Script/Node)
        // Assuming WorldItem is attached to the root of the prefab, or we get it from children.
        WorldItem worldItem = itemGO as WorldItem;
        if (worldItem == null)
        {
            worldItem = itemGO.GetNodeOrNull<WorldItem>("WorldItem");
        }

        if (worldItem != null)
        {
            worldItem.ItemData = itemData; // Assuming WorldItem has 'ItemData' property matching Unity's 'itemData'
            worldItems.Add(worldItem);
            GD.Print($"Dropped {itemData.ItemName} at {position}.");
        }
        else
        {
            GD.PrintErr($"Item prefab '{prefabToSpawn.ResourcePath}' is missing the WorldItem component!");
            itemGO.QueueFree();
        }
    }

    /// <summary>
    /// A creature picks up an item from the world.
    /// </summary>
    /// <param name="pickerUpper">The creature picking up the item.</param>
    /// <param name="itemToPickup">The WorldItem component of the item being picked up.</param>
    public void PickupItem(CreatureStats pickerUpper, WorldItem itemToPickup)
    {
        if (pickerUpper == null || itemToPickup == null) return;

        var inventory = pickerUpper.GetNodeOrNull<InventoryController>("InventoryController");
        if (inventory != null)
        {
            // Create a new ItemInstance logic is handled inside InventoryController.AddItem usually,
            // or we assume InventoryController handles the data conversion.
            // Based on Unity script: inventory.AddItem(itemToPickup.itemData);
            // In Godot InventoryController (previous step), AddItem takes an ItemInstance.
            
            inventory.AddItem(new ItemInstance(itemToPickup.ItemData));
            
            worldItems.Remove(itemToPickup);
            itemToPickup.QueueFree(); // Destroy the object
        }
    }

    /// <summary>
    /// Finds all dropped items within a certain radius of a point.
    /// </summary>
    /// <param name="center">The center of the search area.</param>
    /// <param name="radius">The radius to search within.</param>
    /// <returns>A list of WorldItem components for all items found.</returns>
    public List<WorldItem> GetItemsInRadius(Vector3 center, float radius)
    {
        // Filter out any items that might have been destroyed but not removed (safety check)
        worldItems.RemoveAll(item => !GodotObject.IsInstanceValid(item));

        return worldItems.Where(item => center.DistanceTo(item.GlobalPosition) <= radius).ToList();
    }
}