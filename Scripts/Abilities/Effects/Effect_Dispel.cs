using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: Effect_Dispel.cs (GODOT VERSION)
// PURPOSE: A versatile effect component for Dispel Magic and its variants.
// =================================================================================================
[GlobalClass]
public partial class Effect_Dispel : AbilityEffectComponent
{
    [ExportGroup("Dispel Configuration")]
    [Export]
    [Tooltip("If true, this functions as Greater Dispel Magic.")]
    public bool IsGreaterDispel = false;

    [Export]
    [Tooltip("The bonus to the dispel check (e.g., +4 for Greater Dispel counterspell).")]
    public int DispelCheckBonus = 0;

    [ExportGroup("Target Selection")]
    [Export] public bool AffectAllTargetsInAoE = false;
    [Export] public bool IncludeCaster = false;

    [ExportGroup("Filters")]
    [Export] public string EffectNameContains = "";
    [Export] public int MaxEffectSpellLevel = 0;
    [Export] public bool IgnoreMythicEffects = false;

    [ExportGroup("Execution Controls")]
    [Export] public bool RequireMythicCast = false;
    [Export] public bool AutoSuccess = false;

    [ExportGroup("Casting Interaction")]
    [Export]
    [Tooltip("If true, mythic casting with this dispel component can bypass verbal silence lockouts.")]
    public bool AllowsMythicSilenceBypassForVerbalCasting = false;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (RequireMythicCast && !context.IsMythicCast) return;

        List<CreatureStats> targetsToDispel = BuildTargetList(context, ability);
        if (!targetsToDispel.Any())
        {
            GD.Print("Dispel effect has no valid targets.");
            return;
        }

        int cap = IsGreaterDispel ? 20 : 10;
        int checkBonus = Mathf.Min(context.Caster.Template.CasterLevel, cap) + DispelCheckBonus;
        int areaDispelRoll = Dice.Roll(1, 20) + checkBonus;

        foreach (var target in targetsToDispel)
        {
            int individualRoll = Dice.Roll(1, 20) + checkBonus;
            int dispelRoll = (targetsToDispel.Count > 1) ? areaDispelRoll : individualRoll;

            var effectsOnTarget = target.MyEffects.ActiveEffects
                .Where(e => e.SourceCreature != null && e.SourceSpellLevel > 0 && !e.EffectData.IsUndispellable)
                .Where(PassesFilter)
                .OrderByDescending(e => e.SourceSpellLevel)
                .ToList();

            if (!effectsOnTarget.Any()) continue;

            int spellsToDispel = 1;
            if (IsGreaterDispel) spellsToDispel = Mathf.FloorToInt(context.Caster.Template.CasterLevel / 4f);
            if (context.IsMythicCast && !IsGreaterDispel) spellsToDispel = Mathf.Max(spellsToDispel, 2);

            int spellsDispelled = 0;
            int maxLevelDispelled = 0;

            foreach (var effect in effectsOnTarget)
            {
                if (spellsDispelled >= spellsToDispel) break;
                if (effect.EffectData.IsUndispellable) continue;

                bool success;
                if (AutoSuccess)
                {
                    success = true;
                }
                else
                {
                    int dc = 11 + effect.SourceCreature.Template.CasterLevel;
                    GD.Print($"{context.Caster.Name} attempts to dispel '{effect.EffectData.EffectName}' on {target.Name}. Dispel Check: {dispelRoll} vs DC: {dc}.");
                    success = dispelRoll >= dc;
                }

                if (!success) continue;

                GD.PrintRich($"[color=green]Success! '{effect.EffectData.EffectName}' is dispelled.[/color]");
                target.MyEffects.RemoveEffect(effect.EffectData.EffectName);
                spellsDispelled++;
                if (effect.SourceSpellLevel > maxLevelDispelled) maxLevelDispelled = effect.SourceSpellLevel;
            }

            if (context.IsMythicCast && spellsDispelled > 0 && targetsToDispel.Count == 1 && !AutoSuccess)
            {
                int healAmount = Dice.Roll(maxLevelDispelled, 4);
                context.Caster.HealDamage(healAmount);
                GD.Print($"Mythic Dispel heals caster for {healAmount}.");
            }
        }
    }

    private List<CreatureStats> BuildTargetList(EffectContext context, Ability_SO ability)
    {
        List<CreatureStats> targetsToDispel = new List<CreatureStats>();

        if (AffectAllTargetsInAoE)
        {
            foreach (var t in context.AllTargetsInAoE)
            {
                if (t != null && !targetsToDispel.Contains(t)) targetsToDispel.Add(t);
            }
        }
        else if (IsGreaterDispel && ability.TargetType == TargetType.Area_EnemiesOnly)
        {
            foreach (var t in context.AllTargetsInAoE)
            {
                if (t != null && !targetsToDispel.Contains(t)) targetsToDispel.Add(t);
            }
        }
        else if (context.PrimaryTarget != null)
        {
            targetsToDispel.Add(context.PrimaryTarget);
        }

        if (IncludeCaster && context.Caster != null && !targetsToDispel.Contains(context.Caster))
        {
            targetsToDispel.Add(context.Caster);
        }

        return targetsToDispel;
    }

    private bool PassesFilter(ActiveStatusEffect effect)
    {
        if (effect?.EffectData == null) return false;

        if (MaxEffectSpellLevel > 0 && effect.SourceSpellLevel > MaxEffectSpellLevel) return false;

        if (IgnoreMythicEffects && IsEffectMythic(effect)) return false;

        if (!string.IsNullOrWhiteSpace(EffectNameContains))
        {
            string effectName = effect.EffectData.EffectName ?? string.Empty;
            if (!effectName.Contains(EffectNameContains, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    private static bool IsEffectMythic(ActiveStatusEffect effect)
    {
        if (effect?.EffectData == null) return false;
        string effectName = effect.EffectData.EffectName ?? string.Empty;
        return effectName.Contains("mythic", StringComparison.OrdinalIgnoreCase);
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        float totalScore = 0;
        var primaryTarget = context.PrimaryTarget;
        if (primaryTarget == null) return 0f;

        var mostPowerfulBuff = primaryTarget.MyEffects.ActiveEffects
            .Where(e => e.SourceCreature != null && e.SourceSpellLevel > 0 && e.EffectData.AiTacticalTag != null && e.EffectData.AiTacticalTag.Role.ToString().Contains("Buff"))
            .OrderByDescending(e => e.SourceSpellLevel)
            .FirstOrDefault();

        if (mostPowerfulBuff == null) return 0f;

        float valueOfDispel = mostPowerfulBuff.EffectData.AiTacticalTag.BaseValue;
        int dc = 11 + mostPowerfulBuff.SourceCreature.Template.CasterLevel;
        float successChance = AutoSuccess ? 1f : Mathf.Clamp01((10.5f + context.Caster.Template.CasterLevel - dc) / 20f);

        if (successChance < 0.25f) return 0f;

        totalScore = valueOfDispel * successChance;
        if (IsGreaterDispel) totalScore *= 1.5f;

        return totalScore;
    }
}
