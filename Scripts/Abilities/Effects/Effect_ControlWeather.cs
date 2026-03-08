using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: Effect_ControlWeather.cs
// PURPOSE: Handles the Control Weather spell (Standard, Mythic, Augmented Mythic) and
//          creature-specific ecological variants (e.g., Akhlut).
// =================================================================================================
[GlobalClass]
public partial class Effect_ControlWeather : AbilityEffectComponent
{
    [ExportGroup("Weather Options")]
    [Export]
    [Tooltip("The list of weather states the caster is allowed to summon. For the Akhlut, this should only contain cold/windy variants.")]
    public Godot.Collections.Array<Weather_SO> AllowedWeathers = new();

    [ExportGroup("Mythic Augmented (6th Tier)")]
    [Export]
    [Tooltip("If true, allows the caster to bypass seasonal restrictions and reduces manifestation delay to 1 round (cost: 2 Mythic Power).")]
    public bool SupportsAugmentedMythic = true;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (WeatherManager.Instance == null) return;

        // Ensure we have a selected weather from the UI or AI
        Weather_SO chosenWeather = context.SelectedResource as Weather_SO;
        
        // Fallback for Player if UI selection isn't hooked up yet
        if (chosenWeather == null && AllowedWeathers.Count > 0)
        {
            chosenWeather = AllowedWeathers[0]; 
        }

        if (chosenWeather == null)
        {
            GD.PrintErr("Control Weather failed: No Weather_SO was selected or available.");
            return;
        }

        int casterLevel = context.Caster.Template.CasterLevel;
        
        // 1. Calculate Duration (4d12 hours standard = 4d12 * 3600 seconds)
        int durationHours = Dice.Roll(4, 12);
        
        // Druids double the duration
        bool isDruid = context.Caster.Template.Classes.Any(c => c.Equals("Druid", System.StringComparison.OrdinalIgnoreCase));
        if (isDruid) durationHours *= 2;

        // Mythic doubles duration
        if (context.IsMythicCast) durationHours *= 2;

        float durationSeconds = durationHours * 3600f;

        // 2. Calculate Manifestation Delay
        float delaySeconds = 600f; // Standard: 10 minutes (100 rounds)

        // Determine if this is an Augmented cast (costs 2 total mythic power)
        bool isAugmented = false;
        if (context.IsMythicCast && SupportsAugmentedMythic && context.Caster.CurrentMythicPower >= 1)
        {
            // The initial cast already consumed 1. We consume the 2nd one here.
            context.Caster.ConsumeMythicPower();
            isAugmented = true;
            context.IsAugmentedMythicCast = true;
        }

        if (isAugmented)
        {
            delaySeconds = 6f; // Augmented: 1 round
            GD.PrintRich($"[color=purple]{context.Caster.Name} augments Control Weather! It will manifest in 1 round![/color]");
        }
        else if (context.IsMythicCast)
        {
            int rounds = Mathf.Max(1, 11 - context.Caster.Template.MythicRank);
            delaySeconds = rounds * 6f;
            GD.PrintRich($"[color=purple]{context.Caster.Name} casts Mythic Control Weather. It will manifest in {rounds} rounds.[/color]");
        }

        // 3. Calculate Tactical Wind Direction
        // "You control the general tendencies of the weather, such as the direction..."
        Vector3 windDir = Vector3.Zero;
        if (chosenWeather.WindStrength > WindStrength.None)
        {
            var ai = context.Caster.GetNodeOrNull<AIController>("AIController");
            var threat = ai?.GetPerceivedHighestThreat();
            
            if (threat != null)
            {
                // Blow the wind towards the enemy to impede their movement towards the caster
                windDir = (threat.GlobalPosition - context.Caster.GlobalPosition).Normalized();
            }
            else
            {
                windDir = -context.Caster.GlobalTransform.Basis.Z; // Forward
            }
        }

        // 4. Send to WeatherManager
        WeatherManager.Instance.QueueWeatherChange(chosenWeather, delaySeconds, durationSeconds, windDir);
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        Weather_SO evaluatedWeather = context.SelectedResource as Weather_SO;
        if (evaluatedWeather == null) return 0f;

        float score = 0f;
        var caster = context.Caster;
        
        var enemies = AISpatialAnalysis.FindVisibleTargets(caster);
        var allies = AISpatialAnalysis.FindAllies(caster);
        allies.Add(caster);

        string wName = evaluatedWeather.WeatherName.ToLower();

        // 1. Synergy: Snow/Blizzard + Snowsight
        if (wName.Contains("snow") || wName.Contains("blizzard"))
        {
            int sightAdvantage = 0;
            foreach (var ally in allies) if (ally.Template.HasSnowsight) sightAdvantage++;
            foreach (var enemy in enemies) if (!enemy.Template.HasSnowsight) sightAdvantage++;
            
            score += sightAdvantage * 40f;
        }

        // 2. Synergy: Fog/Mist + Mistsight
        if (wName.Contains("fog") || wName.Contains("mist"))
        {
            int sightAdvantage = 0;
            foreach (var ally in allies) if (ally.Template.HasMistsight) sightAdvantage++;
            foreach (var enemy in enemies) if (!enemy.Template.HasMistsight) sightAdvantage++;
            
            score += sightAdvantage * 40f;
        }

        // 3. Synergy: Wind vs Size Categories
        if (evaluatedWeather.WindStrength >= WindStrength.Strong)
        {
            int windAdvantage = 0;
            foreach (var enemy in enemies)
            {
                if (evaluatedWeather.WindStrength == WindStrength.Strong && enemy.Template.Size <= CreatureSize.Tiny) windAdvantage++;
                if (evaluatedWeather.WindStrength == WindStrength.Severe && enemy.Template.Size <= CreatureSize.Small) windAdvantage += 2;
                if (evaluatedWeather.WindStrength == WindStrength.Windstorm && enemy.Template.Size <= CreatureSize.Medium) windAdvantage += 3;
            }
            
            // Penalize if allies are caught in the wind
            foreach (var ally in allies)
            {
                if (evaluatedWeather.WindStrength == WindStrength.Strong && ally.Template.Size <= CreatureSize.Tiny) windAdvantage--;
                if (evaluatedWeather.WindStrength == WindStrength.Severe && ally.Template.Size <= CreatureSize.Small) windAdvantage -= 2;
                if (evaluatedWeather.WindStrength == WindStrength.Windstorm && ally.Template.Size <= CreatureSize.Medium) windAdvantage -= 3;
            }

            score += windAdvantage * 50f;
        }

        // 4. Defensive Clearing: Removing harmful weather
        if (WeatherManager.Instance?.CurrentWeather != null)
        {
            if (WeatherManager.Instance.CurrentWeather.IsPrecipitation && !evaluatedWeather.IsPrecipitation)
            {
                // If current weather is hurting us, clearing it is highly valuable
                if (caster.Template.HasLightSensitivity == false && WeatherManager.Instance.CurrentWeather.WindStrength >= WindStrength.Severe)
                {
                    score += 150f;
                }
            }
        }

        // 5. Time Delay Factor (Only cast if we can survive the manifestation time)
        if (!context.IsMythicCast)
        {
            // If it takes 10 minutes (100 rounds) to cast/manifest, it is completely useless in active Arena combat.
            // But it is highly valuable during the Travel phase.
            if (TurnManager.Instance != null && TurnManager.Instance.GetAllCombatants().Count > 0)
            {
                return -1f; // Do not cast in active arena combat unless Mythic
            }
            else
            {
                score += 50f; // Travel phase utility buff
            }
        }
        else
        {
            // Mythic cast in combat: Score penalty based on how many rounds it will take to manifest
            int delayRounds = context.IsAugmentedMythicCast ? 1 : Mathf.Max(1, 11 - caster.Template.MythicRank);
            score -= (delayRounds * 15f);
        }

        return Mathf.Max(0f, score);
    }
}