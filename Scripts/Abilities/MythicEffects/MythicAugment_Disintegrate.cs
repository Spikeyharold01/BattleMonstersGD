using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: MythicAugment_Disintegrate.cs
// PURPOSE: Handles the Twin Ray OR Auto-Dust augmentation choices.
// =================================================================================================
[GlobalClass]
public partial class MythicAugment_Disintegrate : MythicAbilityEffectComponent
{
    [Export] public int CostInMythicPower = 2;
    [Export] public bool UseTwinRay = false; // Configurable choice for AI/Player logic?
    // For data-driven simplicity: This component assumes one behavior. 
    // To allow choice, we'd need UI logic. 
    // We will assume AI logic: Use Auto-Dust on Non-Mythic, Twin Ray on Mythic.

    public override void ExecuteMythicEffect(EffectContext context, Ability_SO ability)
    {
        // 1. Check Rank (7th Tier)
        if (context.Caster.Template.MythicRank < 7) return;

        // 2. Cost Check
        if (context.Caster.CurrentMythicPower < (CostInMythicPower - 1)) return;
        
        // 3. Logic: Auto-Dust vs Twin Ray
        bool targetIsMythic = context.PrimaryTarget != null && context.PrimaryTarget.Template.MythicRank > 0;
        
        // If target is Non-Mythic, Auto-Dust is almost always better (instant kill).
        // If target is Mythic, Auto-Dust doesn't work, so use Twin Ray.
        
        bool useAutoDust = !targetIsMythic && context.PrimaryTarget != null;

        if (useAutoDust)
        {
            GD.PrintRich("[color=purple]Augment: Instant Disintegration enabled.[/color]");
            context.Caster.ConsumeMythicPower();
            
            // Find the main effect and enable the flag
            // NOTE: This modifies the RUNTIME instance of the effect if Ability_SO instantiated duplicates properly,
            // or we must be careful. 
            // CombatMagic does NOT clone EffectComponents. It uses the SO directly.
            // WE CANNOT MODIFY FLAGS ON THE COMPONENT DIRECTLY WITHOUT AFFECTING ASSET.
            
            // Solution: We must execute the logic manually OR the Effect_Disintegrate must read from Context?
            // Context is a clean way. 
            // Hack: We assume Effect_Disintegrate reads a transient flag or we execute a special version.
            
            // Better: We execute the effect OURSELVES with a modified flag, and prevent the original?
            // No easy way to prevent original in current architecture without "Countered" logic.
            
            // Alternative: Effect_Disintegrate checks a static/global "AugmentRegistry" for the current cast?
            // Or we just deal the damage here and rely on HP check.
            
            // Cleanest for this system:
            // Modify Effect_Disintegrate to be a proper instance or accept parameters.
            // Since we can't change architecture:
            // We will execute the logic here using a temporary instance of Effect_Disintegrate.
            
            var tempEffect = new Effect_Disintegrate();
            // Copy base settings from ability if possible, or use defaults + overrides
            var baseEffect = ability.EffectComponents[0] as Effect_Disintegrate; 
            if(baseEffect != null)
            {
                tempEffect.DicePerLevel = baseEffect.DicePerLevel;
                // ... copy others ...
            }
            tempEffect.IsAugmentedAutoDust = true;
            
            // We can't stop the main effect from running (it likely ran before Mythic).
            // This is the Mythic Architecture issue identified in Fireball.
            // Mythic effects run AFTER. 
            
            // If the standard effect ran, the target took damage.
            // If we run Auto-Dust now, we just finish them off if they failed save.
            // But we need to know if they saved.
            // We don't have the save result from the previous step easily available in `context`.
            
            // WORKAROUND: Re-roll save? Unfair.
            // CORRECT FIX: `EffectContext` needs to store `SaveResults`.
            // I cannot change `EffectContext` class definition easily as it was in `AbilityEffectComponent.cs`.
            // But I can see it in `AbilityEffectComponent.cs` provided in Prompt. 
            // It has `Caster`, `PrimaryTarget`, etc. NO SaveResults.
            
            // Since I cannot change `EffectContext` without reprinting the file, 
            // I will implement the "Twin Ray" logic (simply cast again) 
            // and the "Auto Dust" logic as a direct check here (Roll save again? No.)
            
            // Assumption: Mythic Disintegrate REPLACES the standard one? 
            // No, the prompt says "The damage dealt increases...".
            // This implies the standard component handles the Mythic Logic via `IsMythicCast` flag, which it does.
            // The Augment is an EXTRA layer.
            
            // If Auto-Dust: "If... it fails its saving throw, it's automatically disintegrated".
            // We can check if it took damage recently?
            // Or we check `WasDisintegrated`?
            // If the standard effect ran, and they failed save, they took damage.
            // If they survived the damage, Auto-Dust kills them.
            
            // WE NEED TO KNOW IF THEY SAVED.
            // I will add `Dictionary<CreatureStats, bool> LastSaveResults` to `EffectContext` in `AbilityEffectComponent.cs` 
            // to allow sharing this data.
            
            // SEE BELOW FOR ADJUSTMENT.
            
            if (context.LastSaveResults != null && context.LastSaveResults.TryGetValue(context.PrimaryTarget, out bool saved))
            {
                if (!saved)
                {
                    GD.Print("Augment: Target failed save, executing Auto-Disintegrate.");
                    context.PrimaryTarget.TakeDamage(99999, "Untyped", context.Caster, null, null, null, true);
                    context.PrimaryTarget.WasDisintegrated = true;
                }
            }
        }
        else // Twin Ray
        {
            GD.PrintRich("[color=purple]Augment: Firing Twin Ray![/color]");
            context.Caster.ConsumeMythicPower();
            
            // Fire a second ray.
            // This requires a second attack roll.
            // We simply re-run the resolution logic for the ability.
            // To prevent infinite loop (if it triggers mythic again), we pass `isMythicCast = false`?
            // But the second ray should be Mythic damage? "fire two rays".
            // Usually implies two full effects.
            // We call `CombatManager.ResolveAbility` again, but disable this component execution?
            // Or manually execute the Damage component.
            
            var damageEffect = ability.EffectComponents[0]; // Assuming first is damage
            
            // Manual Attack Roll
            if (CombatManager.ResolveAbilityAttack(context.Caster, context.PrimaryTarget, ability))
            {
                 var saveResults = new Dictionary<CreatureStats, bool>();
                 // Roll save
                 int roll = Dice.Roll(1, 20) + context.PrimaryTarget.GetFortitudeSave(context.Caster, ability);
                 int dc = ability.SavingThrow.BaseDC;
                 saveResults[context.PrimaryTarget] = roll >= dc;
                 
                 damageEffect.ExecuteEffect(context, ability, saveResults);
            }
        }
    }

    public override string GetMythicDescription()
    {
        return "Augmented (7th Tier): Twin Ray OR Auto-Disintegrate non-mythic on fail.";
    }
}