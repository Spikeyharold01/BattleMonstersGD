using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: AbilityCooldownController.cs (GODOT VERSION)
// PURPOSE: This component will be the "memory" for a creature's ability usage. Its sole job is
// to track which abilities are on cooldown and for how long, specifically for the "recharge dice" mechanic.
// ATTACH TO: Creature Root Node (as a child component Node).
// =================================================================================================

/// <summary>
/// This component is attached to a creature and manages the cooldowns for abilities that use
/// a "recharge dice" mechanic (e.g., a dragon's breath weapon that recharges after 1d4 rounds).
/// It acts as a memory, tracking which abilities are currently unavailable and ticking down their
/// cooldown timers each round.
/// </summary>
public partial class AbilityCooldownController : GridNode
{
    // A dictionary is used to efficiently store and look up the remaining cooldown duration for each ability.
    // The Key is the Ability_SO resource.
    // The Value is an integer representing the number of rounds left on its cooldown.
    private Dictionary<Ability_SO, int> abilityCooldowns = new Dictionary<Ability_SO, int>();

    /// <summary>
    /// Puts a specific ability on cooldown. This is called right after the ability is used.
    /// It determines the cooldown duration by rolling the dice specified in the ability's data.
    /// </summary>
    /// <param name="ability">The ability to put on cooldown.</param>
    public void PutOnCooldown(Ability_SO ability)
    {
        int cooldownDuration = 0;

        // Handle the new CooldownDuration type (e.g., "wait 1d4 rounds")
        if (ability.Usage.Type == UsageType.CooldownDuration && !string.IsNullOrEmpty(ability.Usage.CooldownDurationDice))
        {
            // Use the Dice utility to parse the string (e.g., "1d4") and roll it.
            cooldownDuration = Dice.Roll(ability.Usage.CooldownDurationDice);
        }
        // Handle the old CooldownDice type (e.g., "recharge on a 5-6 on a d6")
        else if (ability.Usage.Type == UsageType.CooldownDice && ability.Usage.CooldownDiceSides > 0)
        {
            // This type represents a recharge *chance*, not a duration. We will treat it as a 1-round cooldown
            // to ensure it gets checked by TickDownCooldowns each round.
            cooldownDuration = 1; 
        }
        else
        {
            // If the ability is not a cooldown type, do nothing.
            return;
        }

        // Add or update the ability's entry in the dictionary with its new cooldown duration.
        abilityCooldowns[ability] = cooldownDuration;
        
        GD.Print($"{GetParent().Name} put {ability.AbilityName} on cooldown for {cooldownDuration} rounds.");
    }

    /// <summary>
    /// Checks if a specific ability is currently on cooldown. This is the primary method
    /// that other systems (like AIActionFactory) will call to see if an ability is usable.
    /// </summary>
    /// <param name="ability">The ability to check.</param>
    /// <returns>True if the ability is on cooldown, false otherwise.</returns>
    public bool IsOnCooldown(Ability_SO ability)
    {
        return abilityCooldowns.ContainsKey(ability) && abilityCooldowns[ability] > 0;
    }

    /// <summary>
    /// Ticks down all active cooldowns by one round. This method should be called by a TurnManager
    /// or ActionManager at the start of the creature's turn.
    /// </summary>
    public void TickDownCooldowns()
    {
        if (abilityCooldowns.Count == 0) return;

        List<Ability_SO> keys = new List<Ability_SO>(abilityCooldowns.Keys);

        foreach (var abilityKey in keys)
        {
            // Handle CooldownDuration (our new breath weapon type)
            if (abilityKey.Usage.Type == UsageType.CooldownDuration)
            {
                abilityCooldowns[abilityKey]--;
                if (abilityCooldowns[abilityKey] <= 0)
                {
                    abilityCooldowns.Remove(abilityKey);
                    GD.Print($"{abilityKey.AbilityName} is now off cooldown for {GetParent().Name}.");
                }
            }
            // Handle CooldownDice (the old recharge chance type)
            else if (abilityKey.Usage.Type == UsageType.CooldownDice)
            {
                // Rule: For a recharge mechanic (like a 3.5e dragon), you roll each round to see if it becomes available.
                // We'll assume a 5+ on a d6 is the recharge condition, as is common.
                int rechargeRoll = Dice.Roll(1, abilityKey.Usage.CooldownDiceSides);
                if (rechargeRoll >= 5) 
                {
                    abilityCooldowns.Remove(abilityKey);
                    GD.Print($"{abilityKey.AbilityName} has recharged (Rolled {rechargeRoll}) for {GetParent().Name}.");
                }
                else
                {
                    GD.Print($"{abilityKey.AbilityName} failed to recharge (Rolled {rechargeRoll}).");
                }
            }
        }
    }
}