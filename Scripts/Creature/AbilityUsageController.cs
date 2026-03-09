using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: AbilityUsageController.cs (GODOT VERSION)
// PURPOSE: Manages the "per day" usage limits for abilities like Channel Energy or domain powers.
// ATTACH TO: All creature prefabs (as a child node of CreatureStats root).
// =================================================================================================
public partial class AbilityUsageController : Node
{
// A dictionary to track the remaining uses for each "per day" ability.
private Dictionary<Ability_SO, int> remainingUses = new Dictionary<Ability_SO, int>();
private CreatureStats myStats;

public override void _Ready()
{
    myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
    // Initialize uses at the start of combat.
    InitializeUses();
}

public void InitializeUses()
{
    remainingUses.Clear();
    if (myStats == null || myStats.Template == null) return;

    // Populate from the template's Channel Energy
    var channelInfo = myStats.Template.ChannelPositiveEnergy;
    if (channelInfo != null && channelInfo.UsesPerDay > 0)
    {
        // Find the placeholder ability SO to use as a key
        // Note: AbilityType enum logic replaced with string name checks or specific component checks in this system usually.
        // Assuming "Channel Positive Energy" is the ability name.
        var channelAbility = myStats.Template.KnownAbilities.Find(a => a.AbilityName.Contains("Channel Positive Energy"));
        
        if (channelAbility != null)
        {
            remainingUses[channelAbility] = channelInfo.UsesPerDay;
        }
    }

    // Populate from other abilities with per-day uses
    if (myStats.Template.KnownAbilities != null)
    {
        foreach (var ability in myStats.Template.KnownAbilities)
        {
            if (ability.Usage.Type == UsageType.PerDay)
            {
                if (ability.Usage.IsDynamicUses)
                {
                    // Calculate uses dynamically: base + stat mod
                    int statMod = 0;
                    switch (ability.Usage.DynamicUsesStat)
                    {
                        case AbilityScore.Charisma: statMod = myStats.ChaModifier; break;
                        case AbilityScore.Wisdom: statMod = myStats.WisModifier; break;
                        case AbilityScore.Intelligence: statMod = myStats.IntModifier; break;
                        case AbilityScore.Constitution: statMod = myStats.ConModifier; break;
                        case AbilityScore.Dexterity: statMod = myStats.DexModifier; break;
                        case AbilityScore.Strength: statMod = myStats.StrModifier; break;
                    }

                    int casterLevelContribution = 0;
                    if (ability.Usage.DynamicUsesCasterLevelDivisor > 0)
                    {
                        casterLevelContribution = Mathf.FloorToInt((float)myStats.Template.CasterLevel / ability.Usage.DynamicUsesCasterLevelDivisor)
                            * Mathf.Max(ability.Usage.DynamicUsesCasterLevelMultiplier, 1);
                    }

                    int dynamicUses = ability.Usage.DynamicUsesBase + statMod + casterLevelContribution;
                    remainingUses[ability] = Mathf.Max(dynamicUses, ability.Usage.MinimumUsesPerDay);
                }
                else
                {
                    // Use the flat value from the SO
                    remainingUses[ability] = ability.Usage.UsesPerDay;
                }
            }
        }
    }
}

/// <summary>
/// Checks if a creature has any uses left for a specific per-day ability.
/// </summary>
public bool HasUsesRemaining(Ability_SO ability)
{
    // If this ability shares uses with another, check the parent ability instead.
    Ability_SO abilityToCheck = ability.SharesUsageWith != null ? ability.SharesUsageWith : ability;

    if (abilityToCheck.Usage.Type != UsageType.PerDay)
    {
        return true; // Not a limited-use ability.
    }

    if (remainingUses.TryGetValue(abilityToCheck, out int uses))
    {
        return uses > 0;
    }
    
    // If it's not in the dictionary, it was never initialized, so it can't be used (if it was marked PerDay).
    // If the Ability_SO says PerDay but logic missed it, assume 0.
    return false;
}

/// <summary>
/// Decrements the use count for an ability.
/// </summary>
public void ConsumeUse(Ability_SO ability)
{
    // If this ability shares uses with another, consume the use from the parent ability.
    Ability_SO abilityToConsume = ability.SharesUsageWith != null ? ability.SharesUsageWith : ability;

    if (abilityToConsume.Usage.Type != UsageType.PerDay) return;

    if (remainingUses.ContainsKey(abilityToConsume) && remainingUses[abilityToConsume] > 0)
    {
        remainingUses[abilityToConsume]--;
        GD.Print($"{myStats.Name} used {ability.AbilityName}, consuming a charge of {abilityToConsume.AbilityName}. ({remainingUses[abilityToConsume]} uses remaining).");
    }
}
}