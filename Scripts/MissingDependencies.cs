using Godot;

// Patches missing classes from the Unity transfer
public class ACBreakdown { }

public enum SuggestedActionIntent { Support, Control, Attack, Defend, Retreat }

public class SuggestedAction 
{ 
	public SuggestedActionIntent Intent; 
}

public partial class EnvironmentalHazard : Node3D 
{ 
	public virtual void ApplyEffect(CreatureStats target) { } 
}

public class DebugDraw3D { }
