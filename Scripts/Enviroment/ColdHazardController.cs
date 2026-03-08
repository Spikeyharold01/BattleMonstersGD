using Godot;
using System.Linq;
// =================================================================================================
// FILE: ColdHazardController.cs (GODOT VERSION)
// PURPOSE: Manages the effects of a cold environment on an individual creature.
// ATTACH TO: Creatures at runtime (Child Node).
// =================================================================================================
public enum ColdSeverity { Cold, Severe, Extreme }
public partial class ColdHazardController : GridNode
{
private CreatureStats myStats;
private StatusEffect_SO fatiguedFromColdEffect;

private ColdSeverity severity;
private int checksMade = 0;
private int roundsUntilNextCheck = 0;
private int durationRounds = -1; 

public void Initialize(ColdSeverity severity, int duration = -1)
{
    this.severity = severity;
    this.durationRounds = duration;
    myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");

    // Create a unique instance of the Fatigued effect for this purpose
    fatiguedFromColdEffect = new StatusEffect_SO();
    fatiguedFromColdEffect.EffectName = "Fatigued from Cold";
    fatiguedFromColdEffect.ConditionApplied = Condition.Fatigued;
    fatiguedFromColdEffect.DurationInRounds = 0; 

    SetCheckFrequency();
}

private void SetCheckFrequency()
{
    switch (severity)
    {
        case ColdSeverity.Cold:     roundsUntilNextCheck = 10; break;
        case ColdSeverity.Severe:   roundsUntilNextCheck = 5;  break;
        case ColdSeverity.Extreme:  roundsUntilNextCheck = 1;  break;
    }
}

// Called manually via ActionManager/TurnManager or Signals.
// Assuming ActionManager iterates or calls this if it exists.
// Or relying on HazardFromStatusController to manage environmental ticks.
// The Unity version had `OnTurnStart_ColdCheck`. We'll expose it and assume it's called.
public void OnTurnStart_ColdCheck()
{
    // Rule: Cold and exposure do not affect creatures with immunity to cold.
    if (myStats.Template.Immunities != null && myStats.Template.Immunities.Any(i => i.Equals("Cold", System.StringComparison.OrdinalIgnoreCase)))
    {
        QueueFree(); 
        return;
    }

    // Data-driven extension point:
    // Any status effect can now toggle environmental cold protection directly from the inspector.
    // This allows spells like Endure Elements to function without needing a dedicated per-spell script.
    if (myStats.MyEffects != null && myStats.MyEffects.HasEnvironmentalColdProtection())
    {
        return;
    }

    if (durationRounds > 0)
    {
        durationRounds--;
        if (durationRounds <= 0)
        {
            GD.Print($"Lingering cold effect on {myStats.Name} has ended.");
            myStats.MyEffects.RemoveEffect("Fatigued from Cold");
            QueueFree();
            return;
        }
    }

    roundsUntilNextCheck--;
    if (roundsUntilNextCheck <= 0)
    {
        PerformColdCheck();
        SetCheckFrequency(); 
    }
}

private void PerformColdCheck()
{
    GD.Print($"{myStats.Name} is exposed to {severity} cold and must make a Fortitude save.");
    int dc = 15 + checksMade;
    
    if (severity == ColdSeverity.Extreme)
    {
        myStats.TakeDamage(Dice.Roll(1, 6), "Cold");
        if (myStats.CurrentHP <= 0) return;
    }

    int fortSave = Dice.Roll(1, 20) + myStats.GetFortitudeSave(null);
    checksMade++;

    if (fortSave < dc)
    {
        int nonlethalDamage = (severity == ColdSeverity.Extreme) ? Dice.Roll(1, 4) : Dice.Roll(1, 6);
        GD.PrintRich($"[color=cyan]{myStats.Name} fails Fortitude save vs cold (Roll: {fortSave} vs DC: {dc}) and takes {nonlethalDamage} nonlethal damage.[/color]");
        
        if (!myStats.MyEffects.HasEffect("Fatigued from Cold"))
        {
            myStats.MyEffects.AddEffect(fatiguedFromColdEffect, null);
        }
        
        myStats.TakeNonlethalDamage(nonlethalDamage);
    }
    else
    {
        GD.PrintRich($"[color=green]{myStats.Name} succeeds on Fortitude save vs cold (Roll: {fortSave} vs DC: {dc}).[/color]");
    }
}
}