using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// =================================================================================================
// FILE: AoOManager.cs
// PURPOSE: A global manager to handle all aspects of Attacks of Opportunity (AoO).
// ATTACH TO: A persistent "GameManager" Node.
// =================================================================================================

/// <summary>
/// Defines the types of actions that can provoke an Attack of Opportunity.
/// </summary>
public enum ProvokingActionType
{
    Movement,
    RangedAttack,
    Spellcasting,
    StandUpFromProne,
    UseItem,
    CombatManeuver
}

/// <summary>
/// This singleton class manages the rules for Attacks of Opportunity (AoO).
/// It is the central authority for checking if an action provokes, finding which creatures
/// can make an AoO, and resolving those attacks, interrupting the normal turn flow.
/// </summary>
public partial class AoOManager : Godot.Node
{
    public static AoOManager Instance { get; private set; }

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
    /// The main public method for simple actions that don't need interruption feedback (like movement).
    /// </summary>
     public async Task CheckAndResolve(CreatureStats provoker, ProvokingActionType actionType)
    {
        if (provoker != null && provoker.MyEffects != null && provoker.MyEffects.HasCondition(Condition.WhirlwindForm))
        {
            if (actionType == ProvokingActionType.Movement) return; // Whirlwind form does not provoke on move
        }

        // This overload calls the more complex version with no ability and no callback.
        await CheckAndResolve(provoker, actionType, null, null);
    }
    /// <summary>
    /// The advanced public method for complex actions (like spellcasting) that need to know if they were interrupted.
    /// </summary>
    /// <param name="provoker">The creature performing the action.</param>
    /// <param name="actionType">The type of action being performed.</param>
    /// <param name="provokingAbility">The spell or ability being used, for concentration checks.</param>
    /// <param name="onActionResolved">A callback that is invoked with 'true' if the action succeeds, 'false' if it was cancelled by the AoO.</param>
    public async Task CheckAndResolve(CreatureStats provoker, ProvokingActionType actionType, Ability_SO provokingAbility, Action<bool> onActionResolved)
    {
        List<CreatureStats> threateners = GetValidThreateners(provoker);
        await ResolveAoOs(provoker, actionType, threateners, provokingAbility, onActionResolved);
    }

    /// <summary>
    /// A specialized method to handle the unique AoO rules for the Withdraw action.
    /// It only resolves AoOs from enemies that the withdrawing creature cannot see.
    /// </summary>
    public async Task CheckAndResolveForWithdraw(CreatureStats provoker)
    {
        // Rule: When you withdraw, the square you start in is not threatened by any opponent you can see.
        List<CreatureStats> allThreateners = GetValidThreateners(provoker);

        // Filter this list to include ONLY threateners the provoker cannot see (e.g., invisible creatures).
        List<CreatureStats> invisibleThreateners = allThreateners
            .Where(t => !LineOfSightManager.GetVisibility(provoker, t).HasLineOfSight)
            .ToList();
            
        // The provoking action type is still movement, but only the invisible threateners get to act.
        await ResolveAoOs(provoker, ProvokingActionType.Movement, invisibleThreateners, null, null);
    }

    /// <summary>
    /// The core private async task that resolves a list of AoOs and handles spell interruption.
    /// </summary>
    private async Task ResolveAoOs(CreatureStats provoker, ProvokingActionType actionType, List<CreatureStats> threateners, Ability_SO provokingAbility, Action<bool> onActionResolved)
    {
        if (threateners.Any())
        {
            GD.PrintRich($"[color=red]{provoker.Name}'s {actionType} action provokes {threateners.Count} Attack(s) of Opportunity![/color]");

            TurnManager.Instance.SetInterruptState(true);

            bool actionWasCancelled = false;

            foreach (var threatener in threateners)
            {
                if (!GodotObject.IsInstanceValid(provoker) || provoker.CurrentHP <= 0) 
                {
                    actionWasCancelled = true;
                    break;
                }

                GD.PrintRich($"[color=red]INTERRUPT:[/color] {threatener.Name} takes an AoO against {provoker.Name}.");

                bool usedStandStill = false;
                // Check for Stand Still feat and if the AoO was provoked by movement.
                if (threatener.HasFeat("Stand Still") && actionType == ProvokingActionType.Movement)
                {
                    // AI/Player Choice would go here. For now, AI will always prefer it if the target is trying to get away.
                    var aiController = threatener.GetNodeOrNull<AIController>("AIController");
                    if (aiController != null && aiController.GetPerceivedHighestThreat() == provoker)
                    {
                        // Resolve the combat maneuver check.
                        int cmbCheck = Dice.Roll(1, 20) + threatener.GetCMB();
                        int targetCMD = provoker.GetCMD();
                        GD.Print($"{threatener.Name} uses Stand Still! CMB Check: {cmbCheck} vs CMD {targetCMD}.");

                        if (cmbCheck >= targetCMD)
                        {
                            GD.PrintRich($"[color=green]Success! {provoker.Name}'s movement is halted for the rest of their turn.[/color]");
                            var stoppedEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/SE_MovementStopped.tres");
                            if (stoppedEffect != null)
                            {
                                var instance = (StatusEffect_SO)stoppedEffect.Duplicate();
                                provoker.MyEffects.AddEffect(instance, threatener);
                            }
                            usedStandStill = true;
                        }
                        else
                        {
                             GD.PrintRich("[color=orange]Stand Still attempt failed.[/color]");
                        }
                    }
                }

                // If Stand Still wasn't used or failed, make a normal damage attack.
                if (!usedStandStill)
                {
                    int hpBeforeAoO = provoker.CurrentHP;
                    CombatManager.ResolveMeleeAttack(threatener, provoker);
                    
                    if(GodotObject.IsInstanceValid(provoker)) // Check if provoker survived
                    {
                        int damageTaken = hpBeforeAoO - provoker.CurrentHP;
                        
                        // --- Spell Interruption Logic ---
                        if (damageTaken > 0 && actionType == ProvokingActionType.Spellcasting && provokingAbility != null)
                        {
                            int concentrationDC = 10 + damageTaken + provokingAbility.SpellLevel;
                            GD.Print($"{provoker.Name} was damaged while casting and must make a DC {concentrationDC} Concentration check.");
                            
                            if (!CombatManager.CheckConcentration(provoker, concentrationDC))
                            {
                                // Spell failed! The action is cancelled.
                                actionWasCancelled = true;
                                // Break the loop, no more AoOs are needed as the action is already lost.
                                break; 
                            }
                        }
                    }
                }
                threatener.AoOsMadeThisRound++;
                
                await ToSignal(GetTree().CreateTimer(1.2f), "timeout"); // Visual Delay
            }
            
            if (!GodotObject.IsInstanceValid(provoker) || provoker.CurrentHP <= 0)
            {
                actionWasCancelled = true;
                GD.Print($"{provoker?.Name ?? "Target"} was defeated by an AoO, their action is cancelled.");
            }

            // Invoke the callback to notify the original action of the outcome.
            onActionResolved?.Invoke(!actionWasCancelled);

            TurnManager.Instance.SetInterruptState(false);
        }
        else
        {
            // If there were no threateners, the action automatically succeeds.
            onActionResolved?.Invoke(true);
        }
    }

    /// <summary>
    /// Gets a list of creatures that currently threaten the target creature.
    /// </summary>
    public List<CreatureStats> GetThreateningCreatures(CreatureStats target)
    {
        List<CreatureStats> allCombatants = TurnManager.Instance.GetAllCombatants();
        if (allCombatants == null) return new List<CreatureStats>();

        // Faction Logic: Assumes "Player" group vs "Enemy" group logic for hostility
        bool targetIsPlayer = target.IsInGroup("Player");

        return allCombatants.Where(c => 
            c != target &&                                    // Can't threaten self
            (c.IsInGroup("Player") != targetIsPlayer) &&      // Must be an enemy (opposite faction)
            !c.GetNode<StatusEffectController>("StatusEffectController").HasCondition(Condition.Helpless) && // Can't be helpless
            c.GlobalPosition.DistanceTo(target.GlobalPosition) <= c.GetEffectiveReach().Max // Must be within reach
        ).ToList();
    }
    
    /// <summary>
    /// A quick check used by the AI to see if it is currently threatened.
    /// </summary>
    public bool IsThreatened(CreatureStats target)
    {
        return GetThreateningCreatures(target).Any();
    }

    /// <summary>
    /// Finds all creatures that can legally perform an AoO against the provoker.
    /// </summary>
    private List<CreatureStats> GetValidThreateners(CreatureStats provoker)
    {
        List<CreatureStats> validThreateners = new List<CreatureStats>();
        foreach (var potentialThreatener in GetThreateningCreatures(provoker))
        {
            if (potentialThreatener.MyEffects.HasCondition(Condition.Grappled))
            {
                continue;
            }

            if (potentialThreatener.AoOsMadeThisRound < potentialThreatener.GetMaxAoOsPerRound())
            {
                validThreateners.Add(potentialThreatener);
            }
        }
        return validThreateners;
    }
}