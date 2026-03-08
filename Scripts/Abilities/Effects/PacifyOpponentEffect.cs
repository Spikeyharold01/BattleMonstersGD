using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: PacifyOpponentEffect.cs (GODOT VERSION)
// PURPOSE: Logic for Pacifying opponents via Diplomacy.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class PacifyOpponentEffect : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
CreatureStats target = context.PrimaryTarget;
CreatureStats caster = context.Caster;

if (target == null || caster == null) return;
    
    if (target.Template.Intelligence < 3 || !caster.SharesLanguageWith(target))
    {
        GD.Print($"Diplomacy failed: {target.Name} cannot be reasoned with (Int < 3 or no shared language).");
        return;
    }

    int dc = 25 + target.ChaModifier + target.GetWillSave(caster);

    int diplomacyRoll = Dice.Roll(1, 20) + caster.GetDiplomacyBonus();

    GD.Print($"{caster.Name} attempts to Pacify {target.Name} with Diplomacy. Rolls {diplomacyRoll} vs DC {dc}.");

    if (diplomacyRoll >= dc)
    {
        GD.PrintRich($"<color=green>Success!</color> {target.Name} is pacified for 1 round.");
        var hesitantEffect = new StatusEffect_SO();
        hesitantEffect.EffectName = "Pacified (Hesitant)";
        hesitantEffect.DurationInRounds = 1;
        hesitantEffect.ConditionApplied = Condition.CompelledHalt; 
        target.MyEffects.AddEffect(hesitantEffect, caster, ability);
    }
    else if (dc - diplomacyRoll >= 5)
    {
        GD.PrintRich($"<color=red>Critical Failure!</color> The attempt backfires and enrages {target.Name}!");
        var enragedEffect = new StatusEffect_SO();
        enragedEffect.EffectName = "Enraged by Diplomacy";
        enragedEffect.DurationInRounds = 1; 
        
        var attackMod = new StatModification { StatToModify = StatToModify.AttackRoll, ModifierValue = 2, BonusType = BonusType.Morale };
        var damageMod = new StatModification { StatToModify = StatToModify.MeleeDamage, ModifierValue = 2, BonusType = BonusType.Morale };
        
        enragedEffect.Modifications.Add(attackMod);
        enragedEffect.Modifications.Add(damageMod);
        target.MyEffects.AddEffect(enragedEffect, caster, ability);
    }
    else
    {
        GD.Print("Diplomacy failed. There is no effect.");
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    if (context.PrimaryTarget == null) return 0f;
    
    var caster = context.Caster;
    var target = context.PrimaryTarget;

    if (target.Template.Intelligence < 3 || !caster.SharesLanguageWith(target)) return 0f;

    int dc = 25 + target.ChaModifier + target.GetWillSave(caster);
    
    float chanceToSucceed = Mathf.Clamp((10.5f + caster.GetDiplomacyBonus() - dc) / 20f, 0f, 1f);
    float chanceToCritFail = Mathf.Clamp((dc - 5f - caster.GetDiplomacyBonus()) / 20f, 0f, 1f);

    float successValue = 300f; 
    float failureCost = -200f;

    return (successValue * chanceToSucceed) + (failureCost * chanceToCritFail);
}
}