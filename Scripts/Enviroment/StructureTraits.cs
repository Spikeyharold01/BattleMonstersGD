using Godot;

// =================================================================================================
// FILE: StructureTraits.cs
// PURPOSE: Data container for structural properties (Transparency, Vulnerabilities).
//          Attached to ANY environmental object (Magical or Physical).
// =================================================================================================
[GlobalClass]
public partial class StructureTraits : Node
{
    [ExportGroup("Visibility")]
    [Export] public bool IsTransparent = false; // Wall of Force = True, Stone = False

    [ExportGroup("Properties")]
    [Export] public bool IsMagicalForce = false; // Blocks Ethereal?
    [Export] public bool BlocksBreathWeapons = true;
    
    [ExportGroup("Vulnerabilities")]
    [Export] public bool DestroyedByDisintegrate = false;
    [Export] public bool DestroyedByRodOfCancellation = false;
    [Export] public bool DestroyedBySphereOfAnnihilation = false;
	 [Export] public bool DestroyedByDispelMagic = false; // Added for Spiritual Weapon

    // Optional: Damage modifiers (e.g. Fire deals double damage to Ice wall)
    // Could be extended with a Dictionary<string, float> damageMultipliers

	public void OnHitByDisintegrate()
    {
        if (DestroyedByDisintegrate)
        {
            GD.Print($"{GetParent().Name} is instantly destroyed by Disintegrate!");
            GetParent().QueueFree();
            return;
        }
        
        // Add Mage's Disjunction logic here if needed (IsMagicalForce check)
    }
    
    public void OnContactWithItem(string itemName)
    {
        string lowerName = itemName.ToLower();
        if (DestroyedByRodOfCancellation && lowerName.Contains("rod of cancellation"))
        {
             GD.Print($"{GetParent().Name} is destroyed by Rod of Cancellation!");
             GetParent().QueueFree();
        }
        if (DestroyedBySphereOfAnnihilation && lowerName.Contains("sphere of annihilation"))
        {
             GD.Print($"{GetParent().Name} is destroyed by Sphere of Annihilation!");
             GetParent().QueueFree();
        }
    }
}