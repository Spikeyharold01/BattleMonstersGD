// =================================================================================================
// FILE: Item_SO.cs (Godot C# Version)
// PURPOSE: Resource defining base data for any item in the game.
// REVISED: Replaced [CreateAssetMenu] with [GlobalClass] and inherit from Resource.
// REVISED: Replaced List<T> with Godot.Collections.Array<T> for Inspector support.
// DO NOT ATTACH - Create assets from this in the FileSystem dock.
// =================================================================================================
using Godot;
using System.Collections.Generic; // Used for non-exported logic collections if needed

/// <summary>
/// Defines the broad category of an item. Used for sorting, rule checks, and UI.
/// </summary>

/// <summary>
/// This Resource acts as a data container for a single item template. It holds all the
/// base, unchanging properties of an item, like its name, bonuses, and damage.
/// </summary>
[GlobalClass]
public partial class Item_SO : Resource
{
    [ExportGroup("Core Info")]
    [Export] public string ItemName;
    [Export(PropertyHint.MultilineText)] public string Description;
    [Export] public ItemType ItemType;
    [Export] public PackedScene WorldPrefab; // Replaces GameObject. The visual representation when dropped.

    [ExportGroup("Equipping")]
    [Export] public bool IsEquippable = false;
    [Export] public EquipmentSlot EquipSlot = EquipmentSlot.None;

    [ExportGroup("Gameplay Stats")]
    // Godot arrays work best in inspector for Resources
    [Export] public Godot.Collections.Array<StatModification> Modifications = new();
    [Export] public int Hardness = 5;
    [Export] public int MaxHP = 10;

    [ExportGroup("Armor Specific Info")]
    [Export] public ArmorCategory ArmorCategory = ArmorCategory.None;

    [ExportGroup("Weapon Specific Info")]
    [Export] public WeaponHandedness Handedness = WeaponHandedness.OneHanded;
    [Export] public WeaponMaterial Material = WeaponMaterial.Normal;
    
    [Export] public WeaponType WeaponType = WeaponType.Melee;
    [Export] public float RangeIncrement = 0f;

    [Export] public bool IsCompositeBow = false;
    [Export] public int StrengthRating = 0;
    
    // Assuming DamageInfo is a Resource or GodotObject
    [Export] public Godot.Collections.Array<DamageInfo> DamageInfo = new();
    
    [Export] public bool IsThrowableRock = false;
    [Export] public bool HasTripFeature = false;
    [Export] public bool HasDisarmFeature = false;
    [Export] public bool HasBraceFeature = false;
    [Export] public bool IsLance = false;
    [Export] public bool IsDoubleWeapon = false;
    [Export] public bool IsImprovised = false;
    [Export] public bool HasBlockingFeature = false;
    [Export] public bool HasDeadlyFeature = false;
    [Export] public bool HasDistractingFeature = false;
    [Export] public bool HasFragileFeature = false;
    [Export] public bool IsMonkWeapon = false;
    [Export] public bool HasGrappleFeature = false;
    [Export] public float WeaponReach = 5f;
    [Export] public bool CreatesDeadZone = false;

    [ExportGroup("Critical Hit Info")]
    [Export] public bool HasKeenProperty = false;
    [Export] public int CriticalThreatRange = 20;
    [Export] public int CriticalMultiplier = 2;
    [Export] public bool HasVorpalProperty = false;

    [ExportGroup("Special Qualities")]
    [Export] public Godot.Collections.Array<string> SpecialQualities = new();
}