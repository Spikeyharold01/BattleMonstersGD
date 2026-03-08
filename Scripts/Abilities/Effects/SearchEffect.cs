
using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: SearchEffect.cs (GODOT VERSION)
// PURPOSE: An effect component for the "Search" action, allowing active Perception checks.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class SearchEffect : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
CreatureStats caster = context.Caster;
if (caster == null) return;

GD.PrintRich($"<color=cyan>{caster.Name} takes a move action to Search the area...</color>");

    // Find all other creatures that are not currently visible to the caster.
    var allCombatants = TurnManager.Instance.GetAllCombatants();
    var hiddenCreatures = allCombatants
        .Where(c => c != caster && !LineOfSightManager.GetVisibility(caster, c).HasLineOfSight)
        .ToList();

    if (!hiddenCreatures.Any())
    {
        GD.Print("...but finds nothing out of the ordinary.");
        return;
    }

    // For each hidden creature, force a new Perception check.
    foreach (var hiddenTarget in hiddenCreatures)
    {
        // We need a way to get the target's original Stealth roll. This would ideally be stored in CombatMemory/StealthController.
        // In a strict port, the Unity code used `LineOfSightManager.PerformPerceptionCheck`.
        // But `LineOfSightManager.cs` provided in Part 15 DOES NOT HAVE `PerformPerceptionCheck`.
        // It only had `GetVisibility`.
        // However, `StealthController` has `StealthResultAgainstObserver`.
        // I will implement logic to check against stored stealth or re-roll if needed.
        // Since `PerformPerceptionCheck` is missing, I will implement the check logic inline here.
        
        // Logic: Get Stealth Roll -> Make Perception Roll -> Compare.
        
        int stealthResult = 0;
        var stealthCtrl = hiddenTarget.GetNodeOrNull<StealthController>("StealthController");
        
        if (stealthCtrl != null && stealthCtrl.StealthResultAgainstObserver.ContainsKey(caster))
        {
            stealthResult = stealthCtrl.StealthResultAgainstObserver[caster];
        }
        else
        {
            // Fallback: Re-roll
            stealthResult = Dice.Roll(1, 20) + hiddenTarget.GetSkillBonus(SkillType.Stealth);
        }

        int perceptionRoll = Dice.Roll(1, 20) + caster.GetSkillBonus(SkillType.Perception);
        
        // Log result
        if (perceptionRoll >= stealthResult)
        {
            GD.PrintRich($"<color=green>Success! {caster.Name} (Perception {perceptionRoll}) spots {hiddenTarget.Name} (Stealth {stealthResult}).</color>");
            
            // Update State
            var stateCtrl = caster.GetNodeOrNull<CombatStateController>("CombatStateController");
            if(stateCtrl != null)
            {
                stateCtrl.UpdateEnemyLocation(hiddenTarget, LocationStatus.Pinpointed);
            }
            
            // Update Stealth Controller (Observer now knows)
            // This might require clearing the stored result or just letting VisibilityManager handle next update.
        }
        else
        {
            GD.Print($"Failure. {caster.Name} (Perception {perceptionRoll}) fails to spot {hiddenTarget.Name} (Stealth {stealthResult}).");
        }
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var caster = context.Caster;
    
    var allCombatants = TurnManager.Instance.GetAllCombatants();
    
    // Count enemies we can't see
    var hiddenCreatures = allCombatants
        .Count(c => c != caster && 
                    !LineOfSightManager.GetVisibility(caster, c).HasLineOfSight && 
                    c.IsInGroup("Player") != caster.IsInGroup("Player"));

    if (hiddenCreatures > 0)
    {
        // The value is proportional to the number of enemies it might find.
        return 20f * hiddenCreatures;
    }

    return 0f;
}
}