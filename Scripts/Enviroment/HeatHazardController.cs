using Godot;
using System.Linq;
// =================================================================================================
// FILE: HeatHazardController.cs (GODOT VERSION)
// PURPOSE: Manages the effects of a hot environment on an individual creature.
// ATTACH TO: Creatures at runtime (Child Node).
// =================================================================================================

public partial class HeatHazardController : Godot.Node
{
private CreatureStats myStats;
private StatusEffect_SO fatiguedFromHeatEffect;

private HeatSeverity severity;
private int checksMade = 0;
private int roundsUntilNextCheck = 0;

public void Initialize(HeatSeverity severity)
{
    this.severity = severity;
    myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");

    fatiguedFromHeatEffect = new StatusEffect_SO();
    fatiguedFromHeatEffect.EffectName = "Fatigued from Heatstroke";
    fatiguedFromHeatEffect.ConditionApplied = Condition.Fatigued;
    fatiguedFromHeatEffect.DurationInRounds = 0; 

    SetCheckFrequency();
}

private void SetCheckFrequency()
{
    // Game time conversion
    switch (severity)
    {
        case HeatSeverity.Hot:      roundsUntilNextCheck = 20; break;
        case HeatSeverity.Severe:   roundsUntilNextCheck = 10; break;
        case HeatSeverity.Extreme:  roundsUntilNextCheck = 5;  break;
    }
}

public void OnTurnStart_HeatCheck()
{
    // Fire immunity/resistance does NOT protect from environmental heat.

    // Data-driven extension point:
    // Any status effect can now toggle environmental heat protection directly from the inspector.
    // This keeps protection logic reusable for many spells, items, and passives.
    if (myStats.MyEffects != null && myStats.MyEffects.HasEnvironmentalHeatProtection())
    {
        return;
    }

    roundsUntilNextCheck--;
    if (roundsUntilNextCheck <= 0)
    {
        PerformHeatCheck();
        SetCheckFrequency();
    }
}

private void PerformHeatCheck()
{
    GD.Print($"{myStats.Name} is exposed to {severity} heat and must make a Fortitude save.");
    int dc = 15 + checksMade;
    int fortSave = Dice.Roll(1, 20) + myStats.GetFortitudeSave(null);
    checksMade++;

    // Rule: Penalty for heavy clothing or any armor.
    if (myStats.MyInventory?.GetEquippedItem(EquipmentSlot.Armor) != null)
    {
        fortSave -= 4;
    }

    if (fortSave < dc)
    {
        int nonlethalDamage = Dice.Roll(1, 4);
        GD.PrintRich($"[color=orange]{myStats.Name} fails Fortitude save vs heat (Roll: {fortSave} vs DC: {dc}) and takes {nonlethalDamage} nonlethal damage.[/color]");

        if (!myStats.MyEffects.HasEffect("Fatigued from Heatstroke"))
        {
            myStats.MyEffects.AddEffect(fatiguedFromHeatEffect, null);
        }
        myStats.TakeNonlethalDamage(nonlethalDamage);
    }
    else
    {
        GD.PrintRich($"[color=green]{myStats.Name} succeeds on Fortitude save vs heat (Roll: {fortSave} vs DC: {dc}).[/color]");
    }

    // Rule: Extreme heat also deals lethal fire damage from breathing hot air.
    if (severity == HeatSeverity.Extreme)
    {
        // This is fire damage, so immunity applies.
        if(myStats.Template.Immunities == null || !myStats.Template.Immunities.Contains("Fire"))
        {
            GD.Print($"{myStats.Name} takes 1d6 fire damage from breathing superheated air.");
            myStats.TakeDamage(Dice.Roll(1, 6), "Fire");
        }
    }
}
}
