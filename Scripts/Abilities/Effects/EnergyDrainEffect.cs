using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: EnergyDrainEffect.cs (GODOT VERSION)
// PURPOSE: Legacy energy-drain bridge now routed through the Corruption Stack Framework.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class EnergyDrainEffect : AbilityEffectComponent
{
    [Export]
    [Tooltip("Corruption profile used by this draining effect.")]
    public CorruptionEffectDefinition CorruptionDefinition;

    [Export]
    [Tooltip("How many corruption stacks to apply on a failed save.")]
    public int StacksToApply = 1;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (CorruptionDefinition == null)
        {
            GD.PrintErr("EnergyDrainEffect is missing its CorruptionEffectDefinition asset!");
            return;
        }

        foreach (var target in context.AllTargetsInAoE)
        {
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;

            bool didSave = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
            if (didSave)
            {
                GD.Print($"{target.Name} saved against the draining effect.");
                continue;
            }

            int applied = target.MyEffects.ApplyCorruption(CorruptionDefinition, context.Caster, StacksToApply);
            GD.PrintRich($"<color=purple>{target.Name} fails its save and gains {applied} corruption stack(s).</color>");
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        return 150f * StacksToApply * context.AllTargetsInAoE.Count;
    }
}
