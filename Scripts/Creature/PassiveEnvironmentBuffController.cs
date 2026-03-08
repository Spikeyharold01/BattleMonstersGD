using Godot;
using System.Linq;
// =================================================================================================
// FILE: PassiveEnvironmentBuffController.cs (GODOT VERSION)
// PURPOSE: Applies a status effect when specific weather/environmental conditions are met.
// ATTACH TO: Creature Prefabs.
// =================================================================================================
[GlobalClass]
public partial class EnvironmentTrigger : Resource
{
[Export] public string AbilityName; // The ability name to check for (e.g., "Rain Frenzy")
[Export] public StatusEffect_SO EffectToApply; // The effect (e.g., Rage)
[Export] public string RequiredWeatherKeyword; // "rain", "storm", "snow"
[Export] public bool RequireNearSurface; // Special check for Adaro logic (within move speed of surface)
}
public partial class PassiveEnvironmentBuffController : GridNode
{
private CreatureStats myStats;

[Export] public Godot.Collections.Array<EnvironmentTrigger> Triggers = new();

public override void _Ready()
{
    myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
    
    // Ensure manual call from ActionManager or Hook if TurnManager emitted signals
    // Since TurnManager.cs in provided context does not have OnCreatureTurnStart signal,
    // we will rely on ActionManager calling OnTurnStart().
}

// Called by ActionManager.OnTurnStart
public void OnTurnStart()
{
    if (WeatherManager.Instance == null) return;

    foreach (var trigger in Triggers)
    {
        // 1. Check if creature actually has the ability
        if (myStats.Template == null || !myStats.Template.KnownAbilities.Any(a => a.AbilityName.Equals(trigger.AbilityName, System.StringComparison.OrdinalIgnoreCase))) continue;

        string effectName = $"{trigger.AbilityName} Effect";
        bool conditionMet = false;

        // 2. Check Weather
        var weather = WeatherManager.Instance.CurrentWeather;
        if (weather != null && weather.WeatherName.ToLower().Contains(trigger.RequiredWeatherKeyword.ToLower()))
        {
            conditionMet = true;
        }

        // 3. Special: Check "Near Surface" (Adaro Rule)
        if (conditionMet && trigger.RequireNearSurface)
        {
            var body = GetParent<Node3D>();
            GridNode myNode = GridManager.Instance.NodeFromWorldPoint(body.GlobalPosition);
            // Check if in water
            if (myNode.terrainType == TerrainType.Water)
            {
                // Check depth. Surface is depth 0. Move speed is e.g. 50ft (10 squares).
                // waterDepth is stored in nodes. 
                float depthInFeet = (myNode.waterDepth) * 5f;
                float moveSpeed = myStats.Template.Speed_Swim > 0 ? myStats.Template.Speed_Swim : myStats.Template.Speed_Land;
                
                // "Within a move action away from the water's surface"
                if (depthInFeet > moveSpeed) 
                {
                    conditionMet = false;
                    // GD.Print($"{myStats.Name} is too deep for Rain Frenzy ({depthInFeet}ft > {moveSpeed}ft).");
                }
            }
        }

        // 4. Apply or Remove Effect
        bool hasEffect = myStats.MyEffects.HasEffect(effectName);

        if (conditionMet && !hasEffect)
        {
            if (trigger.EffectToApply != null)
            {
                GD.PrintRich($"[color=cyan]{myStats.Name}'s {trigger.AbilityName} activates due to {trigger.RequiredWeatherKeyword}![/color]");
                var instance = (StatusEffect_SO)trigger.EffectToApply.Duplicate();
                instance.EffectName = effectName; // Override name so we can track/remove it
                instance.DurationInRounds = 0; // Permanent while condition holds
                myStats.MyEffects.AddEffect(instance, myStats);
            }
        }
        else if (!conditionMet && hasEffect)
        {
            GD.Print($"{myStats.Name}'s {trigger.AbilityName} ends (Conditions not met).");
            myStats.MyEffects.RemoveEffect(effectName);
        }
    }
}
}