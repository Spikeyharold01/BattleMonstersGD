using Godot;
using System;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_HitDiceWord.cs
// PURPOSE: Generic "word" spell resolver (Holy Word / Blasphemy / Dictum / Word of Chaos style).
//          Fully data-driven via exported tier resources.
// =================================================================================================
[GlobalClass]
public partial class HitDiceWordStatusTier : Resource
{
    [Export] public string TierName = "";
    [Export(PropertyHint.Range, "0,50,1")] public int ThresholdOffset = 0;
    [Export] public StatusEffect_SO StatusEffect;
    [Export] public string FailedSaveDuration = "0";
    [Export] public string SuccessfulSaveDuration = "0";
    [Export] public int FailedDurationMultiplier = 1;
    [Export] public int SuccessfulDurationMultiplier = 1;
    [Export] public bool NegatedOnSave = false;
    [Export] public float AIEnemyScore = 50f;
    [Export] public float AIAllyPenalty = 50f;
}

[GlobalClass]
public partial class HitDiceWordKillTier : Resource
{
    [Export] public bool Enabled = false;
    [Export(PropertyHint.Range, "0,50,1")] public int ThresholdOffset = 0;
    [Export] public int SaveSuccessDamageDice = 0;
    [Export] public int SaveSuccessDamageSides = 0;
    [Export] public int SaveSuccessDamageBonusPerCasterLevel = 0;
    [Export] public int SaveSuccessDamageBonusMax = 0;
    [Export] public float AIEnemyScore = 250f;
    [Export] public float AIAllyPenalty = 250f;
}

[GlobalClass]
public partial class Effect_HitDiceWord : AbilityEffectComponent
{
    [ExportGroup("Alignment Filtering")]
    [Export] public string ExcludedAlignmentComponent = "";
    [Export] public bool AffectOnlyIfMissingExcludedAlignment = true;

    [ExportGroup("Tiered Status Effects")]
    [Export] public Godot.Collections.Array<HitDiceWordStatusTier> StatusTiers = new();

    [ExportGroup("Kill Tier")]
    [Export] public HitDiceWordKillTier KillTier;

    [ExportGroup("Optional Extraplanar Banishment")]
    [Export] public bool EnableExtraplanarBanishment = false;
    [Export] public bool AssumeCasterOnHomePlane = false;
    [Export] public int BanishmentSavePenalty = 0;
    [Export] public string BanishmentDurationDice = "0";
    [Export] public bool BanishmentAppliesHpDeficitOnReturn = false;
    [Export] public float BanishmentAIEnemyScore = 120f;
    [Export] public float BanishmentAIAllyPenalty = 120f;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (context?.Caster == null) return;

        int casterLevel = Math.Max(1, context.Caster.Template.CasterLevel);

        foreach (var target in context.AllTargetsInAoE)
        {
            if (target == null || target == context.Caster) continue;
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;
            if (!ShouldAffectByAlignment(target)) continue;

            int targetHd = CreatureRulesUtility.GetHitDiceCount(target, fallback: 1);
            if (targetHd > casterLevel) continue;

            bool saved = targetSaveResults.TryGetValue(target, out bool s) && s;

            foreach (var tier in StatusTiers)
            {
                if (tier == null || tier.StatusEffect == null) continue;
                if (targetHd > casterLevel - tier.ThresholdOffset) continue;

                string successDuration = tier.NegatedOnSave ? "0" : tier.SuccessfulSaveDuration;
                ApplyTimedEffect(target, context.Caster, ability, tier.StatusEffect, tier.FailedSaveDuration, successDuration, saved, tier.FailedDurationMultiplier, tier.SuccessfulDurationMultiplier);
            }

            if (KillTier != null && KillTier.Enabled && targetHd <= casterLevel - KillTier.ThresholdOffset)
            {
                ApplyKillTier(context, ability, target, saved, casterLevel);
            }

            TryApplyExtraplanarBanishment(context, ability, target);
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (context?.Caster == null) return 0f;

        int casterLevel = Math.Max(1, context.Caster.Template.CasterLevel);
        float score = 0f;

        foreach (var target in context.AllTargetsInAoE)
        {
            if (target == null || target == context.Caster) continue;
            if (!ShouldAffectByAlignment(target)) continue;

            int hd = CreatureRulesUtility.GetHitDiceCount(target, fallback: 1);
            if (hd > casterLevel) continue;

            bool isEnemy = target.IsInGroup("Player") != context.Caster.IsInGroup("Player");

            foreach (var tier in StatusTiers)
            {
                if (tier == null) continue;
                if (hd > casterLevel - tier.ThresholdOffset) continue;
                score += isEnemy ? tier.AIEnemyScore : -tier.AIAllyPenalty;
            }

            if (KillTier != null && KillTier.Enabled && hd <= casterLevel - KillTier.ThresholdOffset)
            {
                score += isEnemy ? KillTier.AIEnemyScore : -KillTier.AIAllyPenalty;
            }

            if (EnableExtraplanarBanishment && AssumeCasterOnHomePlane && CreatureRulesUtility.IsExtraplanar(target))
            {
                score += isEnemy ? BanishmentAIEnemyScore : -BanishmentAIAllyPenalty;
            }
        }

        return score;
    }

    private bool ShouldAffectByAlignment(CreatureStats target)
    {
        if (string.IsNullOrWhiteSpace(ExcludedAlignmentComponent)) return true;

        bool hasExcluded = CreatureRulesUtility.HasAlignmentComponent(target, ExcludedAlignmentComponent);
        return AffectOnlyIfMissingExcludedAlignment ? !hasExcluded : hasExcluded;
    }

    private static void ApplyTimedEffect(CreatureStats target, CreatureStats caster, Ability_SO ability, StatusEffect_SO effect, string failDuration, string successDuration, bool saved, int failMultiplier = 1, int successMultiplier = 1)
    {
        if (effect == null) return;

        int baseRounds = Dice.Roll(saved ? successDuration : failDuration);
        int multiplier = saved ? Math.Max(1, successMultiplier) : Math.Max(1, failMultiplier);
        int rounds = baseRounds * multiplier;
        if (rounds <= 0) return;

        var instance = (StatusEffect_SO)effect.Duplicate();
        instance.DurationInRounds = rounds;
        target.MyEffects.AddEffect(instance, caster, ability);
    }

    private void ApplyKillTier(EffectContext context, Ability_SO ability, CreatureStats target, bool saved, int casterLevel)
    {
        if (!saved)
        {
            target.TakeDamage(9999, "Death", context.Caster, null, null);
            return;
        }

        if (KillTier.SaveSuccessDamageDice <= 0 || KillTier.SaveSuccessDamageSides <= 0) return;

        int bonus = casterLevel * Math.Max(0, KillTier.SaveSuccessDamageBonusPerCasterLevel);
        if (KillTier.SaveSuccessDamageBonusMax > 0) bonus = Math.Min(bonus, KillTier.SaveSuccessDamageBonusMax);

        int damage = Dice.Roll(KillTier.SaveSuccessDamageDice, KillTier.SaveSuccessDamageSides) + bonus;
        target.TakeDamage(damage, "Untyped", context.Caster, null, null);
    }

    private void TryApplyExtraplanarBanishment(EffectContext context, Ability_SO ability, CreatureStats target)
    {
        if (!EnableExtraplanarBanishment || !AssumeCasterOnHomePlane) return;
        if (!CreatureRulesUtility.IsExtraplanar(target)) return;

        int dc = ability?.SavingThrow?.BaseDC ?? 10;
        int saveRoll = RollManager.Instance.MakeD20Roll(target) + target.GetWillSave(context.Caster, ability) + BanishmentSavePenalty;
        if (saveRoll >= dc) return;

        var controller = target.GetNodeOrNull<TemporaryBanishmentController>("TemporaryBanishmentController");
        if (controller == null)
        {
            controller = new TemporaryBanishmentController();
            controller.Name = "TemporaryBanishmentController";
            target.AddChild(controller);
        }

        int banishRounds = Math.Max(1, Dice.Roll(BanishmentDurationDice));
        controller.ApplyTemporaryBanishment(banishRounds, BanishmentAppliesHpDeficitOnReturn);
    }
}