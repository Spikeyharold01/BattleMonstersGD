using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: BattleMonsters\Scripts\Abilities\Effects\Effect_DualDamage.cs
// PURPOSE: Deals damage split between two types. 
//          Used for Mythic Augmented spells (Cold/Bludgeoning) or Flame Strike (Fire/Divine).
// =================================================================================================
[GlobalClass]
public partial class Effect_DualDamage : AbilityEffectComponent
{
    [ExportGroup("Damage 1")]
    [Export] public DamageInfo DamageA;
    [Export] public bool ScaleA_WithLevel = true;

    [ExportGroup("Damage 2")]
    [Export] public DamageInfo DamageB;
    [Export] public bool ScaleB_WithLevel = true;

    [ExportGroup("Conditions")]
    [Export]
    [Tooltip("If true, this split only happens if cast as Mythic (or Augmented).")]
    public bool RequireMythic = false;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        // If Mythic is required but not present, fallback to standard behavior?
        // Actually, for "Cone of Cold Augmented", this component would exist ALONGSIDE standard damage in the Mythic List,
        // OR the Ability_SO would be swapped.
        // Assuming this component REPLACES standard damage when condition met.
        
        if (RequireMythic && !context.IsMythicCast) return;

        int casterLevel = context.Caster.Template.CasterLevel;

        int diceA = ScaleA_WithLevel ? Mathf.Min(casterLevel, 15) : DamageA.DiceCount; // 15 cap common, or export Cap
        int diceB = ScaleB_WithLevel ? Mathf.Min(casterLevel, 15) : DamageB.DiceCount;

        // Roll Totals
        int totalA = Dice.Roll(diceA, DamageA.DieSides);
        int totalB = Dice.Roll(diceB, DamageB.DieSides);

        foreach (var target in context.AllTargetsInAoE)
        {
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;

            bool saved = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
            
            int finalA = saved ? totalA / 2 : totalA;
            int finalB = saved ? totalB / 2 : totalB;

if (finalA > 0) target.TakeDamage(finalA, DamageA.DamageType, context.Caster, null, null, null, false);
            if (finalB > 0) target.TakeDamage(finalB, DamageB.DamageType, context.Caster, null, null, null, false);
            
            GD.Print($"{target.Name} takes split damage: {finalA} {DamageA.DamageType}, {finalB} {DamageB.DamageType}.");
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (RequireMythic && !context.IsMythicCast) return 0f;
        // Simple avg calculation
        return 50f; 
    }
}