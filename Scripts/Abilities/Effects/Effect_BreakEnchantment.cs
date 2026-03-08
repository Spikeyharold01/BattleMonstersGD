using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: BattleMonsters\Scripts\Abilities\Effects\Effect_BreakEnchantment.cs
// PURPOSE: Removes Enchantments, Transmutations, and Curses via Caster Level check.
//          Handles Mythic auto-success vs non-mythic.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_BreakEnchantment : AbilityEffectComponent
{
    [ExportGroup("Limits")]
    [Export] public int CasterLevelCap = 15;
    [Export] public int MaxSpellLevelRemovable = 5;

    [ExportGroup("Mythic Augment")]
    [Export] public bool IsMythicAugmented = false;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        int casterLevel = context.Caster.Template.CasterLevel;
        int checkBonus = Mathf.Min(casterLevel, CasterLevelCap);
        
        if (context.IsMythicCast)
        {
            checkBonus += context.Caster.Template.MythicRank;
        }

        foreach (var target in context.AllTargetsInAoE)
        {
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;

            // Find valid effects to break using EffectTag
            // Valid tags: Enchantment, Transmutation, Curse
            var effectsToBreak = target.MyEffects.ActiveEffects.Where(e => 
                e.EffectData.Tag == EffectTag.Curse ||
                e.EffectData.Tag == EffectTag.Enchantment ||
                e.EffectData.Tag == EffectTag.Transmutation
            ).ToList();

            if (!effectsToBreak.Any())
            {
                GD.Print($"{target.Name} has no enchantments, transmutations, or curses to break.");
                continue;
            }

            foreach (var effect in effectsToBreak)
            {
                bool isTargetEffectMythic = effect.EffectData.EffectName.Contains("(Mythic)") || (effect.SourceCreature != null && effect.SourceCreature.Template.MythicRank > 0);

                if (context.IsMythicCast && !isTargetEffectMythic)
                {
                    GD.PrintRich($"[color=cyan]Mythic Break Enchantment automatically removes '{effect.EffectData.EffectName}' from {target.Name}.[/color]");
                    RemoveAndReflect(target, effect, context);
                    continue;
                }

                if (effect.EffectData.IsUndispellable)
                {
                    int limit = MaxSpellLevelRemovable;
                    if (context.IsMythicCast) limit = 5 + (context.Caster.Template.MythicRank / 2);

                    if (effect.SourceSpellLevel > limit)
                    {
                        GD.Print($"Break Enchantment fails: '{effect.EffectData.EffectName}' is undispellable and level {effect.SourceSpellLevel} > {limit}.");
                        continue;
                    }
                }

                int dc = 11 + (effect.SourceCreature != null ? effect.SourceCreature.Template.CasterLevel : effect.SourceSpellLevel * 2);
                int roll = Dice.Roll(1, 20) + checkBonus;

                GD.Print($"{context.Caster.Name} attempts to break '{effect.EffectData.EffectName}' (Tag: {effect.EffectData.Tag}). Check: {roll} vs DC {dc}.");

                if (roll >= dc)
                {
                    GD.PrintRich($"[color=green]Success![/color]");
                    RemoveAndReflect(target, effect, context);
                }
                else
                {
                    GD.PrintRich($"[color=red]Failure.[/color]");
                }
            }
        }
    }

    private void RemoveAndReflect(CreatureStats victim, ActiveStatusEffect effect, EffectContext context)
    {
        // 1. Remove
        victim.MyEffects.RemoveEffect(effect.EffectData.EffectName);

        // 2. Mythic Augmented Reflection
        if (IsMythicAugmented && effect.SourceCreature != null)
        {
            var originalCaster = effect.SourceCreature;
            
            GD.PrintRich($"[color=purple]Mythic Augment reflects '{effect.EffectData.EffectName}' back at {originalCaster.Name}![/color]");
            
            var reflectedEffect = (StatusEffect_SO)effect.EffectData.Duplicate();
            
            // Standard Will save fallback if not specified
            SaveType type = SaveType.Will;
            if (reflectedEffect.AllowsRecurringSave) type = reflectedEffect.RecurringSaveType;
            
            int saveRoll = Dice.Roll(1, 20);
            switch(type)
            {
                case SaveType.Fortitude: saveRoll += originalCaster.GetFortitudeSave(context.Caster); break;
                case SaveType.Reflex: saveRoll += originalCaster.GetReflexSave(context.Caster); break;
                case SaveType.Will: saveRoll += originalCaster.GetWillSave(context.Caster); break;
            }
            
            if (saveRoll >= effect.SaveDC)
            {
                GD.Print($"{originalCaster.Name} saves against the reflected effect.");
            }
            else
            {
                originalCaster.MyEffects.AddEffect(reflectedEffect, context.Caster);
            }
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        float value = 0;
        foreach (var target in context.AllTargetsInAoE)
        {
            if (target.IsInGroup("Player") != context.Caster.IsInGroup("Player")) continue; 

            foreach (var effect in target.MyEffects.ActiveEffects)
            {
                if (effect.EffectData.Tag == EffectTag.Curse ||
                    effect.EffectData.Tag == EffectTag.Enchantment ||
                    effect.EffectData.Tag == EffectTag.Transmutation)
                {
                    // If it has a tactical tag, use that value, else fallback
                    if (effect.EffectData.AiTacticalTag != null)
                        value += effect.EffectData.AiTacticalTag.BaseValue;
                    else
                        value += 50f;
                }
            }
        }
        return value;
    }
}