using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_Disintegrate.cs
// PURPOSE: Generic logic for Disintegration effects (Ray, Touch, Breath).
//          Handles Damage scaling, Partial Saves, Object destruction, and Dusting on death.
// =================================================================================================
[GlobalClass]
public partial class Effect_Disintegrate : AbilityEffectComponent
{
    [ExportGroup("Damage Settings")]
    [Export] public int DicePerLevel = 2;
    [Export] public int DieSides = 6;
    [Export] public int MaxDice = 40;
    
    [Export] public int SaveDamageDice = 5;
    [Export] public int SaveDieSides = 6;

    [ExportGroup("Mythic Overrides (Optional defaults)")]
    [Export] public int MythicDicePerLevel = 3;
    [Export] public int MythicMaxDice = 60;
    [Export] public int MythicSaveDamageDice = 5;
    [Export] public int MythicSaveDieSides = 8;
    [Export] public int MythicConDamageDice = 1;
    [Export] public int MythicConDamageSides = 4;

    // Set by MythicAugment component
    public bool IsAugmentedAutoDust = false; 

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        // 1. Determine Target (Creature or Object)
        CreatureStats creatureTarget = context.PrimaryTarget;
        Node3D objectTarget = context.TargetObject;

        // 2. Handle Object Interaction
        if (creatureTarget == null && objectTarget != null)
        {
            // Check for StructureTraits
            var traits = objectTarget.GetNodeOrNull<StructureTraits>("StructureTraits") ?? 
                         objectTarget.GetParent()?.GetNodeOrNull<StructureTraits>("StructureTraits");

            if (traits != null && traits.DestroyedByDisintegrate)
            {
                traits.OnHitByDisintegrate();
                return;
            }
            
            // Standard Object Damage (10ft cube logic simplified to massive damage)
            var durability = objectTarget.GetNodeOrNull<ObjectDurability>("ObjectDurability") ?? 
                             objectTarget.GetParent()?.GetNodeOrNull<ObjectDurability>("ObjectDurability");
                             
            if (durability != null)
            {
                GD.Print($"{context.Caster.Name} disintegrates part of {objectTarget.Name}.");
                // Deal massive damage to destroy 10ft section
                // Objects take full damage, usually no save if unattended.
                int dmg = Dice.Roll(MaxDice, DieSides); 
                durability.TakeDamage(dmg, "Untyped"); 
            }
            return;
        }

        if (creatureTarget == null) return;

        // 3. Ranged Touch Attack
        // CombatManager usually handles this if Ability.AttackRollType is set.
        // We assume the effect only fires ON HIT. If CombatManager logic didn't filter misses,
        // we would need to check here. Current architecture implies Effect executes only if hit/valid.

        bool saved = targetSaveResults.ContainsKey(creatureTarget) && targetSaveResults[creatureTarget];
        int cl = context.Caster.Template.CasterLevel;
        int totalDamage = 0;
        int conDamage = 0;

        // --- AUGMENTED AUTO-DUST LOGIC ---
        if (IsAugmentedAutoDust && !saved && creatureTarget.Template.MythicRank == 0)
        {
            GD.PrintRich($"[color=red]Augmented Disintegrate instantly dusts {creatureTarget.Name}![/color]");
            creatureTarget.TakeDamage(99999, "Untyped", context.Caster, null, null, null, true);
            creatureTarget.WasDisintegrated = true;
            return;
        }

        // --- DAMAGE CALCULATION ---
        if (context.IsMythicCast)
        {
            if (saved)
            {
                totalDamage = Dice.Roll(MythicSaveDamageDice, MythicSaveDieSides);
                conDamage = 1;
            }
            else
            {
                int dice = Mathf.Min(cl * MythicDicePerLevel, MythicMaxDice);
                totalDamage = Dice.Roll(dice, DieSides);
                conDamage = Dice.Roll(MythicConDamageDice, MythicConDamageSides);
            }
        }
        else // Standard
        {
            if (saved)
            {
                totalDamage = Dice.Roll(SaveDamageDice, SaveDieSides);
            }
            else
            {
                int dice = Mathf.Min(cl * DicePerLevel, MaxDice);
                totalDamage = Dice.Roll(dice, DieSides);
            }
        }

        // Apply Damage
        GD.Print($"{ability.AbilityName} deals {totalDamage} damage to {creatureTarget.Name}. (Save: {saved})");
        creatureTarget.TakeDamage(totalDamage, "Untyped", context.Caster);

        // Apply Mythic Con Damage
        if (context.IsMythicCast && conDamage > 0)
        {
            GD.Print($"{creatureTarget.Name} takes {conDamage} Constitution damage.");
            creatureTarget.TakeAbilityDamage(AbilityScore.Constitution, conDamage);
        }

        // --- DISINTEGRATION CHECK ---
        // If reduced to 0 HP or 0 Con, turn to dust.
        bool deadByHP = creatureTarget.CurrentHP <= 0;
        bool deadByCon = (creatureTarget.Template.Constitution - creatureTarget.ConDamage) <= 0;

        if (deadByHP || deadByCon)
        {
            creatureTarget.WasDisintegrated = true;
            GD.PrintRich($"[color=red]{creatureTarget.Name} is entirely disintegrated! Only fine dust remains.[/color]");
            // Optional: Visual effect spawn here
            
            // Note: Equipment is unaffected per spell description.
            // CreatureStats.Die() handles disabling.
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (context.PrimaryTarget == null) return 0f;
        
        // Very high value for high-HP targets or constructs/objects
        float score = 150f;
        
        if (context.PrimaryTarget.Template.Type == CreatureType.Construct) score += 50f; // Works well on golems? Usually magic immunity blocks, but if not.
        if (context.IsMythicCast) score += 50f;

        return score;
    }
}