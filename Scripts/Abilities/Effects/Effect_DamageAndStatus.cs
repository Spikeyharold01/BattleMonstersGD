using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_DamageAndStatus.cs (GODOT VERSION)
// PURPOSE: A generic effect that deals damage, and IF damage is taken, applies a status effect.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_DamageAndStatus : AbilityEffectComponent
{
[ExportGroup("Damage Step")]
[Export] public Godot.Collections.Array<DamageInfo> DamagePacket = new();

[ExportGroup("Conditional Status Step")]
[Export]
[Tooltip("The effect applied if the target takes at least 1 point of damage.")]
public StatusEffect_SO SecondaryEffect;

[Export]
[Tooltip("Does the secondary effect allow its own save? (e.g. Poison allows Fort save).")]
public bool SecondaryEffectAllowsSave = true;

[Export] public SaveType SecondarySaveType = SaveType.Fortitude;

[Export]
[Tooltip("If 0, uses the Ability's DC. If >0, overrides it.")]
public int OverrideDC = 0;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    var caster = context.Caster;
    
    foreach (var target in context.AllTargetsInAoE)
    {
        if (target == caster) continue;

        // 1. Deal Damage
        int totalDamageTaken = 0;
        foreach(var dmg in DamagePacket)
        {
            int roll = Dice.Roll(dmg.DiceCount, dmg.DieSides) + dmg.FlatBonus;
            target.TakeDamage(roll, dmg.DamageType, caster, null, null, (final) => totalDamageTaken += final);
        }

        // 2. Conditional Logic
        if (totalDamageTaken > 0 && SecondaryEffect != null)
        {
            if (SecondaryEffect.IsMindControlEffect && target.HasImmunity(ImmunityType.MindAffecting)) continue;

            // 3. Secondary Save
            bool applied = true;
            if (SecondaryEffectAllowsSave)
            {
                int dc = (OverrideDC > 0) ? OverrideDC : ability.SavingThrow.BaseDC;
                if (ability.SavingThrow.IsDynamicDC) 
                {
                     // Simple logic for now as per source
                    dc = 15; 
                }

                int bonus = 0;
                switch(SecondarySaveType)
                {
                    case SaveType.Fortitude: bonus = target.GetFortitudeSave(caster); break;
                    case SaveType.Reflex: bonus = target.GetReflexSave(caster); break;
                    case SaveType.Will: bonus = target.GetWillSave(caster); break;
                }

                if (Dice.Roll(1, 20) + bonus >= dc) applied = false;
            }

            if (applied)
            {
                var instance = (StatusEffect_SO)SecondaryEffect.Duplicate();
                target.MyEffects.AddEffect(instance, caster, ability);
            }
        }
    }
}

public override float GetAIEstimatedValue(EffectContext context) { return 100f; }
}