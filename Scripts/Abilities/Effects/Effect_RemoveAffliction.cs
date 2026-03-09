using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: BattleMonsters\Scripts\Abilities\Effects\Effect_RemoveAffliction.cs
// PURPOSE: Removes afflictions (Poison, Disease) via Caster Level check.
//          Handles "Neutralize Poison" (Standard/Greater) and "Remove Disease".
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_RemoveAffliction : AbilityEffectComponent
{
    [ExportGroup("Targeting")]
    [Export]
    [Tooltip("Which tag to target (e.g., Poison for Neutralize Poison, Disease for Remove Disease).")]
    public EffectTag TagToRemove = EffectTag.Poison;

    [Export]
    [Tooltip("Additional keywords to check in the effect name if tags are missing (e.g., 'Slime', 'Parasite').")]
    public Godot.Collections.Array<string> NameKeywords = new();

    [ExportGroup("Advanced Logic")]
    [Export]
    [Tooltip("If true, automatically succeeds on checks and heals ability damage (Greater Neutralize Poison).")]
    public bool AutoSucceed = false;

    [Export]
    [Tooltip("If true, heals all ability damage on success/auto-success (Greater Neutralize Poison).")]
    public bool HealAbilityDamage = false;

    [Export]
    [Tooltip("If targeting a creature capable of inflicting this affliction, apply this suppression effect.")]
    public StatusEffect_SO SuppressionEffect;

    [Export]
    [Tooltip("Duration of suppression in minutes per caster level.")]
    public int SuppressionDurationMinutesPerLevel = 10;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
            var caster = context.Caster;
               if (caster == null) return;
 foreach (var target in context.AllTargetsInAoE)
        {
            
			 if (target == null) continue;
            if (TargetFilter != null && !TargetFilter.IsTargetValid(caster, target)) continue;
			
                // --- MODE 1: CURE AFFLICTION ---
            var activeAfflictions = target.MyEffects.ActiveEffects.Where(e =>
                e.EffectData.Tag == TagToRemove ||
                NameKeywords.Any(k => e.EffectData.EffectName.ToLower().Contains(k.ToLower()))
            ).ToList();

            if (activeAfflictions.Any())
            {
                foreach (var affliction in activeAfflictions)
                {
                    int dc = affliction.SaveDC;
                    if (dc == 0) dc = 15; // Fallback

                    if (AutoSucceed)
                    {
                        GD.PrintRich($"[color=green]Auto-removing '{affliction.EffectData.EffectName}'.[/color]");
                        target.MyEffects.RemoveEffect(affliction.EffectData.EffectName);
						
						if (HealAbilityDamage)
                        {
                            target.HealAbilityDamage(AbilityScore.None, 999);
                        }
                    }
                    else
                    {
						int check = Dice.Roll(1, 20) + caster.Template.CasterLevel;
                        GD.Print($"{caster.Name} attempts to remove '{affliction.EffectData.EffectName}'. Check: {check} vs DC {dc}.");

                        if (check >= dc)
                        {
                            GD.PrintRich($"[color=green]Success! Removed.[/color]");
                            target.MyEffects.RemoveEffect(affliction.EffectData.EffectName);
                        }
                        else
                        {
                            GD.PrintRich($"[color=red]Failure.[/color]");
                        }
                    }
                }
            }
        else
            {
                GD.Print($"{target.Name} has no active afflictions matching {TagToRemove}.");
            }

            // --- MODE 2: SUPPRESS SOURCE (e.g. Poisonous Creature) ---
            // Only valid if we have a suppression effect configured
            if (SuppressionEffect != null)
            {
                // Check if target has abilities matching the tag keyword (e.g. "Poison")
                string keyword = TagToRemove.ToString();
                bool isSource = target.Template.SpecialAttacks.Any(a => a.AbilityName.Contains(keyword, System.StringComparison.OrdinalIgnoreCase)) ||
                                target.Template.MeleeAttacks.Any(m => m.SpecialQualities.Any(q => string.Equals(q, keyword, System.StringComparison.OrdinalIgnoreCase)));

                if (isSource)
                {
                 // Allow Will Save to negate suppression
                    int save = Dice.Roll(1, 20) + target.GetWillSave(caster, ability);
                    int spellDC = ability.SavingThrow.BaseDC;
                    if (ability.SavingThrow.IsDynamicDC) spellDC = 10 + ability.SpellLevel + caster.WisModifier;

                    if (save >= spellDC)
                    {
                        GD.Print($"{target.Name} resists having their {keyword} suppressed.");
                    }
                    else
                    {
                        GD.PrintRich($"[color=cyan]{target.Name}'s {keyword} is suppressed![/color]");
                        var instance = (StatusEffect_SO)SuppressionEffect.Duplicate();

                        int durationRounds = caster.Template.CasterLevel * 10 * SuppressionDurationMinutesPerLevel;
                        instance.DurationInRounds = durationRounds;

                        target.MyEffects.AddEffect(instance, caster, ability);
                    } 
                }
            }
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (context.PrimaryTarget == null) return 0f;
        
        // Cure Ally
        if (context.PrimaryTarget.IsInGroup("Player") == context.Caster.IsInGroup("Player"))
        {
            int count = context.PrimaryTarget.MyEffects.ActiveEffects.Count(e => e.EffectData.Tag == TagToRemove);
            if (count > 0) return 100f * count * (AutoSucceed ? 1.5f : 1f);
        }
        // Suppress Enemy
        else if (SuppressionEffect != null)
        {
            string keyword = TagToRemove.ToString();
            bool isSource = context.PrimaryTarget.Template.SpecialAttacks.Any(a => a.AbilityName.Contains(keyword, System.StringComparison.OrdinalIgnoreCase));
            if (isSource) return 50f;
        }

        return 0f;
    }
}