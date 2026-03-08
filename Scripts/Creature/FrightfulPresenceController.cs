using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: FrightfulPresenceController.cs
// PURPOSE: Automatically triggers the Frightful Presence ability either passively as an Aura,
//          or dynamically whenever the creature makes an offensive action (Attack/Charge).
// ATTACH TO: Creature Root Node (for creatures that possess Frightful Presence).
// =================================================================================================
public partial class FrightfulPresenceController : Godot.Node
{
    [Export]
    [Tooltip("The Ability_SO containing the Effect_FrightfulPresence component.")]
    public Ability_SO FrightfulPresenceAbility;

    [Export]
    [Tooltip("If true, the ability radiates continuously and affects enemies when their turn starts.")]
    public bool IsAura = false;

    [Export]
    [Tooltip("If true, the ability triggers automatically (once per round) when this creature attacks or charges.")]
    public bool TriggersOnAttack = true;

    private CreatureStats myStats;
    private int lastTriggerRound = -1;

    public override void _Ready()
    {
        myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");

        // Join group so TurnManager can find auras efficiently
        AddToGroup("FrightfulPresenceControllers");

        // Subscribe to global combat events for the "On Attack" trigger
        CombatMemory.OnOffensiveActionRecorded += HandleOffensiveAction;
    }

    public override void _ExitTree()
    {
        CombatMemory.OnOffensiveActionRecorded -= HandleOffensiveAction;
    }

    /// <summary>
    /// Called by TurnManager when ANY creature begins its turn. (Aura Mode)
    /// </summary>
    public void CheckExposure(CreatureStats victim)
    {
        if (!IsAura || myStats == null || myStats.CurrentHP <= 0 || victim == myStats) return;
        if (FrightfulPresenceAbility == null) return;

        float radius = FrightfulPresenceAbility.AreaOfEffect.Range;
        if (myStats.GlobalPosition.DistanceTo(victim.GlobalPosition) > radius) return;

        // Frightful Presence requires witnessing the creature (Line of Sight)
        if (!LineOfSightManager.GetVisibility(victim, myStats).HasLineOfSight) return;

        // Trigger the effect directly against this specific victim to prevent full-AoE spam
        var context = new EffectContext { Caster = myStats, PrimaryTarget = victim, AllTargetsInAoE = new Godot.Collections.Array<CreatureStats> { victim } };
        var saveResults = new Dictionary<CreatureStats, bool>();
        
        int dc = FrightfulPresenceAbility.SavingThrow.IsSpecialAbilityDC ? 
            10 + Mathf.FloorToInt(CreatureRulesUtility.GetHitDiceCount(myStats) / 2f) + myStats.ChaModifier : 
            FrightfulPresenceAbility.SavingThrow.BaseDC;

        saveResults[victim] = (Dice.Roll(1, 20) + victim.GetWillSave(myStats)) >= dc;

        foreach (var component in FrightfulPresenceAbility.EffectComponents)
        {
            component.ExecuteEffect(context, FrightfulPresenceAbility, saveResults);
        }
    }

    /// <summary>
    /// Called by CombatMemory whenever ANY creature attacks or casts an offensive spell.
    /// </summary>
    private void HandleOffensiveAction(CreatureStats actor)
    {
        if (!TriggersOnAttack || myStats == null || actor != myStats || myStats.CurrentHP <= 0) return;
        if (FrightfulPresenceAbility == null) return;

        int currentRound = TurnManager.Instance?.GetCurrentRound() ?? 0;
        if (currentRound <= lastTriggerRound) return; // Only trigger once per round

        lastTriggerRound = currentRound;
        GD.PrintRich($"[color=purple]{myStats.Name} acts aggressively, unleashing their Frightful Presence![/color]");

        // Trigger the effect as a burst centered on the creature
        _ = CombatManager.ResolveAbility(myStats, null, null, myStats.GlobalPosition, FrightfulPresenceAbility, false);
    }
}