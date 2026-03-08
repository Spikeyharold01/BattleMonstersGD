using Godot;

// =================================================================================================
// FILE: SummonList_SO.cs
// PURPOSE: Generic data model for summon spell lists (Summon Monster, Summon Nature's Ally, etc.).
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================

[GlobalClass]
public partial class SummonEntry : Resource
{
    [Export] public CreatureTemplate_SO CreatureTemplate;
    [Export] public string CreatureNameFallback = "";
    [Export] public int SourceListLevel = 1;
    [Export] public int CountDice = 1;
    [Export] public int CountBonus = 0;
    [Export] public bool ApplyCelestialIfGood = false;
    [Export] public bool ApplyFiendishIfEvil = false;
    [Export] public string SubtypesCsv = "";
    [Export] public bool IsAlternativeOption = false;
}

[GlobalClass]
public partial class SummonList_SO : Resource
{
    [Export] public int SpellLevel = 1;
    [Export] public Godot.Collections.Array<SummonEntry> Entries = new();

    [Export]
    [Tooltip("Optional reference to the next lower summon list.")]
    public SummonList_SO LowerLevelList;
}