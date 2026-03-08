using Godot;

// =================================================================================================
// FILE: AnimatedObjectController.cs
// PURPOSE: Tracks the tether between an animated object/tree and its creator.
// =================================================================================================
public partial class AnimatedObjectController : GridNode
{
    public CreatureStats Creator { get; private set; }
    private float controlRange;
    private CreatureStats myStats;
    private StatusEffect_SO inertEffect;

    public void Initialize(CreatureStats creator, float range)
    {
        this.Creator = creator;
        this.controlRange = range;
        myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
        AddToGroup("AnimatedObjects");

        // The "Inert" effect stops movement (Speed 0 via Entangled mapping or similar severe debuff).
        inertEffect = new StatusEffect_SO 
        { 
            EffectName = "Inert (Connection Lost)", 
            ConditionApplied = Condition.Entangled, // Standard way to root a creature in this system
            DurationInRounds = 0 
        };
        inertEffect.Modifications.Add(new StatModification { StatToModify = StatToModify.Dexterity, ModifierValue = -100, IsPenalty = true });
    }

    public void OnTurnStart()
    {
        if (myStats == null || myStats.CurrentHP <= 0) return;

        bool shouldBeInert = false;

        // 1. Check if Creator is incapacitated
        if (Creator == null || !GodotObject.IsInstanceValid(Creator) || Creator.IsDead || 
            Creator.MyEffects.HasCondition(Condition.Unconscious) || 
            Creator.MyEffects.HasCondition(Condition.Helpless))
        {
            shouldBeInert = true;
            GD.Print($"{myStats.Name}'s creator is incapacitated. It goes inert.");
        }
        // 2. Check Range
        else if (myStats.GlobalPosition.DistanceTo(Creator.GlobalPosition) > controlRange)
        {
            shouldBeInert = true;
            GD.Print($"{myStats.Name} is too far from its creator ({controlRange}ft limit). It goes inert.");
        }

        if (shouldBeInert)
        {
            if (!myStats.MyEffects.HasEffect(inertEffect.EffectName))
            {
                myStats.MyEffects.AddEffect((StatusEffect_SO)inertEffect.Duplicate(), null);
            }
        }
        else
        {
            if (myStats.MyEffects.HasEffect(inertEffect.EffectName))
            {
                GD.Print($"{myStats.Name} reconnects with its creator and animates again!");
                myStats.MyEffects.RemoveEffect(inertEffect.EffectName);
            }
        }
    }
}