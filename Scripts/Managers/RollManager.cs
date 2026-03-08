using Godot;
using System.Linq;

// =================================================================================================
// FILE: RollManager.cs (GODOT VERSION)
// PURPOSE: A singleton manager to handle all d20 rolls that can be affected by special abilities.
// ATTACH TO: A persistent "GameManager" Node.
// =================================================================================================

public partial class RollManager : Godot.Node
{
    public static RollManager Instance { get; private set; }

    public override void _Ready()
    {
        if (Instance != null && Instance != this)
        {
            QueueFree();
        }
        else
        {
            Instance = this;
        }
    }

    /// <summary>
    /// The primary method for making an attack roll or saving throw.
    /// It checks the rolling creature for any one-time effects that might modify the roll.
    /// </summary>
    /// <param name="roller">The creature making the roll.</param>
    /// <returns>The final d20 roll result.</returns>
    public int MakeD20Roll(CreatureStats roller)
    {
          // --- 1. Base Roll ---
        int roll1 = Dice.Roll(1, 20);
        int finalRoll = roll1;
        string logMessage = "";
        bool consumedForcedLowerRoll = false;

        if (roller != null)
        {
            var oneTimeController = roller.GetNodeOrNull<OneTimeEffectController>("OneTimeEffectController");
            var statusEffectController = roller.GetNodeOrNull<StatusEffectController>("StatusEffectController");
            
            // --- 2. Check for forced-lower-roll penalties first ---
            // Reason for priority:
            // When a creature is compelled by a hostile mythic effect (for example, mythic bane),
            // that pressure should apply before optional self-beneficial effects are considered.
            if (oneTimeController != null && oneTimeController.HasForcedLowerRollCharge())
            {
                string sourceName = oneTimeController.ConsumeForcedLowerRollCharge();
                if (!string.IsNullOrEmpty(sourceName))
                {
                    int roll2 = Dice.Roll(1, 20);
                    finalRoll = Mathf.Min(roll1, roll2);
                    consumedForcedLowerRoll = true;
                    logMessage += $"[color=orange]Forced by {sourceName} to roll twice ({roll1}, {roll2}), taking {finalRoll}.[/color] ";
                }
            }

            // --- 3. Check for Mythic "Roll Twice" Effect ---
            if (!consumedForcedLowerRoll && oneTimeController != null && oneTimeController.HasRollTwiceCharge())
            {
                if (ShouldUseRollTwice(roller, roll1))
                {
                    if (oneTimeController.ConsumeRollTwiceCharge())
                    {
                        int roll2 = Dice.Roll(1, 20);
                        finalRoll = Mathf.Max(roll1, roll2);
                        logMessage += $"[color=purple]Used Mythic effect to roll twice ({roll1}, {roll2}), taking {finalRoll}.[/color] ";
                    }
                }
            }
             // --- 3b. Check for Disadvantage Offers (Wreath of Fate) ---
            if (oneTimeController != null && oneTimeController.HasDisadvantageOffer())
            {
                // UI Hook / AI Logic needed here.
                // For Sim: AI always takes the offer if Staggered is bad for them (usually is).
                // Player: Auto-accept for now or hook UI.
                
                string effectToRemove = oneTimeController.ConsumeDisadvantageOffer();
                if (!string.IsNullOrEmpty(effectToRemove))
                {
                    int roll2 = Dice.Roll(1, 20);
                    finalRoll = Mathf.Min(roll1, roll2); // Take Worse
                    
                    logMessage += $"[color=orange]Accepted Disadvantage ({roll1}, {roll2}) -> {finalRoll} to remove {effectToRemove}.[/color] ";
                    
                    statusEffectController?.RemoveEffect(effectToRemove);
                }
            }
            
            // --- 4. Check for Dischargeable Bonus (like Guidance) ---
            // This happens AFTER the roll is determined, as the bonus is added to the roll, not a re-roll.
            if (statusEffectController != null)
            {
                // Peek to see if a bonus is available without consuming it yet.
                var availableBonusEffect = statusEffectController.ActiveEffects.FirstOrDefault(e => !e.IsSuppressed && e.EffectData.IsDischargeable && e.RemainingCharges > 0);
                if (availableBonusEffect != null)
                {
                    if(ShouldUseDischargeBonus(roller, finalRoll))
                    {
                        var consumedBonus = statusEffectController.ConsumeDischargeableBonus();
                        if (consumedBonus != null)
                        {
                           // We don't add the bonus to the roll here. That's the combat manager's job (calculating totals).
                           // This system only determines the raw d20 result. The log indicates the bonus was consumed.
                           logMessage += $"[color=green]Used '{consumedBonus.BonusType}' bonus from {availableBonusEffect.EffectData.EffectName}.[/color]";
                        }
                    }
                }
            }
        }
        
        if (!string.IsNullOrEmpty(logMessage)) GD.PrintRich(logMessage);
        
        return finalRoll;
    }

    /// <summary>
    /// A helper method to encapsulate the decision logic for using a "roll twice" effect.
    /// </summary>
    private bool ShouldUseRollTwice(CreatureStats roller, int firstRoll)
    {
        if (roller.IsInGroup("Player"))
        {
            // In a real game, this would pop up a UI dialogue box asking "Use Mythic Power (Roll Twice)?"
            // and would return the player's choice. For now, we'll auto-accept if the roll is bad.
            GD.Print($"Player {roller.Name} has a roll-twice charge. (UI Prompt would appear here)");
            return firstRoll <= 10; 
        }
        else // It's an AI
        {
            // AI logic: use it if the first roll is 10 or less.
            return firstRoll <= 10;
        }
    }

    /// <summary>
    /// A helper method to encapsulate the decision logic for using a dischargeable bonus like Guidance.
    /// </summary>
    private bool ShouldUseDischargeBonus(CreatureStats roller, int currentRoll)
    {
         if (roller.IsInGroup("Player"))
        {
            // In a real game, a UI would prompt "Use Guidance (+1)?"
            GD.Print($"Player {roller.Name} has a dischargeable bonus available. (UI Prompt would appear here)");
            return true; // Auto-accept for now
        }
        else // It's an AI
        {
            // Simple AI logic: Always take a free bonus. A smarter AI might save it for a more important roll.
            return true;
        }
    }
}
