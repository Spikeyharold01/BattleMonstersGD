// =================================================================================================
// FILE: Feat_SO.cs (Godot C# Version)
// PURPOSE: Resource defining a single Feat.
// REVISED: Replaced [CreateAssetMenu] with [GlobalClass] and inherit from Resource.
// REVISED: Replaced List<T> with Godot.Collections.Array<T> for Inspector support.
// DO NOT ATTACH - Create assets from this in the FileSystem dock.
// =================================================================================================
using Godot;
using System.Collections.Generic; // Available for logic, but Arrays used for Export


[GlobalClass]
public partial class Feat_SO : Resource
{
    [Export] public string FeatName;
    [Export(PropertyHint.MultilineText)] public string Description;
    [Export] public FeatType Type;

    [ExportGroup("Passive Stat Bonuses")]
    // Assuming StatModification is a Resource or GodotObject
    [Export] public Godot.Collections.Array<StatModification> Modifications = new();
    
    [ExportGroup("Activated Actions")]
    [Export] public Ability_SO AssociatedAbility;
}