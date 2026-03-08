using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_AlignmentBranch.cs (GODOT VERSION)
// PURPOSE: Executes different effects based on target alignment (Good/Evil/Law/Chaos/Neutral).
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_AlignmentBranch : AbilityEffectComponent
{
    [Export] public Godot.Collections.Array<AbilityEffectComponent> EffectsIfGood = new();
    [Export] public Godot.Collections.Array<AbilityEffectComponent> EffectsIfEvil = new();
    [Export] public Godot.Collections.Array<AbilityEffectComponent> EffectsIfNeutral = new();
    
    // Optional: Axis Checking (Law/Chaos) if needed later
    // [Export] public bool CheckLawChaosAxis = false; 

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        foreach (var target in context.AllTargetsInAoE)
        {
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;

            string align = target.Template.Alignment;
            bool handled = false;

            if (align.Contains("Good"))
            {
                ExecuteList(EffectsIfGood, target, context, ability, targetSaveResults);
                handled = true;
            }
            else if (align.Contains("Evil"))
            {
                ExecuteList(EffectsIfEvil, target, context, ability, targetSaveResults);
                handled = true;
            }
            
            // If not handled (Neutral), or explicitly Neutral
            if (!handled || align.Contains("Neutral") && !align.Contains("Good") && !align.Contains("Evil"))
            {
                // Note: True Neutral matches here. CN/LN match here if not Good/Evil.
                ExecuteList(EffectsIfNeutral, target, context, ability, targetSaveResults);
            }
        }
    }

    private void ExecuteList(Godot.Collections.Array<AbilityEffectComponent> list, CreatureStats target, EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> saveResults)
    {
        if (list == null) return;
        
        // Create single-target context for the sub-effect
        var subContext = new EffectContext 
        {
            Caster = context.Caster,
            PrimaryTarget = target,
            AimPoint = target.GlobalPosition,
            AllTargetsInAoE = new Godot.Collections.Array<CreatureStats> { target },
            Ability = context.Ability
        };
        
        // Filter save results for just this target
        var subSave = new Dictionary<CreatureStats, bool>();
        if (saveResults.ContainsKey(target)) subSave[target] = saveResults[target];

        foreach(var eff in list)
        {
            eff.ExecuteEffect(subContext, ability, subSave);
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        // Simple Average estimate
        return 50f;
    }
}