using Godot;

// =================================================================================================
// FILE: DistractionTarget.cs
// PURPOSE: Marks an object as a valid attack target for Low-INT AI.
// ATTACH TO: Decoy Prefabs (Dancing Lights Shape, Silent Image).
// =================================================================================================
public partial class DistractionTarget : Node3D
{
    [Export] public int MaxIntelligenceToFool = 5; // Dancing Lights is vague, so only fools simpletons.
    
    // We mock a CreatureStats component so the combat system accepts it as a target.
    // Or, simpler: We add a dummy CreatureStats to the prefab and this script modifies its "Threat" calculation.
}