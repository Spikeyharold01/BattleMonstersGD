using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

// =================================================================================================
// FILE: Effect_RaiseDead.cs (GODOT VERSION)
// PURPOSE: Implements the "Raise Dead" spell logic.
// - Revives a dead creature (if not Undead/Construct/Outsider/Elemental).
// - Checks 'WasKilledByDeathEffect'.
// - Checks Soul Willingness (Ally = Yes, Enemy = No).
// - Restores HP to HD.
// - Applies 2 corruption stacks (or 2 CON drain if the creature is extremely fragile).
// - Cures normal poison/disease.
// - 50% spell loss chance.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_RaiseDead : AbilityEffectComponent
{
    [ExportGroup("Costs")]
    [Export]
    [Tooltip("Corruption profile used as the resurrection drawback.")]
    public CorruptionEffectDefinition ResurrectionCorruption;

    [Export]
    [Tooltip("Amount of Con drain if the target is Level 1.")]
    public int LevelOneConDrain = 2;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        CreatureStats caster = context.Caster;
        
        foreach (var target in context.AllTargetsInAoE)
        {
            if (TargetFilter != null && !TargetFilter.IsTargetValid(caster, target)) continue;

            // 1. Check if Dead
            if (!target.IsDead)
            {
                GD.Print($"{ability.AbilityName} fails: {target.Name} is not dead.");
                continue;
            }

            // 2. Creature Type Checks
            var type = target.Template.Type;
            if (type == CreatureType.Construct || type == CreatureType.Elemental || 
                type == CreatureType.Outsider || type == CreatureType.Undead)
            {
                GD.Print($"{ability.AbilityName} fails: Cannot raise creature type {type}.");
                continue;
            }

            // 3. Death Effect Check
            if (target.WasKilledByDeathEffect)
            {
                GD.Print($"{ability.AbilityName} fails: {target.Name} was killed by a death effect.");
                continue;
            }

            // 4. Soul Willing Check
            // Rule: "The subject’s soul must be free and willing to return."
            // Logic: Allies are willing. Enemies are unwilling to return to be killed again or captured.
            // In Godot, we check Group membership ("Player" vs "Enemy").
            bool isSameFaction = (caster.IsInGroup("Player") && target.IsInGroup("Player")) || 
                                 (caster.IsInGroup("Enemy") && target.IsInGroup("Enemy"));

            if (!isSameFaction)
            {
                GD.Print($"{ability.AbilityName} fails: {target.Name}'s soul is unwilling to return (Hostile Faction).");
                continue;
            }

            // --- PERFORM RESURRECTION ---
            GD.PrintRich($"<color=cyan>Raising {target.Name} from the dead...</color>");

            // A. Restore HP = Current HD
            // Note: Rules say "hit points equal to its current HD". 
            // e.g. Level 5 Fighter (5d10) has 5 HP when raised.
            int hitDice = GetHitDiceCount(target);
            
            // Re-enable physics/logic disabled on death
            target.SetPhysicsProcess(true);
            target.SetProcess(true);
            var col = target.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
            if (col != null) col.Disabled = false;
            
            // Reset death state by healing to full, then damaging down to specific HP
            target.HealDamage(9999); 
            
            int desiredHP = hitDice;
            int damageToApply = target.Template.MaxHP - desiredHP;
            
            if (damageToApply > 0)
            {
                target.TakeDamage(damageToApply, "True", null); // True damage ignores DR
                GD.Print($"{target.Name} is raised with {desiredHP} HP.");
            }

            // B. Ability Scores (0 -> 1)
            if ((target.Template.Strength - target.StrDamage) <= 0) target.HealAbilityDamage(AbilityScore.Strength, 1);
            if ((target.Template.Dexterity - target.DexDamage) <= 0) target.HealAbilityDamage(AbilityScore.Dexterity, 1);
            if ((target.Template.Constitution - target.ConDamage) <= 0) target.HealAbilityDamage(AbilityScore.Constitution, 1);
            if ((target.Template.Intelligence - target.IntDamage) <= 0) target.HealAbilityDamage(AbilityScore.Intelligence, 1);
            if ((target.Template.Wisdom - target.WisDamage) <= 0) target.HealAbilityDamage(AbilityScore.Wisdom, 1);
            if ((target.Template.Charisma - target.ChaDamage) <= 0) target.HealAbilityDamage(AbilityScore.Charisma, 1);

            // C. Cure Poison and Disease (Normal)
            var effectsToRemove = new List<string>();
            foreach (var effect in target.MyEffects.ActiveEffects)
            {
                string name = effect.EffectData.EffectName.ToLower();
                // Heuristic: If name contains poison/disease and NOT "Magical" or "Curse"
                if ((name.Contains("poison") || name.Contains("disease")) && 
                    !name.Contains("curse") && !name.Contains("magical"))
                {
                    effectsToRemove.Add(effect.EffectData.EffectName);
                }
            }
            foreach (var eName in effectsToRemove) target.MyEffects.RemoveEffect(eName);

            // D. Corruption burden or CON Drain
            if (hitDice <= 1)
            {
                // Fragile-body logic: 2 Con Drain
                if (target.Template.Constitution - target.ConDamage <= LevelOneConDrain)
                {
                    GD.Print($"...but {target.Name} is too weak (CON) to survive the raising! They die again.");
                    target.TakeDamage(9999, "Death"); // Re-kill
                    return;
                }
                target.TakeAbilityDamage(AbilityScore.Constitution, LevelOneConDrain);
                GD.Print($"{target.Name} takes {LevelOneConDrain} Constitution drain from the ordeal.");
            }
            else
            {
                if (ResurrectionCorruption != null)
                {
                    int stacksApplied = target.MyEffects.ApplyCorruption(ResurrectionCorruption, caster, 2);
                    GD.Print($"{target.Name} returns carrying {stacksApplied} resurrection corruption stacks.");
                }
            }

            // E. Spell Loss (50% chance)
            if (target.MyUsage != null)
            {
                // Full implementation of specific slot loss requires deeper access to AbilityUsageController logic.
                GD.Print("Spellcasting logic: 50% chance to lose prepared spells (not fully implemented in sim).");
            }

            // F. Re-add to Turn Manager
            TurnManager.Instance.ReviveCombatant(target);
        }
    }

    private int GetHitDiceCount(CreatureStats creature)
    {
        if (creature == null || string.IsNullOrEmpty(creature.Template.HitDice)) return 1;
        Match match = Regex.Match(creature.Template.HitDice, @"(\d+)d");
        if (match.Success)
        {
            if (int.TryParse(match.Groups[1].Value, out int hd)) return hd;
        }
        return 1;
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        // AI Logic: Prioritize raising dead allies.
        if (context.PrimaryTarget == null) return 0f;
        var target = context.PrimaryTarget;

        // 1. Must be dead
        if (!target.IsDead) return 0f;
        
        // 2. Must be Ally (Soul Willing Check)
        bool isSameFaction = (context.Caster.IsInGroup("Player") && target.IsInGroup("Player")) || 
                             (context.Caster.IsInGroup("Enemy") && target.IsInGroup("Enemy"));
        
        if (!isSameFaction) return 0f;

        // 3. Validations
        var type = target.Template.Type;
        if (type == CreatureType.Construct || type == CreatureType.Elemental || 
            type == CreatureType.Outsider || type == CreatureType.Undead) return 0f;
        if (target.WasKilledByDeathEffect) return 0f;

        // High Value
        float score = 500f; 
        
        // Bonus for strong allies
        score += target.Template.ChallengeRating * 50f;

        return score;
    }
}