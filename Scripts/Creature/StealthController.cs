using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: StealthController.cs (GODOT VERSION)
// PURPOSE: Manages a creature's in-combat stealth state.
// ATTACH TO: All creature prefabs (Child Node).
// =================================================================================================
public partial class StealthController : Node
{
private CreatureStats myStats;
private bool isActivelyHiding;
private bool isRemainingStill;

// A dictionary to store the result of this creature's last stealth check against each observer.
// Key: Observer, Value: The Stealth roll result they need to beat with Perception.
public Dictionary<CreatureStats, int> StealthResultAgainstObserver { get; private set; } = new Dictionary<CreatureStats, int>();
public bool IsActivelyHiding => isActivelyHiding;
public bool IsRemainingStill => isActivelyHiding && isRemainingStill;

public override void _Ready()
{
    myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
}

/// <summary>
/// Called at the start of combat to clear any old state.
/// Should be called by CombatStateController or similar during initialization.
/// </summary>
public void OnCombatStart()
{
    isActivelyHiding = false;
    isRemainingStill = false;
    StealthResultAgainstObserver.Clear();
}

/// <summary>
/// Performs a Stealth check and updates the results for all observers who can currently see the creature.
/// </summary>
/// <param name="penalty">Any situational penalty to the check (e.g., for moving fast or sniping).</param>
public void PerformStealthCheck(int penalty = 0)
{
    // Must have cover or concealment to hide.
    bool hasCoverOrConcealment = false;
    var allCombatants = TurnManager.Instance.GetAllCombatants();
    
    foreach (var observer in allCombatants)
    {
        if (observer == myStats) continue;
        var visibility = LineOfSightManager.GetVisibility(observer, myStats);
        if (visibility.CoverBonusToAC > 0 || visibility.ConcealmentMissChance > 0)
        {
            hasCoverOrConcealment = true;
            break;
        }
    }
    
    // This check might be too strict if iterating all. 
    // Pathfinder logic: You hide against specific observers. 
    // If Observer A has LoS but no cover, you can't hide from A. 
    // If Observer B has cover, you can hide from B.
    // However, the original script does a global check. Sticking to fidelity.
    if (!hasCoverOrConcealment)
    {
        GD.Print($"{myStats.Name} cannot use Stealth without cover or concealment.");
        return;
    }

    int stealthRoll = Dice.Roll(1, 20) + myStats.GetStealthBonus(isMoving: true) + penalty;
    GD.PrintRich($"[color=gray]{myStats.Name} attempts to hide (Stealth Roll: {stealthRoll}).[/color]");

    isActivelyHiding = true;
    isRemainingStill = true;

    // Update the required perception DC for all observers.
    foreach (var observer in allCombatants)
    {
        if (observer == myStats) continue;
        StealthResultAgainstObserver[observer] = stealthRoll;
    }
}

/// <summary>
/// Called when an attack is made, breaking stealth.
/// </summary>
public void BreakStealth()
{
    isActivelyHiding = false;
    isRemainingStill = false;
    StealthResultAgainstObserver.Clear();
    GD.Print($"{myStats.Name}'s stealth is broken by attacking.");
}

/// <summary>
/// Marks that the creature has started moving while hidden (sneaking). They are no longer perfectly still.
/// </summary>
public void OnMovementWhileHidden()
{
    if (!isActivelyHiding) return;
    isRemainingStill = false;
}

/// <summary>
/// Called for loud or revealing actions like verbal spellcasting.
/// </summary>
public void BreakStealthFromNoise(string reason)
{
    if (!isActivelyHiding) return;
    isActivelyHiding = false;
    isRemainingStill = false;
    StealthResultAgainstObserver.Clear();
    GD.Print($"{myStats.Name}'s stealth is broken: {reason}.");
}
}
// Depende