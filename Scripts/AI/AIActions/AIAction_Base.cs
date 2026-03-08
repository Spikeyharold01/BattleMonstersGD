using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;
// =================================================================================================
// FILE: AIAction_Base.cs (GODOT VERSION)
// PURPOSE: Base class for all AI Actions.
// ATTACH TO: Do not attach (Abstract Class).
// =================================================================================================
public static class AIActionFactory
{
// Kept for backward compatibility or simple logic generation
public static List<AIAction> CreateActionsForAbility(Ability_SO ability, AIController controller, List<CreatureStats> visibleTargets)
{
return new List<AIAction>(); // Logic moved to AIController generally
}
}
public abstract class AIAction
{
public string Name;
protected AIController controller;
protected TacticalData tactics;
protected AIPersonalityProfile profile;
public float Score { get; protected set; }

protected Weather_SO currentWeather;
protected bool isProne;

public AIAction(AIController controller)
{
    this.controller = controller;
    this.tactics = controller.GetTactics();
    this.profile = controller.GetProfile();
    this.currentWeather = WeatherManager.Instance?.CurrentWeather;
    
    var effectController = controller.MyStats.GetNodeOrNull<StatusEffectController>("StatusEffectController");
    this.isProne = effectController != null && effectController.HasCondition(Condition.Prone);
}

public abstract void CalculateScore();
public abstract Task Execute(); // Use Task instead of IEnumerator for async execution

public virtual CreatureStats GetTarget() => null; 

public void BoostScore(float multiplier, string reason)
{
    if (this.Score > 0)
    {
        this.Score *= multiplier;
        this.Name += reason;
    }
}
}