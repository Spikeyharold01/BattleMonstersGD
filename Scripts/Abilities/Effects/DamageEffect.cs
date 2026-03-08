using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class DamageEffect : AbilityEffectComponent
{
    [Export] public DamageInfo Damage; // Ensure DamageInfo is a Resource
    [Export] public SaveEffect EffectOnSave = SaveEffect.HalfDamage;
    [Export(PropertyHint.Range, "0,10,0.01")] public float DamageMultiplier = 1f;
	
    [ExportGroup("Optional Independent Save")]
    [Export] public bool UseIndependentSave = false;
    [Export] public SaveType IndependentSaveType = SaveType.None;
    [Export] public int IndependentBaseDC = 0;
    [Export] public bool IndependentIsSpecialAbilityDC = false;
    [Export] public AbilityScore IndependentDCStat = AbilityScore.None;

    [Export] public bool ScalesWithCasterLevel = false;

    [ExportGroup("Scaling")]
	[Export] public int DiceScalingDivisor = 0;
    [Export] public int DiceScalingPerStep = 1;
    [Export] public int MaximumScaledDiceCount = 0;
    [Export] public int ScalingFlatBonusDivisor = 0;
    [Export] public int MaxScalingBonus = 0;
    
    [ExportGroup("Mythic Overrides")]
    [Export] public bool HasMythicVersion = false;
    [Export] public int MythicDiceCount;
    [Export] public int MythicFlatPerLevel = 0;
    [Export] public int MythicMaxBonus = 0;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (Damage == null) return;

        foreach (var target in context.AllTargetsInAoE)
        {
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;

            // --- Attack Roll Check ---
            if (ability.AttackRollType != AttackRollType.None)
            {
                bool isHit = CombatManager.ResolveAbilityAttack(context.Caster, target, ability);
                if (!isHit)
                {
                    GD.Print($"...the attack on {target.Name} missed!");
                    continue; 
                }
            }

            // --- Damage Calculation ---
            int diceToRoll = Damage.DiceCount;
            int diceSides = Damage.DieSides;
            int calculatedFlatBonus = Damage.FlatBonus;

            if (context.IsMythicCast && HasMythicVersion)
            {
                diceToRoll = MythicDiceCount;
                int levelBonus = (MythicFlatPerLevel > 0) ? context.Caster.Template.CasterLevel * MythicFlatPerLevel : 0;
                if (MythicMaxBonus > 0) levelBonus = Mathf.Min(levelBonus, MythicMaxBonus);
                calculatedFlatBonus += levelBonus;
            }
            else
            {
             if (DiceScalingDivisor > 0)
                {
                    int scaledDice = Damage.DiceCount + (Mathf.FloorToInt((float)context.Caster.Template.CasterLevel / DiceScalingDivisor) * Mathf.Max(DiceScalingPerStep, 1));
                    diceToRoll = (MaximumScaledDiceCount > 0) ? Mathf.Min(scaledDice, MaximumScaledDiceCount) : scaledDice;
                }
                else if (ScalesWithCasterLevel)
                {
                    diceToRoll = 1 + Mathf.FloorToInt((context.Caster.Template.CasterLevel - 1) / 2f);
                }

                if (ScalingFlatBonusDivisor > 0)
                {
                    int levelBonus = Mathf.FloorToInt((float)context.Caster.Template.CasterLevel / ScalingFlatBonusDivisor);
                    if (MaxScalingBonus > 0) levelBonus = Mathf.Min(levelBonus, MaxScalingBonus);
                    calculatedFlatBonus += levelBonus;
                }
            }

            int totalDamage = Dice.Roll(diceToRoll, diceSides) + calculatedFlatBonus;
             if (!Mathf.IsEqualApprox(DamageMultiplier, 1f))
            {
                totalDamage = Mathf.FloorToInt(totalDamage * Mathf.Max(0f, DamageMultiplier));
            }
			
            bool didSave = ResolveSaveResult(context, ability, target, targetSaveResults);
            if (didSave)
            {
                if (EffectOnSave == SaveEffect.HalfDamage) totalDamage /= 2;
                if (EffectOnSave == SaveEffect.Negates) totalDamage = 0;
            }

            if (totalDamage > 0)
            {
                // Create temp pseudo-weapon for DR purposes
                var magicalPseudoWeapon = new Item_SO();
                magicalPseudoWeapon.Modifications.Add(new StatModification { StatToModify = StatToModify.AttackRoll, BonusType = BonusType.Enhancement, ModifierValue = 1 });

                 target.TakeDamage(totalDamage, Damage.DamageType, context.Caster, magicalPseudoWeapon, null, null, false);
            }
        }
    }


    private bool ResolveSaveResult(EffectContext context, Ability_SO ability, CreatureStats target, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (!UseIndependentSave || IndependentSaveType == SaveType.None)
        {
            return targetSaveResults.ContainsKey(target) && targetSaveResults[target];
        }

        int dc = CalculateIndependentDC(context, ability);
        int saveRoll = RollManager.Instance.MakeD20Roll(target);
        int saveBonus = 0;

        switch (IndependentSaveType)
        {
            case SaveType.Fortitude: saveBonus = target.GetFortitudeSave(context.Caster, ability); break;
            case SaveType.Reflex: saveBonus = target.GetReflexSave(context.Caster, ability); break;
            case SaveType.Will: saveBonus = target.GetWillSave(context.Caster, ability); break;
        }

        bool saved = (saveRoll + saveBonus) >= dc;
        GD.Print($"{target.Name} independent save vs {ability.AbilityName} damage (DC {dc}, {IndependentSaveType}): Rolled {saveRoll + saveBonus}. Success: {saved}");
        return saved;
    }

    private int CalculateIndependentDC(EffectContext context, Ability_SO ability)
    {
        if (IndependentBaseDC > 0) return IndependentBaseDC;

        if (IndependentIsSpecialAbilityDC)
        {
            int hd = CreatureRulesUtility.GetHitDiceCount(context.Caster, fallback: 1);
            int statMod = GetCasterAbilityModifier(context.Caster, IndependentDCStat);
            return 10 + Mathf.FloorToInt(hd / 2f) + statMod;
        }

        return ability?.SavingThrow?.BaseDC ?? 10;
    }

    private static int GetCasterAbilityModifier(CreatureStats caster, AbilityScore stat)
    {
        if (caster == null) return 0;

        switch (stat)
        {
            case AbilityScore.Charisma: return caster.ChaModifier;
            case AbilityScore.Wisdom: return caster.WisModifier;
            case AbilityScore.Constitution: return caster.ConModifier;
            case AbilityScore.Intelligence: return caster.IntModifier;
            case AbilityScore.Strength: return caster.StrModifier;
            case AbilityScore.Dexterity: return caster.DexModifier;
            default: return 0;
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (context.PrimaryTarget == null && context.AllTargetsInAoE.Count == 0) return 0;
        if (Damage == null) return 0;

        CreatureStats sampleTarget = context.PrimaryTarget ?? context.AllTargetsInAoE[0];
        if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, sampleTarget)) return 0;

        float avgDamage = ((Damage.DiceCount * (Damage.DieSides / 2f + 0.5f)) + Damage.FlatBonus) * Mathf.Max(0f, DamageMultiplier);
        // Assuming AIController.GetPredictedDamageMultiplier exists
        // float multiplier = context.Caster.AIController.GetPredictedDamageMultiplier(sampleTarget, Damage.DamageType);
        float multiplier = 1.0f; // Placeholder
        
        return avgDamage * multiplier * context.AllTargetsInAoE.Count;
    }
}