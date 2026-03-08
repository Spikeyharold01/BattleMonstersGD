using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: Effect_OnGrappleMaintain.cs (GODOT VERSION)
// PURPOSE: Generic Logic for "Constrict", "Death Roll", "Debilitating Constriction".
// Triggers whenever the creature chooses "Damage" option on a Maintain Grapple check.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_OnGrappleMaintain : AbilityEffectComponent
{
[ExportGroup("Requirements")]
[Export]
[Tooltip("Target must be this size or smaller relative to attacker.")]
public int SizeDifferenceAllowed = 0; // 0 = Same size or smaller.

[ExportGroup("Damage")]
[Export]
[Tooltip("If true, deals the damage of a specific natural attack (e.g. Bite/Tentacle).")]
public bool UseNaturalAttackDamage = true;

[Export] public string NaturalAttackName = "bite"; 

[Export]
[Tooltip("Additional damage dice (e.g. Constrict bonus).")]
public Godot.Collections.Array<DamageInfo> ExtraDamage = new();

[ExportGroup("Status Effect")]
[Export]
[Tooltip("Effect to apply on success (e.g. Prone for Death Roll, Con Dmg for Constriction).")]
public StatusEffect_SO EffectToApply;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    var attacker = context.Caster;
    var target = context.PrimaryTarget;
    if (attacker == null || target == null) return;

    // 1. Size Check
    if ((int)target.Template.Size > (int)attacker.Template.Size + SizeDifferenceAllowed)
    {
        GD.Print($"{ability.AbilityName} failed: Target too large.");
        return;
    }

    GD.PrintRich($"[color=red]{attacker.Name} performs {ability.AbilityName} on {target.Name}![/color]");

    // 2. Deal Natural Attack Damage (Bite/Tentacle)
    if (UseNaturalAttackDamage)
    {
        var attack = attacker.Template.MeleeAttacks.FirstOrDefault(a => a.AttackName.ToLower().Contains(NaturalAttackName.ToLower()));
        if (attack != null)
        {
            // Roll Damage manually
            int dmg = 0;
            foreach(var info in attack.DamageInfo) dmg += Dice.Roll(info.DiceCount, info.DieSides) + info.FlatBonus;
            dmg += attacker.StrModifier; // Basic Str bonus
            
            // Assuming attack.DamageInfo[0] exists if attack != null
            if (attack.DamageInfo.Count > 0)
                target.TakeDamage(dmg, attack.DamageInfo[0].DamageType, attacker, null, attack);
        }
    }

    // 3. Deal Extra Damage (if any)
    if (ExtraDamage != null)
    {
        foreach(var dmg in ExtraDamage)
        {
            int roll = Dice.Roll(dmg.DiceCount, dmg.DieSides) + dmg.FlatBonus;
            target.TakeDamage(roll, dmg.DamageType, attacker);
        }
    }

    // 4. Apply Status (Prone / Ability Dmg)
    if (EffectToApply != null)
    {
        var instance = (StatusEffect_SO)EffectToApply.Duplicate();
        // If it's Prone, duration 0 (permanent until stand).
        // If it's Ability Damage, duration instant.
        target.MyEffects.AddEffect(instance, attacker);
    }
}

public override float GetAIEstimatedValue(EffectContext context) { return 100f; }
}