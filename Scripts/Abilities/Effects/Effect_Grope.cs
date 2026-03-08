using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: Effect_Grope.cs (GODOT VERSION)
// PURPOSE: Logic for blindly finding an enemy in a square.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_Grope : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
var caster = context.Caster;
var stateController = caster.GetNodeOrNull<CombatStateController>("CombatStateController");
if (stateController == null) return;

var creaturesAtAimpoint = context.AllTargetsInAoE;

    GD.Print($"{caster.Name} gropes blindly into the square at {context.AimPoint}.");

    foreach (var target in creaturesAtAimpoint)
    {
        if (Dice.Roll(1, 100) > 50) // 50% miss chance
        {
            GD.Print($"...and finds {target.Name}! Their location is pinpointed.");
            stateController.UpdateEnemyLocation(target, LocationStatus.Pinpointed);
        }
        else
        {
            GD.Print($"...but misses the target in the square.");
        }
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var stateController = context.Caster.GetNodeOrNull<CombatStateController>("CombatStateController");
    if (stateController == null) return 0f;

    bool canPinpoint = stateController.EnemyLocationStates.Any(kvp => kvp.Value == LocationStatus.KnownSquare);

    return (context.Caster.MyEffects.HasCondition(Condition.Blinded) && canPinpoint) ? 75f : 0f;
}
}