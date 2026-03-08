using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_HoldBreath.cs (GODOT VERSION)
// PURPOSE: A data-centric marker for the "Hold Breath" ability.
// HOW TO USE: Add this component to a generic "Hold Breath" Ability resource in the Godot Inspector.
//             Any creature that possesses an ability containing this component will benefit
//             from significantly extended breath-holding capacity while underwater.
// =================================================================================================

[GlobalClass]
public partial class Effect_HoldBreath : AbilityEffectComponent
{
    [ExportGroup("Rules Data")]
    [Export]
    [Tooltip("The number of minutes the creature can hold its breath for every point of its Constitution score.")]
    public int MinutesPerConScore = 6;

    /// <summary>
    /// Calculates the number of combat rounds that the creature can safely stay underwater per point of Constitution.
    /// In this system, one combat round equals 6 seconds, which means there are 10 rounds in a single minute.
    /// This method converts the configured minutes into total rounds for use by the game mechanics.
    /// </summary>
    public int GetRoundsMultiplier()
    {
        // 10 rounds = 1 minute.
        return MinutesPerConScore * 10;
    }

    /// <summary>
    /// This method is called whenever the ability is activated, but for the "Hold Breath" trait,
    /// the actual benefit is provided passively while the creature is submerged.
    /// Therefore, this execution step simply logs that the ability is being recognized.
    /// </summary>
    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        // When this ability is active on a creature, their internal breathing system is
        // automatically enhanced based on the duration configured in the inspector.
        if (context?.Caster != null)
        {
            GD.Print($"[Hold Breath] {context.Caster.Name} is utilizing their extraordinary lung capacity ({MinutesPerConScore} minutes per Con point).");
        }
    }

    /// <summary>
    /// This method is used by the artificial intelligence to decide if it should "use" an ability.
    /// Because "Hold Breath" is a passive trait that is always active, it does not need to be
    /// chosen or activated like a spell. Therefore, we return a value of zero so the AI
    /// never tries to "cast" it as an action.
    /// </summary>
    public override float GetAIEstimatedValue(EffectContext context)
    {
        // Passive traits have no "cost" or "activation value" in the decision-making loop.
        return 0f;
    }
}
