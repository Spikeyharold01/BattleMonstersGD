using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: Effect_SoulSiphoning.cs (GODOT VERSION)
// PURPOSE: Handles Akhana's Soul Siphoning logic.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_SoulSiphoning : AbilityEffectComponent
{
[ExportGroup("Configuration")]
[Export] public StatusEffect_SO SoulBoundEffect;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    var caster = context.Caster;
    var target = context.PrimaryTarget;

    if (caster == null || target == null) return;

    // 1. Validation: Must be Grappling the target
    bool isGrapplingTarget = caster.CurrentGrappleState != null && 
                             caster.CurrentGrappleState.Controller == caster &&
                             caster.CurrentGrappleState.Target == target;

    if (!isGrapplingTarget)
    {
        GD.Print("Soul Siphoning failed: Not grappling target.");
        return;
    }

    // 2. Check Save
    if (targetSaveResults.ContainsKey(target) && targetSaveResults[target])
    {
        GD.Print($"{target.Name} resists Soul Siphoning.");
        return;
    }

    // 3. Calculate Damage Scaling
    float percent = 0.15f; // Default CR <= 10
    if (caster.Template.ChallengeRating > 20) percent = 0.30f;
    else if (caster.Template.ChallengeRating > 15) percent = 0.25f;
    else if (caster.Template.ChallengeRating > 10) percent = 0.20f;

    int damage = Mathf.FloorToInt(target.Template.MaxHP * percent);
    GD.Print($"{caster.Name} siphons soul! Dealing {damage} damage ({percent:P0} of Max HP).");

    // 4. Apply Damage (True Damage, cannot reduce below 1 HP)
    int currentHP = target.CurrentHP;
    if (currentHP - damage < 1) damage = currentHP - 1; // Cap damage
    
    if (damage > 0)
    {
        target.TakeDamage(damage, "True", caster);
    }

    // 5. Apply Status
    if (SoulBoundEffect != null)
    {
        var instance = (StatusEffect_SO)SoulBoundEffect.Duplicate();
        instance.DurationInRounds = 0; 
        target.MyEffects.AddEffect(instance, caster, ability);
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var caster = context.Caster;
    var target = context.PrimaryTarget;
    
    // --- STEP 1: HARD PREREQUISITES ---
    if (caster == null || target == null) return 0f;
    if (target.MyEffects.HasCondition(Condition.SoulBound)) return 0f; // Already bound
    if (caster.MyEffects.HasCondition(Condition.Stunned)) return 0f;

    // Must be grappling target
    bool isGrappling = caster.CurrentGrappleState != null && 
                       caster.CurrentGrappleState.Controller == caster && 
                       caster.CurrentGrappleState.Target == target;
    if (!isGrappling) return 0f;

    // Must be living
    if (target.Template.Type == CreatureType.Undead || target.Template.Type == CreatureType.Construct) return 0f;

    // --- STEP 2: TARGET PRIORITY SCORE (SVS) ---
    float svs = 0f;

    // +Recruitment Value
    if (target.Template.ChallengeRating >= caster.Template.ChallengeRating - 2) svs += 30f;
    if (CombatMemory.IsKnownToBeMythic(target)) svs += 20f; 
    
    // SubSchool property check for Healing not in Ability_SO provided snippet (Ability_SO has School only).
    // Assuming Name check or extended logic.
    if (target.Template.KnownAbilities.Any(a => a.School == MagicSchool.Conjuration && (a.AbilityName.Contains("Heal") || a.AbilityName.Contains("Cure")))) svs += 10f;

    // +Escape Risk
    bool canTeleport = target.Template.KnownAbilities.Any(a => a.AbilityName.Contains("Teleport") || a.AbilityName.Contains("Plane Shift") || a.AbilityName.Contains("Dimension Door"));
    if (canTeleport) svs += 25f;
    if (target.Template.Speed_Fly > 40f || target.Template.Speed_Land > 50f) svs += 15f;
    
    // +Combat Impact
    var highestThreat = CombatMemory.GetHighestThreat();
    if (target == highestThreat) svs += 15f;
    if (target.Template.PrimaryCastingStat != AbilityScore.None) svs += 10f;

    // -Penalty Factors
    float hpPercent = (float)target.CurrentHP / target.Template.MaxHP;
    if (hpPercent <= 0.2f) svs -= 30f; 
    if (target.Template.SubTypes != null && (target.Template.SubTypes.Contains("Swarm") || target.Template.Type == CreatureType.Vermin)) svs -= 20f; 
    
    // --- STEP 3: THRESHOLD CHECK ---
    if (svs < 40f)
    {
        // --- STEP 4: IMMEDIATE USE OVERRIDES ---
        bool forceUse = false;
        var visibleEnemies = AISpatialAnalysis.FindVisibleTargets(caster);
        if (visibleEnemies.Count == 1 && visibleEnemies[0] == target) forceUse = true;

        if (!forceUse) return 0f; 
    }

    // --- STEP 4: DELAY LOGIC ---
    if (hpPercent > 0.7f && caster.CurrentHP > caster.Template.MaxHP * 0.5f)
    {
         return 20f; 
    }

    // --- FINAL SCORE ---
    return 200f + svs; 
}
}