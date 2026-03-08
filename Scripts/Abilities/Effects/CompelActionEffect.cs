using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: CompelActionEffect.cs (GODOT VERSION)
// PURPOSE: An effect component for compelling a creature to perform a specific action (e.g., Command).
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class CompelActionEffect : AbilityEffectComponent
{
[Export] public CommandWord Command;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    CommandWord commandToExecute = context.SelectedCommand;
    if (commandToExecute == CommandWord.None)
    {
        GD.PrintErr("CompelActionEffect executed without a selected command.");
        return;
    }

     foreach (var target in context.AllTargetsInAoE)
    {
        if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;
        
        bool didSave = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
        // Mythic Command: If they save, they are staggered.
         if (didSave)
        {
            if (context.IsMythicCast)
            {
                GD.PrintRich($"[color=purple]{target.Name} saved, but is Staggered for 1 round by Mythic Command![/color]");
                var staggeredEffect = new StatusEffect_SO();
                staggeredEffect.EffectName = "Mythic Command Stagger";
                staggeredEffect.ConditionApplied = Condition.Staggered;
                staggeredEffect.DurationInRounds = 1;
                staggeredEffect.IsMindControlEffect = true;
                target.MyEffects.AddEffect(staggeredEffect, context.Caster, ability);
            }
            continue;
        }

        GD.Print($"{target.Name} is compelled to '{commandToExecute}'.");

        var commandEffect = new StatusEffect_SO();
        commandEffect.EffectName = $"Compelled: {commandToExecute}";
        commandEffect.DurationInRounds = 1; // Base duration
        commandEffect.IsMindControlEffect = true; // Mark this as a mind-affecting effect

        switch (commandToExecute)
        {
            case CommandWord.Approach:
                commandEffect.ConditionApplied = Condition.CompelledApproach;
                target.MyEffects.AddEffect(commandEffect, context.Caster, ability);
                break;
            case CommandWord.Drop:
                target.MyInventory?.DropItemFromSlot(EquipmentSlot.MainHand, target.GlobalPosition);
                commandEffect.ConditionApplied = Condition.FumbledItems;
                target.MyEffects.AddEffect(commandEffect, context.Caster, ability);
                break;
            case CommandWord.Fall:
                if (ResourceLoader.Exists("res://Data/StatusEffects/Prone_Effect.tres"))
                {
                    var proneEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Prone_Effect.tres");
                    if(proneEffect != null) target.MyEffects.AddEffect((StatusEffect_SO)proneEffect.Duplicate(), context.Caster, ability);
                }
                break;
            case CommandWord.Flee:
                commandEffect.ConditionApplied = Condition.CompelledFlee;
                target.MyEffects.AddEffect(commandEffect, context.Caster, ability);
                break;
            case CommandWord.Halt:
                commandEffect.ConditionApplied = Condition.CompelledHalt;
                target.MyEffects.AddEffect(commandEffect, context.Caster, ability);
                break;
        }
    }
}

public float GetAIEstimatedValueForCommand(EffectContext context, CommandWord command)
{
    if (context.PrimaryTarget == null) return 0;
    
    var target = context.PrimaryTarget;
    var caster = context.Caster;
    float score = 0;

    // Base score for forcing an enemy to waste their turn
    score += 60f;

    switch (command)
    {
        case CommandWord.Approach:
            // Good for bringing a ranged enemy into melee.
            bool targetIsRanged = target.Template.KnownAbilities.Any(a => a.Range.GetRange(target) > 15f);
            if (targetIsRanged) score += 50f;
            break;

        case CommandWord.Drop:
            // Extremely valuable if the target has a powerful weapon or item.
            var targetWeapon = target.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
            if (targetWeapon != null)
            {
                // Score based on how "good" the weapon is
                int diceCount = targetWeapon.DamageInfo.Count > 0 ? targetWeapon.DamageInfo[0].DiceCount : 0;
                int dieSides = targetWeapon.DamageInfo.Count > 0 ? targetWeapon.DamageInfo[0].DieSides : 0;
                score += (diceCount * dieSides) * 5f;
            }
            break;

        case CommandWord.Fall:
            // Great against ranged attackers (-4 to ranged attacks while prone).
            bool isTargetRangedFall = target.Template.KnownAbilities.Any(a => a.Range.GetRange(target) > 15f);
            if (isTargetRangedFall) score += 70f;
            // Good for melee allies to attack (+4 to hit prone targets).
            // Use AISpatialAnalysis
            int meleeAllies = AISpatialAnalysis.FindAllies(caster)
                .Count(a => caster.GlobalPosition.DistanceTo(target.GlobalPosition) <= 10f); // Replaced Distance check
            score += meleeAllies * 20f;
            break;

        case CommandWord.Flee:
            // Good for getting a dangerous melee enemy away from the caster or a vulnerable ally.
            if (caster.GlobalPosition.DistanceTo(target.GlobalPosition) <= 10f)
            {
                score += 40f;
            }
            break;

        case CommandWord.Halt:
            // The best general-purpose lockdown. Especially good against enemies with full-attack routines.
            score += 30f;
            if (target.Template.BaseAttackBonus >= 6) score += 50f; // High BAB implies full attacks are dangerous
            break;
    }

    // AI INTELLIGENCE UPGRADE: Factor in the chance of success.
    int predictedWillSave = target.GetWillSave(caster); 
    int spellLevel = 1; // Command is a 1st level spell.
    
    int dc = 10 + spellLevel + caster.WisModifier; 
    
    int rollNeeded = dc - predictedWillSave;
    float chanceToSucceed = Mathf.Clamp((21f - rollNeeded) / 20f, 0f, 1f);

    return score * chanceToSucceed;
}

public override float GetAIEstimatedValue(EffectContext context)
{
    // This generic method is no longer sufficient for modal spells.
    // The scoring logic is now in AIAction_CastGenericAbility.
    return 0; 
}
}