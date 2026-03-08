using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: Effect_RemoveCurse.cs (GODOT VERSION)
// PURPOSE: Removes effects tagged as 'Curse' via a Caster Level check.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_RemoveCurse : AbilityEffectComponent
{
    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        CreatureStats caster = context.Caster;

        foreach (var target in context.AllTargetsInAoE)
        {
            if (TargetFilter != null && !TargetFilter.IsTargetValid(caster, target)) continue;

            // Find all active curses
            // Note: iterate backwards to remove safely
            for (int i = target.MyEffects.ActiveEffects.Count - 1; i >= 0; i--)
            {
                var effect = target.MyEffects.ActiveEffects[i];

                // Check Tag
                if (effect.EffectData.Tag == EffectTag.Curse)
                {
                    // DC Check: 1d20 + CL vs DC
                    int roll = Dice.Roll(1, 20) + caster.Template.CasterLevel;
                    int dc = effect.SaveDC; 
                    
                    // If DC wasn't stored (legacy), default to 11 + Spell Level?
                    if (dc == 0) dc = 11 + effect.SourceSpellLevel; // Fallback estimate

                    GD.Print($"{caster.Name} attempts to remove curse '{effect.EffectData.EffectName}' (DC {dc}). Roll: {roll}.");

                    if (roll >= dc)
                    {
                        GD.PrintRich($"[color=green]Success! Curse removed.[/color]");
                        target.MyEffects.RemoveEffect(effect.EffectData.EffectName);
                    }
                    else
                    {
                        GD.PrintRich($"[color=orange]Failure.[/color]");
                    }
                }
            }
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (context.PrimaryTarget == null) return 0f;
        
        // AI Logic: Do they have a curse?
        bool hasCurse = context.PrimaryTarget.MyEffects.ActiveEffects.Any(e => e.EffectData.Tag == EffectTag.Curse);
        
        if (!hasCurse) return 0f;

        // High value to cure allies
        if (context.Caster.IsInGroup("Player") == context.PrimaryTarget.IsInGroup("Player"))
        {
            return 200f;
        }

        return 0f;
    }
}