using Godot;
using System.Collections.Generic;

// Standard C# class (not a Resource) to hold context data
public class EffectContext
{
    public CreatureStats Caster;
    public CreatureStats PrimaryTarget; 
	 public Ability_SO Ability;
    public Node3D TargetObject; // Replaces GameObject
    public Vector3 AimPoint;
    public Godot.Collections.Array<CreatureStats> AllTargetsInAoE = new(); // Use Godot Array
    public CommandWord SelectedCommand { get; set; } = CommandWord.None;
    public bool IsMythicCast { get; set; } = false;
	 public bool IsAugmentedMythicCast { get; set; } = false;
    public Resource SelectedResource { get; set; } // Used for spells with sub-choices (e.g., Weather_SO)
	 public Dictionary<CreatureStats, bool> LastSaveResults; 
}

[GlobalClass]
public abstract partial class AbilityEffectComponent : Resource
{
    [Export] public TargetFilter_SO TargetFilter;

    // Abstract methods must be implemented by child classes
    public abstract void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults);

    public abstract float GetAIEstimatedValue(EffectContext context);
}