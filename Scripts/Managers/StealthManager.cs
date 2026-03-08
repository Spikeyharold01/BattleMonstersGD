using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: StealthManager.cs (GODOT VERSION)
// PURPOSE: A static utility class to handle the opposed checks for Stealth vs. Perception.
// It determines which creatures are aware of others at the start of combat.
// =================================================================================================

/// <summary>
/// Defines the different states of awareness one creature can have of another.
/// </summary>
public enum AwarenessState
{
    Unaware, // The creature does not know the other creature exists.
    Aware,   // The creature knows the other creature is present.
    Hidden   // Not used in this implementation, but could represent a creature actively hiding during combat.
}

/// <summary>
/// A static utility class that resolves pre-combat detection between all creatures in an encounter.
/// It simulates the "Stealth vs. Perception" opposed skill check from tabletop RPGs to determine
/// who is aware of whom, which is crucial for setting up surprise rounds.
/// </summary>
public static class StealthManager
{
    /// <summary>
    /// The main function that resolves who is aware of whom before combat starts. It takes a list
    /// of all combatants and returns a map detailing each creature's awareness of others.
    /// </summary>
    /// <param name="allCreatures">A list of all creatures in the potential encounter.</param>
    /// <returns>A dictionary where the Key is a creature and the Value is a list of other creatures they are aware of.</returns>
    public static Dictionary<CreatureStats, List<CreatureStats>> ResolvePreCombatDetection(List<CreatureStats> allCreatures)
    {
        // The final map to be returned. Key: Creature, Value: List of creatures they see.
        var awarenessMap = new Dictionary<CreatureStats, List<CreatureStats>>();
        
        // A temporary, more detailed map to store the pairwise awareness state between any two creatures.
        // It's a "dictionary of dictionaries" for easy lookup: creatureAwareness[Spotter][Hider] = AwarenessState.
        var creatureAwareness = new Dictionary<CreatureStats, Dictionary<CreatureStats, AwarenessState>>();

        // --- Initialization ---
        foreach (var creature in allCreatures)
        {
            awarenessMap[creature] = new List<CreatureStats>();
            creatureAwareness[creature] = new Dictionary<CreatureStats, AwarenessState>();
        }

        // --- Step 1: Determine who is attempting to be stealthy and roll their Stealth check ---
        foreach (var hider in allCreatures)
        {
            int stealthRanks = GetSkillBonus(hider, SkillType.Stealth);
            
            // For this implementation, we assume any creature with ranks in the Stealth skill is attempting to hide.
            if (stealthRanks > 0)
            {
                int stealthRoll = Dice.Roll(1, 20) + stealthRanks;
                GD.Print($"[Stealth] {hider.Name} is attempting to hide with a Stealth roll of {stealthRoll}.");

                // --- Step 2: Perform opposed Perception checks for every other creature ---
                foreach (var spotter in allCreatures)
                {
                    if (hider == spotter) continue; 

                    float distance = hider.GlobalPosition.DistanceTo(spotter.GlobalPosition);

                    // --- NEW: SPECIAL SENSES CHECK ---
                    // Rule: Blindsight and Blindsense automatically defeat Stealth.
                    if ((spotter.Template.HasBlindsight && distance <= spotter.Template.SpecialSenseRange) ||
                        (spotter.Template.BlindsenseRange > 0 && distance <= spotter.Template.BlindsenseRange))
                    {
                        // Line of effect is still required for these senses.
                        // Assuming LineOfSightManager.HasLineOfEffect is static and accepts (Node, Vector3, Vector3)
                        if (LineOfSightManager.HasLineOfEffect(spotter, spotter.GlobalPosition, hider.GlobalPosition))
                        {
                            creatureAwareness[spotter][hider] = AwarenessState.Aware;
                            GD.Print($"[Stealth] {spotter.Name} automatically detects {hider.Name} via Blindsight/Blindsense.");
                            continue; 
                        }
                    }

                    bool canSee = LineOfSightManager.GetVisibility(spotter, hider).HasLineOfSight;
                    bool canHear = SoundSystem.CanHear(spotter, hider);

                    // Integration point (surprise logic): detection is additive (Sight OR Hearing).
                    if (!canSee && canHear)
                    {
                        creatureAwareness[spotter][hider] = AwarenessState.Aware;
                        GD.Print($"[Stealth/Hearing] {spotter.Name} detects {hider.Name} by sound.");
                        continue;
                    }

                    if (!canSee)
                    {
                        creatureAwareness[spotter][hider] = AwarenessState.Unaware;
                        continue; 
                    }

                    // If there is line of sight, the spotter gets to make a Perception check.
                    int perceptionRoll = Dice.Roll(1, 20) + GetSkillBonus(spotter, SkillType.Perception);

                    // Recruitment intelligence may reveal tells and habitat patterns without changing creature base stats.
                    TacticalModifierBundle intelModifiers = RecruitmentRuntime.ActiveManager?.ResolveIntelligenceModifiers(hider.Template) ?? new TacticalModifierBundle();
                    perceptionRoll += Mathf.RoundToInt(intelModifiers.PerceptionBonus);

                    // Apply a penalty to the Perception check based on distance, a common TTRPG rule.
                    int distancePenalty = Mathf.FloorToInt(distance / 10f); // -1 penalty per 10 feet.
                    perceptionRoll -= distancePenalty;

                    // The core opposed check:
                    int effectiveStealthRoll = stealthRoll - Mathf.RoundToInt((RecruitmentRuntime.ActiveManager?.ResolveIntelligenceModifiers(hider.Template)?.AmbushResistanceBonus ?? 0f) * 10f);

                    if (perceptionRoll >= effectiveStealthRoll)
                    {
                        creatureAwareness[spotter][hider] = AwarenessState.Aware;
                        GD.Print($"[Stealth] {spotter.Name} (Perception: {perceptionRoll}) SPOTS {hider.Name} (Stealth: {effectiveStealthRoll}).");
                    }
                    else
                    {
                        creatureAwareness[spotter][hider] = AwarenessState.Unaware;
                        GD.Print($"[Stealth] {spotter.Name} (Perception: {perceptionRoll}) FAILS to spot {hider.Name} (Stealth: {effectiveStealthRoll}).");
                    }
                }
            }
            else // This block handles creatures who are NOT attempting to hide.
            {
                foreach (var spotter in allCreatures)
                {
                    if (hider == spotter) continue;
                    bool canSee = LineOfSightManager.GetVisibility(spotter, hider).HasLineOfSight;
                    bool canHear = SoundSystem.CanHear(spotter, hider);
                    if (canSee || canHear)
                    {
                        creatureAwareness[spotter][hider] = AwarenessState.Aware;
                    }
                }
            }
        }
        
        // --- Step 3: Populate the final, simplified awareness map ---
        foreach(var creature in allCreatures)
        {
            foreach(var other in allCreatures)
            {
                if (creature == other) continue;

                if (creatureAwareness[creature].ContainsKey(other) && creatureAwareness[creature][other] == AwarenessState.Aware)
                {
                    awarenessMap[creature].Add(other);
                }
            }
        }

        return awarenessMap;
    }

    /// <summary>
    /// A helper function to get a creature's total bonus for a given skill (ranks + ability modifier).
    /// </summary>
    /// <returns>The creature's total skill bonus.</returns>
    private static int GetSkillBonus(CreatureStats creature, SkillType skill)
    {
        // Using LINQ to find skill
        int rank = 0;
        foreach(var s in creature.Template.SkillRanks)
        {
            if (s.Skill == skill) 
            {
                rank = s.Ranks;
                break;
            }
        }
        
        int abilityMod = 0;
        switch (skill)
        {
            case SkillType.Perception:
                abilityMod = creature.WisModifier;
                break;
            case SkillType.Stealth:
                abilityMod = creature.DexModifier;
                break;
        }
        
        return rank + abilityMod;
    }
}