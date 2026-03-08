using Godot;

// =================================================================================================
// FILE: TerrainTransformRule_SO.cs
// PURPOSE: A data container defining what happens when a creature enters or exits a specific terrain.
// =================================================================================================
[GlobalClass]
public partial class TerrainTransformRule_SO : Resource
{
    [Export]
    [Tooltip("The terrain type that triggers this transformation when entered.")]
    public TerrainType TargetTerrain = TerrainType.Water;

    [ExportGroup("Visuals & Stats")]
    [Export]
    [Tooltip("If assigned, hides the creature's default visuals and shows this prefab while in the terrain.")]
    public PackedScene VisualPrefabOverride;

    [Export]
    [Tooltip("If assigned, modifies the creature's base template (speeds, attacks) while in the terrain.")]
    public CreatureTemplateModifier_SO TemplateModifier;

    [ExportGroup("Transition Buffs")]
    [Export]
    [Tooltip("A temporary status effect applied to the creature the moment they ENTER this terrain (e.g., Shore Storming Momentum).")]
    public StatusEffect_SO BuffOnEnter;

    [Export]
    [Tooltip("A temporary status effect applied to the creature the moment they EXIT this terrain.")]
    public StatusEffect_SO BuffOnExit;
}