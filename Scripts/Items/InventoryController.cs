using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: InventoryController.cs
// PURPOSE: Manages a creature's items, both carried and equipped.
// REVISED: Now uses an ItemInstance class to track live data like current HP for sundering.
// REVISED: Added EquipFirstWeaponFromBackpack for the Charge action.
// REVISED: Now correctly handles equipping rules for one-handed vs. two-handed weapons.
// ATTACH TO: All creature scenes (as a child node) that can use equipment.
// =================================================================================================

/// <summary>
/// A class representing a specific instance of an item. This allows tracking of
/// live data like current HP, charges, etc., separate from the base Item_SO template.
/// </summary>
public class ItemInstance
{
    public Item_SO ItemData { get; private set; }
    public CreatureSize DesignedForSize { get; set; } // The size of the creature this weapon was originally for.
    public int CurrentHP;
    
    // Property to check if broken
    public bool IsBroken => CurrentHP <= ItemData.MaxHP / 2;

    public ItemInstance(Item_SO itemData)
    {
        this.ItemData = itemData;
        this.CurrentHP = itemData.MaxHP;
    }

    public void TakeDamage(int damage)
    {
        int damageToTake = damage - ItemData.Hardness;
        if (damageToTake > 0)
        {
            CurrentHP -= damageToTake;
        }
    }
}

/// <summary>
/// This component is responsible for holding and managing a creature's inventory.
/// </summary>
public partial class InventoryController : Node
{
    [ExportGroup("Starting Gear")]
    [Export]
    [Tooltip("Items the creature will start with in its backpack.")]
    public Godot.Collections.Array<Item_SO> StartingItems = new();

    [Export]
    [Tooltip("Items the creature will start with already equipped.")]
    public Godot.Collections.Array<Item_SO> StartingEquipment = new();

    private List<ItemInstance> backpackItems = new List<ItemInstance>();
    private Dictionary<EquipmentSlot, ItemInstance> equippedItems = new Dictionary<EquipmentSlot, ItemInstance>();
    
    private CreatureStats myStats; // Cache stats for equipment checks

    // Godot Signal replacement for Action event
    [Signal] public delegate void EquipmentChangedEventHandler();

    public override void _Ready()
    {
        // Assuming CreatureStats is on the parent or same node. Adjust based on scene tree.
        myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
        
        // Initialize Dictionary
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            if (slot != EquipmentSlot.None)
            {
                equippedItems[slot] = null;
            }
        }

        // Initialize Starting Items (Backpack)
        foreach(var item in StartingItems)
        {
            AddItem(new ItemInstance(item));
        }

        // Equip items specified directly on the inspector component
        foreach(var item in StartingEquipment)
        {
            var instance = new ItemInstance(item);
            if (myStats != null) instance.DesignedForSize = myStats.Template.Size;
            EquipItem(instance);
        }

        // Equip default items parsed by the importer from the template
        if (myStats != null && myStats.Template != null && myStats.Template.StartingEquipment != null)
        {
            foreach(var item in myStats.Template.StartingEquipment)
            {
                // Only equip if the slot isn't already filled by a prefab-specific item
                if (GetEquippedItem(item.EquipSlot) == null)
                {
                    var instance = new ItemInstance(item);
                    instance.DesignedForSize = myStats.Template.Size;
                    EquipItem(instance);
                }
            }
        }
    }
    
    public void AddItem(ItemInstance itemInstance)
    {
        if (itemInstance == null) return;
        
        // Rule: Creatures that can't use equipment (like animals) can't pick up items.
        if (myStats != null && myStats.Template != null && !myStats.Template.CanUseEquipment)
        {
            GD.Print($"{GetParent().Name} cannot pick up {itemInstance.ItemData.ItemName} because it cannot use equipment.");
            return;
        }

        backpackItems.Add(itemInstance);
        GD.Print($"{GetParent().Name} picked up {itemInstance.ItemData.ItemName}.");
    }
    
    public void RemoveItem(ItemInstance itemInstance)
    {
        if (itemInstance == null) return;
        backpackItems.Remove(itemInstance);
    }

    public void UnequipItem(EquipmentSlot slot)
    {
        if (equippedItems.ContainsKey(slot) && equippedItems[slot] != null)
        {
            ItemInstance item = equippedItems[slot];
            equippedItems[slot] = null;
            backpackItems.Add(item);
            GD.Print($"{GetParent().Name} unequipped {item.ItemData.ItemName} from {slot} slot and moved it to backpack.");
            EmitSignal(SignalName.EquipmentChanged);
        }
    }

    public void EquipItem(ItemInstance itemToEquip)
    {
        if (itemToEquip == null || !itemToEquip.ItemData.IsEquippable)
        {
            return;
        }
        
        // Ensure the item is tracked in the backpack before equipping
        if (!backpackItems.Contains(itemToEquip))
        {
            backpackItems.Add(itemToEquip);
        }

        // --- NEW: Two-Weapon Fighting Equip Logic ---
        var mainHandItem = GetEquippedItem(EquipmentSlot.MainHand);

        // Case 1: Trying to equip a two-handed weapon.
        if (itemToEquip.ItemData.Handedness == WeaponHandedness.TwoHanded)
        {
            // Unequip anything in the off-hand or shield slot to make room.
            UnequipItem(EquipmentSlot.OffHand);
            UnequipItem(EquipmentSlot.Shield);
        }

        // Case 2: Trying to equip an item in the OffHand or Shield slot.
        if (itemToEquip.ItemData.EquipSlot == EquipmentSlot.OffHand || itemToEquip.ItemData.EquipSlot == EquipmentSlot.Shield)
        {
            // Fail if a two-handed weapon is already in the main hand.
            if (mainHandItem != null && mainHandItem.Handedness == WeaponHandedness.TwoHanded)
            {
                GD.Print($"Cannot equip {itemToEquip.ItemData.ItemName}; {GetParent().Name} is using a two-handed weapon.");
                return;
            }
        }
        // --- END NEW LOGIC ---

        // If the target slot is already full, unequip the item currently there.
        if (equippedItems.ContainsKey(itemToEquip.ItemData.EquipSlot) && equippedItems[itemToEquip.ItemData.EquipSlot] != null)
        {
            UnequipItem(itemToEquip.ItemData.EquipSlot);
        }

        equippedItems[itemToEquip.ItemData.EquipSlot] = itemToEquip;
        backpackItems.Remove(itemToEquip);
        GD.Print($"{GetParent().Name} equipped {itemToEquip.ItemData.ItemName} in {itemToEquip.ItemData.EquipSlot} slot.");
        
        EmitSignal(SignalName.EquipmentChanged);
    }
    
    public void EquipFirstWeaponFromBackpack()
    {
        ItemInstance weaponToEquip = backpackItems.FirstOrDefault(i => i.ItemData.ItemType == ItemType.Weapon);
        if(weaponToEquip != null)
        {
            GD.Print($"{GetParent().Name} draws {weaponToEquip.ItemData.ItemName} as part of its charge.");
            EquipItem(weaponToEquip);
        }
    }

    public void DropItemFromSlot(EquipmentSlot slot, Vector3 dropPosition)
    {
        if (equippedItems.ContainsKey(slot) && equippedItems[slot] != null)
        {
            ItemInstance item = equippedItems[slot];
            equippedItems[slot] = null;
              // --- UPDATED SECTION START ---
            // Now correctly calls the ItemManager Singleton
            if (ItemManager.Instance != null)
            {
                ItemManager.Instance.DropItem(item.ItemData, dropPosition);
            }
            else
            {
                GD.PrintErr("ItemManager Instance not found! Item dropped into void.");
            }
            // --- UPDATED SECTION END ---
            
            GD.Print($"{GetParent().Name} dropped {item.ItemData.ItemName} from {slot} slot.");

            EmitSignal(SignalName.EquipmentChanged);
        }
    }

   public bool IsEquipmentMelded { get; set; } = false;

    public Item_SO GetEquippedItem(EquipmentSlot slot)
    {
        if (IsEquipmentMelded && IsSlotMelded(slot)) return null;
        
        if (equippedItems.TryGetValue(slot, out ItemInstance item))
        {
            return item?.ItemData;
        }
        return null;
    }

    public ItemInstance GetEquippedItemInstance(EquipmentSlot slot)
    {
        if (IsEquipmentMelded && IsSlotMelded(slot)) return null;
        
        if (equippedItems.TryGetValue(slot, out ItemInstance item))
        {
            return item;
        }
        return null;
    }

    private bool IsSlotMelded(EquipmentSlot slot)
    {
        // Weapons and Armor meld into the new form and become inactive
        return slot == EquipmentSlot.MainHand || slot == EquipmentSlot.OffHand || slot == EquipmentSlot.Armor || slot == EquipmentSlot.Shield;
    }

    public int GetTotalStatModifierFromEquipment(StatToModify stat)
    {
        int totalModifier = 0;
      foreach (var itemInstance in equippedItems.Values)
        {
             if (itemInstance != null)
            {
                if (IsEquipmentMelded && IsSlotMelded(itemInstance.ItemData.EquipSlot)) continue;

                // Broken items provide halved AC bonus and doubled armor check penalty
                if (itemInstance.IsBroken)
                {
                    if (stat == StatToModify.ArmorClass && (itemInstance.ItemData.ItemType == ItemType.Armor || itemInstance.ItemData.ItemType == ItemType.Shield))
                    {
                        // Linq Sum logic converted
                        float rawSum = itemInstance.ItemData.Modifications
                            .Where(m => m.StatToModify == stat)
                            .Sum(m => m.ModifierValue);
                            
                        totalModifier += Mathf.FloorToInt(rawSum / 2f);
                        continue; // Skip normal processing for this item
                    }
                    // We will handle broken weapon penalties in CombatManager
                }
                
                totalModifier += itemInstance.ItemData.Modifications
                    .Where(m => m.StatToModify == stat)
                    .Sum(m => m.ModifierValue);
            }
        }
        return totalModifier;
    }

    /// <summary>
    /// Returns a list of all weapon instances currently in the backpack.
    /// </summary>
    public List<ItemInstance> GetBackpackWeapons()
    {
        return backpackItems.Where(i => i.ItemData.ItemType == ItemType.Weapon).ToList();
    }

    /// <summary>
    /// Equips a new weapon, handling the logic of unequipping existing items.
    /// This is the core of the weapon switching mechanic.
    /// </summary>
    /// <param name="weaponToEquip">The weapon instance from the backpack to equip.</param>
    public void SwitchToWeapon(ItemInstance weaponToEquip)
    {
        if (weaponToEquip == null || weaponToEquip.ItemData.ItemType != ItemType.Weapon) return;

        // Unequip current main hand weapon
        UnequipItem(EquipmentSlot.MainHand);

        // If the new weapon is two-handed, also unequip the off-hand/shield.
        if (weaponToEquip.ItemData.Handedness == WeaponHandedness.TwoHanded)
        {
            UnequipItem(EquipmentSlot.OffHand);
            UnequipItem(EquipmentSlot.Shield);
        }

        // Equip the new weapon
        EquipItem(weaponToEquip);
        GD.Print($"{GetParent().Name} switched to using {weaponToEquip.ItemData.ItemName}.");
    }
}