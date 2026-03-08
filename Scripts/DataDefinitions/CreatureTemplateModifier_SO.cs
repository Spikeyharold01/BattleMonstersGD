using Godot;

// =================================================================================================
// FILE: CreatureTemplateModifier_SO.cs (GODOT VERSION)
// PURPOSE: A data container for applying templates (e.g., Zombie, Fiendish) to an existing creature.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class CreatureTemplateModifier_SO : Resource
{
    [ExportGroup("Identity")]
    [Export]
    [Tooltip("If set to anything other than None, changes the creature's type.")]
    public CreatureType ChangeTypeTo = CreatureType.None;

    [ExportGroup("Movement")]
    [Export] 
    [Tooltip("Amount to increase or decrease land speed.")]
    public int BonusLandSpeed = 0;

    [ExportGroup("Combat Additions")]
    [Export] 
    [Tooltip("New natural attacks to append to the creature (e.g., the Void Zombie's Tongue).")]
    public Godot.Collections.Array<NaturalAttack> AddMeleeAttacks = new();
    
    [Export] 
    [Tooltip("New special qualities to add (e.g., 'Quick Strikes').")]
    public Godot.Collections.Array<string> AddSpecialQualities = new();
    
    [Export] 
    [Tooltip("New special attacks/abilities to add to the known abilities list.")]
    public Godot.Collections.Array<Ability_SO> AddSpecialAttacks = new();
}