using Godot;
using System.Text.RegularExpressions;

// =================================================================================================
// FILE: Dice.cs (GODOT VERSION)
// PURPOSE: A simple static utility for dice rolling.
// DO NOT ATTACH: This is a static class.
// =================================================================================================

/// <summary>
/// A simple static utility class for simulating tabletop dice rolls, like 3d6 or 1d20.
/// Because the class is 'static', it cannot be attached to a Node in Godot,
/// and you don't need to create an instance of it. Its methods can be called directly
/// from any other script, for example: int result = Dice.Roll(1, 20);
/// </summary>
public static class Dice
{
    /// <summary>
    /// Rolls a specified number of dice with a specified number of sides and returns the sum.
    /// Simulates standard tabletop dice notation (e.g., 3d6 would be Roll(3, 6)).
    /// </summary>
    /// <param name="numDice">The number of dice to roll (e.g., 3 for 3d6).</param>
    /// <param name="numSides">The number of sides on each die (e.g., 6 for 3d6).</param>
    /// <returns>The total sum of all dice rolled.</returns>
    public static int Roll(int numDice, int numSides)
    {
        // --- Input Validation ---
        if (numDice <= 0 || numSides <= 0) return 0;
        
        int total = 0;
        
        // --- Dice Rolling Loop ---
        for (int i = 0; i < numDice; i++)
        {
            // Godot's GD.RandRange(min, max) is inclusive for integers [min, max].
            total += GD.RandRange(1, numSides);
        }
        
        return total;
    }

    /// <summary>
    /// Rolls based on a string input like "1d4", "2d6+5", or "3d8".
    /// </summary>
    public static int Roll(string diceString)
    {
        if (string.IsNullOrEmpty(diceString)) return 0;

        // Clean string
        diceString = diceString.ToLower().Trim();

        // Regex for XdY(+Z)
        var match = Regex.Match(diceString, @"(\d+)d(\d+)([+-]\d+)?");
        if (match.Success)
        {
            int numDice = int.Parse(match.Groups[1].Value);
            int numSides = int.Parse(match.Groups[2].Value);
            int bonus = 0;
            if (match.Groups[3].Success)
            {
                bonus = int.Parse(match.Groups[3].Value);
            }

            return Roll(numDice, numSides) + bonus;
        }

        // Fallback or error
        GD.PrintErr($"Invalid dice string format: {diceString}");
        return 0;
    }
}