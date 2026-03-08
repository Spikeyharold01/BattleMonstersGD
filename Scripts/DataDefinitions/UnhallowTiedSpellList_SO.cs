using Godot;

// =================================================================================================
// FILE: UnhallowTiedSpellList_SO.cs
// PURPOSE: A registry containing all valid Ability_SOs that can be tied to an Unhallow effect.
// =================================================================================================
[GlobalClass]
public partial class UnhallowTiedSpellList_SO : Resource
{
    [Export]
    [Tooltip("List of valid tied spells (e.g. Darkness, Freedom of Movement, Invisibility Purge).")]
    public Godot.Collections.Array<Ability_SO> AllowedSpells = new();
}