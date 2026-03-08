using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
// =================================================================================================
// FILE: Effect_LocateFoe.cs (GODOT VERSION)
// PURPOSE: Effect for locating hidden foes via perception.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_LocateFoe : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
var caster = context.Caster;
var stateController = caster.GetNodeOrNull<CombatStateController>("CombatStateController");
if (stateController == null) return;

GD.Print($"{caster.Name} uses a free action to listen and make a Perception check to locate foes.");

    var allEnemies = TurnManager.Instance.GetAllCombatants().Where(c => c.IsInGroup("Player") != caster.IsInGroup("Player"));

    foreach (var enemy in allEnemies)
    {
        var stealthController = enemy.GetNodeOrNull<StealthController>("StealthController");
        int stealthDC = (stealthController != null && stealthController.StealthResultAgainstObserver.ContainsKey(caster)) 
            ? stealthController.StealthResultAgainstObserver[caster] 
            : 0;
        
        // Replicating PerformPerceptionCheck logic manually since LineOfSightManager snippet didn't have it.
        int perceptionRoll = Dice.Roll(1, 20) + caster.GetSkillBonus(SkillType.Perception);
        
        // Apply distance penalty? Standard LoS Manager applies it.
        float distance = caster.GlobalPosition.DistanceTo(enemy.GlobalPosition);
        int distancePenalty = Mathf.FloorToInt(distance / 10f);
        int finalRoll = perceptionRoll - distancePenalty;
        
        // Visibility check (Blindness)
        // If blind, -4 penalty or auto-fail on visual?
        // "Locate Foe" usually implies hearing if blind.
        // Pathfinder: Perception to hear is opposed by Stealth.
        // Blind creature: +20 DC to Pinpoint, base to Notice?
        // Original code logic:
        // if roll >= finalDC + 20 -> Pinpoint
        // else if roll >= finalDC -> KnownSquare
        
        // Note: Original code parsed result string. I am doing logic directly.
        // If StealthDC is 0 (not sneaking), base DC to notice creature in combat is usually 0, modified by distance.
        
        int effectiveDC = stealthDC; 
        
        if (finalRoll >= effectiveDC)
        {
            GD.Print($"Success! {caster.Name} (Roll: {finalRoll}) vs DC {effectiveDC}.");
            
            if (finalRoll >= effectiveDC + 20)
            {
                stateController.UpdateEnemyLocation(enemy, LocationStatus.Pinpointed);
            }
            else
            {
                stateController.UpdateEnemyLocation(enemy, LocationStatus.KnownSquare);
            }
        }
        else
        {
            GD.Print($"Failure. {caster.Name} (Roll: {finalRoll}) vs DC {effectiveDC}.");
        }
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var stateController = context.Caster.GetNodeOrNull<CombatStateController>("CombatStateController");
    if (stateController == null) return 0f;
    
    bool needsToLocate = stateController.EnemyLocationStates.Any(kvp => kvp.Value == LocationStatus.Unknown);
    
    return (context.Caster.MyEffects.HasCondition(Condition.Blinded) && needsToLocate) ? 50f : 0f;
}
}