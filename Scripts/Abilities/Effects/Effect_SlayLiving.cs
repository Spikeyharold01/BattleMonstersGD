using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_SlayLiving.cs (GODOT VERSION)
// PURPOSE: Implements the "Slay Living" spell logic.
// - Melee touch attack.
// - affects only living creatures (Constructs/Undead immune).
// - Death descriptor (Death immunity applies).
// - Specific damage on Fail (12d6+CL) vs Success (3d6+CL).
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_SlayLiving : AbilityEffectComponent
{
[ExportGroup("Damage Settings")]
[Export]
[Tooltip("Damage dealt on a failed Fortitude save (Default: 12d6).")]
public DamageInfo DamageOnFail;

[Export]
[Tooltip("Damage dealt on a successful Fortitude save (Default: 3d6).")]
public DamageInfo DamageOnSuccess;

[Export]
[Tooltip("The damage type applied (Standard: NegativeEnergy).")]
public string DamageType = "NegativeEnergy";

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    // 1. Validation
    if (DamageOnFail == null || DamageOnSuccess == null)
    {
        GD.PrintErr($"Effect_SlayLiving on {ability.AbilityName} is missing DamageInfo references.");
        return;
    }

    CreatureStats caster = context.Caster;
    
    foreach (var target in context.AllTargetsInAoE)
    {
        if (TargetFilter != null && !TargetFilter.IsTargetValid(caster, target)) continue;

        // 2. "Living Creature" Check
        // Constructs and Undead are not living.
        if (target.Template.Type == CreatureType.Construct || target.Template.Type == CreatureType.Undead)
        {
            GD.Print($"{ability.AbilityName} fails: {target.Name} is not a living creature.");
            continue;
        }

        // 3. Death Effect Immunity Check
        // "School necromancy [death]" implies immunity to death effects blocks this.
        if (target.HasImmunity(ImmunityType.DeathEffects))
        {
            GD.Print($"{ability.AbilityName} fails: {target.Name} is immune to death effects.");
            continue;
        }

        // 4. Melee Touch Attack Check
        // The spell requires a successful melee touch attack.
        if (ability.AttackRollType != AttackRollType.None)
        {
            bool isHit = CombatManager.ResolveAbilityAttack(caster, target, ability);
            if (!isHit)
            {
                GD.Print($"...the touch attack for {ability.AbilityName} on {target.Name} missed!");
                continue;
            }
        }

        // 5. Calculate Damage based on Save
        // Note: The spell adds +1 point per Caster Level to BOTH outcomes.
        int casterLevel = caster.Template.CasterLevel;
        
        bool saved = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
        DamageInfo activeProfile = saved ? DamageOnSuccess : DamageOnFail;

        int totalDamage = Dice.Roll(activeProfile.DiceCount, activeProfile.DieSides);
        
        // Add Flat Bonus from profile (if any) + Caster Level
        totalDamage += activeProfile.FlatBonus + casterLevel;

        GD.PrintRich($"[color=purple]{ability.AbilityName} hits {target.Name}! (Save: {(saved ? "Success" : "Failure")}). Dealing {totalDamage} {DamageType} damage.[/color]");

        // 6. Apply Damage
        // We pass null for weapon/natural attack as this is a spell effect.
 target.TakeDamage(totalDamage, DamageType, caster, null, null, null, false);
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    if (context.PrimaryTarget == null || DamageOnFail == null || DamageOnSuccess == null) return 0f;

    var target = context.PrimaryTarget;
    var caster = context.Caster;

    // 1. Check Valid Target Logic (replicated from Execute for AI prediction)
    if (target.Template.Type == CreatureType.Construct || target.Template.Type == CreatureType.Undead) return 0f;
    if (target.HasImmunity(ImmunityType.DeathEffects)) return 0f;
    if (TargetFilter != null && !TargetFilter.IsTargetValid(caster, target)) return 0f;

    // 2. Estimate Hit Chance (Touch Attack)
    float hitChance = 1.0f;
    if (context.Ability.AttackRollType != AttackRollType.None)
    {
        int touchAC = CombatCalculations.CalculateFinalAC(target, true, 0, caster);
        int attackBonus = caster.Template.BaseAttackBonus + caster.StrModifier; // Melee Touch
        float rollNeeded = touchAC - attackBonus;
        hitChance = Mathf.Clamp((21f - rollNeeded) / 20f, 0.05f, 0.95f);
    }

    // 3. Estimate Save Chance
    int dc = 10 + context.Ability.SpellLevel + caster.WisModifier; // Assuming Cleric/standard caster stat
    if (context.Ability.SavingThrow.IsDynamicDC)
    {
         // Use actual dynamic logic if possible, or fallback estimate
         // We can't easily access the dynamic stat type here without a large switch, assuming Wis for now or using context.Caster.PrimaryCastingStat logic if available.
         // For safety, we use the BaseDC or a rough estimate.
         dc = context.Ability.SavingThrow.BaseDC;
    }
    
    int targetFort = target.GetFortitudeSave(caster, context.Ability);
    float rollNeededForSave = dc - targetFort;
    float failChance = Mathf.Clamp((rollNeededForSave - 1f) / 20f, 0.05f, 0.95f);
    float saveChance = 1.0f - failChance;

    // 4. Calculate Expected Damage
    int cl = caster.Template.CasterLevel;
    float avgFailDmg = (DamageOnFail.DiceCount * (DamageOnFail.DieSides / 2f + 0.5f)) + DamageOnFail.FlatBonus + cl;
    float avgSaveDmg = (DamageOnSuccess.DiceCount * (DamageOnSuccess.DieSides / 2f + 0.5f)) + DamageOnSuccess.FlatBonus + cl;

    float expectedDamage = (avgFailDmg * failChance) + (avgSaveDmg * saveChance);
    
    // Final Score weighted by Hit Chance
    return expectedDamage * hitChance * 1.5f; // 1.5x multiplier because burst damage is valuable
}
}