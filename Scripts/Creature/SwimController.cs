using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: SwimController.cs (GODOT VERSION)
// PURPOSE: Manages a creature's state while swimming, including breath and fatigue.
// ATTACH TO: All creature scenes (as a child node of the CreatureStats root).
// =================================================================================================
public partial class SwimController : GridNode
{
	
private CreatureStats myStats;

/// <summary>
/// Searches the creature's known abilities to find the "Hold Breath" trait and retrieve its unique
/// breathing multiplier. This allows the specific rules for holding breath to be stored
/// in the data-centric ability itself rather than being hardcoded here.
/// </summary>
private int GetBreathMultiplierFromAbilities()
{
    // A creature can have many abilities, so we look through each one to find the
    // specific "Hold Breath" effect.
    if (myStats.Template?.KnownAbilities != null)
    {
        foreach (var ability in myStats.Template.KnownAbilities)
        {
            if (ability == null || ability.EffectComponents == null) continue;

            // We check each component of the ability to see if it is the "Hold Breath" rule.
            foreach (var component in ability.EffectComponents)
            {
                if (component is Effect_HoldBreath holdBreath)
                {
                    // Once found, we ask the ability how many rounds the creature can hold its breath.
                    return holdBreath.GetRoundsMultiplier();
                }
            }
        }
    }

    // If no special "Hold Breath" ability is found, we return 0 to indicate we should use
    // the standard creature rules instead.
    return 0;
}

// --- STATE TRACKERS ---
[ExportGroup("Swimming State")]
[Export] private bool isUnderwater = false;
[Export] private int roundsHoldingBreath = 0;
[Export] public bool HasAttemptedSwimCheck { get; set; } = false;
[Export] public int CurrentEnvironmentalDC { get; set; } = 10;
[Export] private int drowningConCheckDC = 10;
[Export] private float timeSpentSwimming = 0f;

public override void _Ready()
{
    // Cache reference to the parent CreatureStats
    myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
}

/// <summary>
/// Called at the start of a creature's turn to handle breath and drowning checks.
/// Should be called by ActionManager.OnTurnStart.
/// </summary>
public void OnTurnStart()
{
    var body = GetParent<Node3D>();
    GridNode currentNode = GridManager.Instance.NodeFromWorldPoint(body.GlobalPosition);
    
     // If not in water, reset all states.
    if (currentNode.terrainType != TerrainType.Water)
    {
        isUnderwater = false;
        roundsHoldingBreath = 0;
        drowningConCheckDC = 10;
        return;
    }
	// --- NEW: SALT WATER VULNERABILITY ---
    if (myStats.HasSpecialRule("Salt Water Vulnerability"))
    {
        bool isSaltWater = currentNode.environmentalTags.Contains("SaltWater") || 
                           (EnvironmentManager.Instance != null && EnvironmentManager.Instance.CurrentSceneProperties.Contains(EnvironmentProperty.Coastal));
        
        if (isSaltWater)
        {
            float depthInFeet = (currentNode.waterDepth + 1) * 5f;
            bool isImmersed = depthInFeet >= myStats.Template.VerticalReach || myStats.MyEffects.HasCondition(Condition.Prone);
            
            int dmgDice = isImmersed ? 4 : 1; // 4d6 immersion, 1d6 partial/wading
            int dmg = Dice.Roll(dmgDice, 6);
            GD.PrintRich($"[color=red]{myStats.Name} burns in the salt water! Takes {dmg} damage.[/color]");
            
            // Bypasses resistances because it "acts as an extremely strong acid to creatures with this weakness"
            myStats.TakeDamage(dmg, "Acid", null, null, null, null, true); 
        }
    }
	
	// Rule: A paralyzed swimmer cannot swim and may drown.
        // We simulate this by forcing them underwater immediately and treating it as "Breath Holding" failure eventually.
        // Or simpler: If Paralyzed, treat as if underwater even if on surface?
        if (myStats.MyEffects.HasCondition(Condition.Paralyzed) && !isNativeAquatic && !myStats.MyEffects.HasEffect("Water Breathing"))
        {
             if (!isUnderwater)
             {
                 GoUnderwater(); // They sink
             }
             // They cannot hold breath effectively if paralyzed? 
             // Pathfinder: "A paralyzed creature... can’t swim and may drown." 
             // It implies they act as if they ran out of breath faster or just sink. 
             // Standard logic: They just sink (GoUnderwater) and standard breath rules apply.
        }

// --- BREATHING CHECK ---
    // Native aquatic creatures, those with Water Breathing, and creatures with No Breath do not need to hold their breath.
    bool isNativeAquatic = myStats.Template.NaturalEnvironmentProperties != null && myStats.Template.NaturalEnvironmentProperties.Contains(EnvironmentProperty.Aquatic);
    
    if (isNativeAquatic || myStats.MyEffects.HasEffect("Water Breathing") || myStats.HasSpecialRule("No Breath"))
    {
 // They are fine, no checks needed.
        isUnderwater = true; // They are still underwater, just not holding breath
        roundsHoldingBreath = 0;
        drowningConCheckDC = 10;
        return;
    }

    // --- WATER WALKING CHECK ---
    if (myStats.MyEffects.HasCondition(Condition.WaterWalking))
    {
        if (isUnderwater)
        {
            var body = GetParent<Node3D>();
            body.GlobalPosition += new Vector3(0, 60f, 0); // Rise 60 ft towards surface
            
            GridNode newNode = GridManager.Instance.NodeFromWorldPoint(body.GlobalPosition);
            if (newNode.terrainType != TerrainType.Water)
            {
                isUnderwater = false;
                GD.PrintRich($"[color=cyan]{myStats.Name} breaches the surface due to Water Walk![/color]");
            }
            else
            {
                GD.PrintRich($"[color=cyan]{myStats.Name} is borne rapidly toward the surface by Water Walk.[/color]");
            }
        }
        roundsHoldingBreath = 0;
        drowningConCheckDC = 10;
        return;
    }

    // If we reach here, the creature is non-aquatic and must hold its breath.
    if (isUnderwater)
    {
        // --- CALCULATION FOR HOLDING BREATH ---
        // Normally, a creature can hold its breath for 2 rounds for every point of their Constitution score.
        // A combat round lasts for 6 seconds, so 2 rounds equals 12 seconds per point of Constitution.
        int multiplier = (myStats.Template.BreathHoldingMultiplier > 0) ? myStats.Template.BreathHoldingMultiplier : 2;

        // Check if the creature has a data-centric "Hold Breath" ability that provides a custom multiplier.
        int customMultiplier = GetBreathMultiplierFromAbilities();
        if (customMultiplier > 0)
        {
            // We use the higher value to ensure the creature gets the maximum benefit from their abilities.
            multiplier = Mathf.Max(multiplier, customMultiplier);
        }

        // The final limit is determined by multiplying the chosen multiplier by the creature's Constitution score.
        // This provides the total number of combat rounds the creature can safely stay underwater before
        // they begin to risk drowning.
        int maxRoundsToHoldBreath = multiplier * myStats.Template.Constitution;
        
        // Check if the creature has now exceeded their safe time limit for holding their breath.
        if (roundsHoldingBreath > maxRoundsToHoldBreath)
        {
            GD.Print($"{myStats.Name} must make a Constitution check to continue holding breath.");
            int conRoll = Dice.Roll(1, 20) + myStats.ConModifier;
            
            if (conRoll < drowningConCheckDC)
            {
                // Drowning: Fall unconscious and start taking lethal damage.
                GD.PrintRich($"[color=red]{myStats.Name} fails Constitution check ({conRoll} vs DC {drowningConCheckDC}) and begins to drown![/color]");
                
                // Rule: At 0 HP, immediately fall to -1 and start dying.
                if(myStats.CurrentHP == 0) myStats.TakeDamage(1, "Drowning");
                
                // Rule: If already dying, take 1d6 damage.
                if (myStats.CurrentHP < 0)
                {
                    myStats.TakeDamage(Dice.Roll(1, 6), "Drowning");
                }
            }
            else
            {
                 GD.PrintRich($"[color=green]{myStats.Name} succeeds Constitution check ({conRoll} vs DC {drowningConCheckDC}) and holds their breath for another round.[/color]");
            }
            drowningConCheckDC++; // DC increases each round.
        }

        roundsHoldingBreath++;
    }
}

/// <summary>
/// Reduces the amount of time a creature can hold its breath when they perform a tiring action.
/// When a creature is underwater and performs a standard or full-round action, they use up
/// more oxygen, which is represented by increasing the number of rounds they have been
/// holding their breath. This ensures that active combat underwater is more dangerous
/// than simply waiting.
/// </summary>
public void OnTakeStrenuousAction()
{
    if (isUnderwater)
    {
        // Creatures that can naturally breathe underwater, are using magic to do so, or
        // do not require breath at all do not need to worry about using up their oxygen.
        bool isNativeAquatic = myStats.Template.NaturalEnvironmentProperties != null && myStats.Template.NaturalEnvironmentProperties.Contains(EnvironmentProperty.Aquatic);
        if(isNativeAquatic || myStats.MyEffects.HasEffect("Water Breathing") || myStats.HasSpecialRule("No Breath")) return;

        // Determine the standard breathing limit based on the creature's traits.
        int multiplier = (myStats.Template.BreathHoldingMultiplier > 0) ? myStats.Template.BreathHoldingMultiplier : 2;

        // Check for a data-centric "Hold Breath" multiplier from abilities.
        int customMultiplier = GetBreathMultiplierFromAbilities();
        if (customMultiplier > 0)
        {
            multiplier = Mathf.Max(multiplier, customMultiplier);
        }

        int maxRoundsToHoldBreath = multiplier * myStats.Template.Constitution;

        // If the creature is still within its safe breathing window, performing a strenuous
        // action uses up one additional round's worth of oxygen from their current reserve.
        if (roundsHoldingBreath <= maxRoundsToHoldBreath)
        {
            roundsHoldingBreath++;
            GD.Print($"{myStats.Name} uses up an extra round of breath by performing a strenuous action underwater.");
        }
    }
}

/// <summary>
/// Called when a creature moves through water to track time for fatigue.
/// </summary>
public void LogTimeSwimming(float seconds)
{
    timeSpentSwimming += seconds;
    // 1 hour = 3600 seconds. A round is 6 seconds.
    if (timeSpentSwimming >= 3600f)
    {
        timeSpentSwimming = 0;
        GD.Print($"{myStats.Name} has been swimming for an hour and must make a check against fatigue.");
        int swimCheck = Dice.Roll(1, 20) + myStats.GetSkillBonus(SkillType.Swim);
        if (swimCheck < 20)
        {
            int fatigueDamage = Dice.Roll(1, 6);
            GD.PrintRich($"[color=orange]{myStats.Name} fails DC 20 Swim check ({swimCheck}) and takes {fatigueDamage} nonlethal damage from fatigue.</color>");
            myStats.TakeNonlethalDamage(fatigueDamage);
        }
    }
}

/// <summary>
/// Forces the creature to go underwater, usually from a failed Swim check.
/// </summary>
public void GoUnderwater()
{
    if (!isUnderwater)
    {
        isUnderwater = true;
        GD.PrintRich($"[color=orange]{myStats.Name} slips and goes underwater![/color]");
    }
}
}