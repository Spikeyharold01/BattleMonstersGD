using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: OneTimeEffectController.cs (GODOT VERSION)
// PURPOSE: Tracks charges of single-use special abilities, like Mythic Bless's roll-twice effect.
// ATTACH TO: All creature prefabs (Child Node).
// =================================================================================================
public partial class OneTimeEffectController : Godot.Node
{
private Dictionary<string, int> effectCharges = new Dictionary<string, int>();

// Key: Effect source name. Value: Number of times the creature must roll twice and keep the lower result.
// This is intentionally generic so any current or future ability can reuse the same mechanic.
private Dictionary<string, int> forcedLowerRollCharges = new Dictionary<string, int>();

// Key: Status Effect Name to remove. Value: Number of times they must accept disadvantage (usually 1).
    private Dictionary<string, int> disadvantageOffers = new Dictionary<string, int>();

    public void AddDisadvantageOffer(string effectName)
    {
        if (!disadvantageOffers.ContainsKey(effectName))
        {
            disadvantageOffers.Add(effectName, 1);
            GD.Print($"{GetParent().Name} can choose to roll with disadvantage to remove {effectName}.");
        }
    }

    public bool HasDisadvantageOffer() => disadvantageOffers.Count > 0;

    /// <summary>
    /// Checks if the creature wants to use a disadvantage offer for this roll.
    /// Returns the name of the effect to remove if yes, null if no.
    /// </summary>
    public string ConsumeDisadvantageOffer()
    {
        // Simple Logic: First available offer.
        // Complex Logic: UI selection.
        if (disadvantageOffers.Count > 0)
        {
            var key = disadvantageOffers.Keys.First();
            disadvantageOffers.Remove(key);
            return key;
        }
        return null;
    }

public void AddRollTwiceCharge(string sourceEffectName)
{
    if (effectCharges.ContainsKey(sourceEffectName))
    {
        effectCharges[sourceEffectName]++;
    }
    else
    {
        effectCharges.Add(sourceEffectName, 1);
    }
    GD.Print($"{GetParent().Name} gained a 'Roll Twice' charge from {sourceEffectName}.");
}

public bool ConsumeRollTwiceCharge()
{
    // Consuming first available
    // Create copy of keys to modify dict during iteration if needed (though here we only modify value or remove)
    foreach (var key in new List<string>(effectCharges.Keys))
    {
        if (effectCharges[key] > 0)
        {
            effectCharges[key]--;
            GD.Print($"{GetParent().Name} consumed a 'Roll Twice' charge from {key}.");
            if (effectCharges[key] <= 0)
            {
                effectCharges.Remove(key);
            }
            return true;
        }
    }
    return false;
}

public bool HasRollTwiceCharge()
{
    foreach (var chargeCount in effectCharges.Values)
    {
        if (chargeCount > 0) return true;
    }
    return false;
}

/// <summary>
/// Adds a "forced lower roll" charge.
/// Narrative intent:
/// - The creature suffers a moment of magical hesitation or uncertainty.
/// - The next attack roll or saving throw is made twice, and the lower result stands.
/// </summary>
/// <param name="sourceEffectName">A readable label for the source of the penalty.</param>
public void AddForcedLowerRollCharge(string sourceEffectName)
{
    if (string.IsNullOrWhiteSpace(sourceEffectName))
    {
        sourceEffectName = "Unknown Source";
    }

    if (forcedLowerRollCharges.ContainsKey(sourceEffectName))
    {
        forcedLowerRollCharges[sourceEffectName]++;
    }
    else
    {
        forcedLowerRollCharges.Add(sourceEffectName, 1);
    }

    GD.Print($"{GetParent().Name} gained a 'roll twice, take lower' charge from {sourceEffectName}.");
}

/// <summary>
/// Returns true when there is at least one pending forced-lower-roll penalty.
/// </summary>
public bool HasForcedLowerRollCharge()
{
    foreach (var chargeCount in forcedLowerRollCharges.Values)
    {
        if (chargeCount > 0) return true;
    }
    return false;
}

/// <summary>
/// Consumes one forced-lower-roll charge and returns the source label.
/// Output expectations:
/// - Returns a source name when a charge is consumed.
/// - Returns null when there was no charge to consume.
/// </summary>
public string ConsumeForcedLowerRollCharge()
{
    foreach (var key in new List<string>(forcedLowerRollCharges.Keys))
    {
        if (forcedLowerRollCharges[key] > 0)
        {
            forcedLowerRollCharges[key]--;
            if (forcedLowerRollCharges[key] <= 0)
            {
                forcedLowerRollCharges.Remove(key);
            }

            GD.Print($"{GetParent().Name} consumed a 'roll twice, take lower' charge from {key}.");
            return key;
        }
    }

    return null;
}
}
