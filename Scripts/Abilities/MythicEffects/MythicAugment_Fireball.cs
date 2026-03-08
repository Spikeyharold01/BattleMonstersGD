using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: MythicAugment_Fireball.cs
// PURPOSE: Handles the Augmented (6th Tier) logic for Mythic Fireball.
//          - Increases Max Damage to 20d10.
//          - Increases Radius to 40ft.
//          - Bypasses Fire Resistance/Immunity.
// =================================================================================================
[GlobalClass]
public partial class MythicAugment_Fireball : MythicAbilityEffectComponent
{
    [Export] public int AugmentedMaxDice = 20;
    [Export] public float AugmentedRadius = 40f;
    [Export] public int CostInMythicPower = 2;

    public override void ExecuteMythicEffect(EffectContext context, Ability_SO ability)
    {
        // 1. Check Tier/Rank requirement (6th Tier)
        // Note: Pathfinder rules usually say "At 6th tier...". 
        // We check the caster's rank.
        if (context.Caster.Template.MythicRank < 6) return;

        // 2. Check Resource Cost (Expend 2 uses)
        // The base casting already consumed 1. We need 1 more (Total 2).
        if (context.Caster.CurrentMythicPower < (CostInMythicPower - 1)) 
        {
            GD.Print($"{context.Caster.Name} lacks mythic power for Augmented Fireball.");
            return;
        }
        
        // Consume the extra power
        context.Caster.ConsumeMythicPower(); 

        // 3. Modify Logic
        // We need to modify the DamageAndApplyEffectOnFail component that is about to run (or ran).
        // Mythic components usually run AFTER standard components in the current architecture (see CombatMagic.cs).
        // This is a problem for modifying damage parameters.
        
        // SOLUTION: 
        // We need to intercept execution. 
        // OR: We manually execute the damage logic here with overridden parameters, 
        // and ensure the standard component knows not to run or we re-run it?
        // Re-running is bad (double damage).
        //
        // CORRECT APPROACH FOR THIS ARCHITECTURE:
        // CombatMagic executes standard components, then mythic.
        // We cannot easily modify the standard execution retroactively.
        // 
        // HACK: We will execute the AUGMENTED damage here, and rely on the fact that `ExecuteEffect`
        // in `DamageAndApplyEffectOnFail` uses `context.IsMythicCast`.
        // We can add a flag to context `IsAugmented`?
        // 
        // BETTER: This script will perform the damage itself, covering the "Augmented" portion.
        // But the standard script also runs.
        // 
        // REVISED ARCHITECTURE: `CombatMagic` should check for Mythic components FIRST to set flags/data in `EffectContext`, 
        // then run components.
        // Since I cannot change `CombatMagic` flow heavily without breaking compatibility, I will use this component
        // to Apply the Augmented *Bonus* or *Replacement*.
        // 
        // The standard `DamageAndApplyEffectOnFail` does 10d10 (Mythic).
        // Augmented does 20d10 + Bypass.
        // 
        // We will perform the Augmented Damage here. To prevent double damage, we need to ensure the standard component
        // didn't already kill everyone or that we can subtract? No.
        // 
        // CLEANEST FIX: `DamageAndApplyEffectOnFail` checks for `MythicAugment_Fireball` in the ability list? No, decoupling.
        // 
        // CHOSEN SOLUTION: The `DamageAndApplyEffectOnFail` script updates provided in Step 1 allow us to inject `BypassResistance`.
        // However, setting properties on a Resource at runtime modifies the ASSET for everyone. BAD.
        // 
        // We must implement the logic entirely inside this script and remove the standard component from the ability? 
        // No.
        // 
        // We will execute the Augmented logic here. We assume the player selects "Fireball (Mythic Augmented)" 
        // as a separate ability entry if the mechanics are this distinct, OR we allow `CombatMagic` 
        // to find this component and execute it INSTEAD of standard damage.
        // 
        // For simplicity in this specific "Fireball" request:
        // I will implement `MythicAugment_Fireball` to do the damage. 
        // In the Editor, you will NOT put `DamageAndApplyEffectOnFail` on the Mythic version of the ability if it conflicts,
        // or you will use a `Condition` in the standard script.
        // 
        // ACTUALLY: The standard `DamageAndApplyEffectOnFail` handles the 1d10/level logic.
        // Augmented just raises the cap and adds bypass.
        // 
        // I will implement logic to re-calculate damage with the new cap and apply the difference if needed, 
        // OR simply deal the full damage here and we assume the standard component is disabled/not present 
        // on the "Augmented" version of the spell profile.
        
        // EXECUTION:
        var damageLogic = new DamageAndApplyEffectOnFail();
        damageLogic.Damage = new DamageInfo { DamageType = "Fire", DiceCount = 1, DieSides = 10 };
        damageLogic.ScalesWithCasterLevel = true;
        damageLogic.DiceScalingDivisor = 1;
        damageLogic.MaximumScaledDiceCount = 20; // Augmented Cap
        damageLogic.BypassResistanceAndImmunity = true; // Bypass
        
        // Find targets in 40ft radius (Augmented)
        var newTargets = AoEHelper.GetTargetsInBurst(context.AimPoint, new AreaOfEffect { Range = 40f }, "Creature");
        context.AllTargetsInAoE = newTargets; // Override context targets
        
        GD.PrintRich("[color=purple]Executing Augmented Mythic Fireball (20d10, Bypass Immunity, 40ft).[/color]");
        
        // We need save results for the new area
        var saveResults = new Dictionary<CreatureStats, bool>();
        int dc = ability.SavingThrow.BaseDC; // Or dynamic
        
        foreach(var t in newTargets)
        {
            int roll = Dice.Roll(1, 20) + t.GetReflexSave(context.Caster);
            saveResults[t] = roll >= dc;
        }

        damageLogic.ExecuteEffect(context, ability, saveResults);
    }

    public override string GetMythicDescription()
    {
        return "Augmented (6th Tier): Damage cap 20d10, Radius 40ft, Bypasses Fire Resistance/Immunity.";
    }
}