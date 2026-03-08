using Godot;

// =================================================================================================
// FILE: StomachRuptureController.cs
// PURPOSE: Tracks a creature's HP. When healed above the threshold, removes the ruptured stomach debuff.
// =================================================================================================
public partial class StomachRuptureController : GridNode
{
    private CreatureStats owner;
    private int hpWhenRuptured;

    public void Initialize(CreatureStats swallower)
    {
        owner = swallower;
        hpWhenRuptured = swallower.CurrentHP;
        owner.OnHealthChanged += CheckHealing;
    }

    private void CheckHealing(int currentHp, int maxHp)
    {
        if (currentHp > hpWhenRuptured)
        {
            GD.Print($"{owner.Name}'s stomach has healed enough to use Swallow Whole again.");
            owner.MyEffects.RemoveEffect("Stomach Ruptured");
            owner.OnHealthChanged -= CheckHealing;
            QueueFree();
        }
    }

    public override void _ExitTree()
    {
        if (owner != null)
        {
            owner.OnHealthChanged -= CheckHealing;
        }
    }
}