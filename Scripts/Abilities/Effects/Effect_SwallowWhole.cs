using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_SwallowWhole.cs
// PURPOSE: Executes a Swallow Whole attempt. Requires the target to be grappled first.
// =================================================================================================
[GlobalClass]
public partial class Effect_SwallowWhole : AbilityEffectComponent
{
    [Export]
    [Tooltip("Target must be this many size categories smaller than the attacker (Default: 1).")]
    public int SizeDifferenceRequired = 1;

    [Export]
    [Tooltip("Damage dealt each round the creature is swallowed.")]
    public DamageInfo StomachDamage;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        var attacker = context.Caster;
        var target = context.PrimaryTarget;

        if (attacker == null || target == null) return;

        // 1. Validation: Must be grappling the target
        bool isGrapplingTarget = attacker.CurrentGrappleState != null && 
                                 attacker.CurrentGrappleState.Controller == attacker && 
                                 attacker.CurrentGrappleState.Target == target;

        if (!isGrapplingTarget)
        {
            GD.Print($"Swallow Whole failed: {attacker.Name} is not grappling {target.Name}.");
            return;
        }

        // 2. Validation: Must not have a ruptured stomach
        if (attacker.MyEffects.HasEffect("Stomach Ruptured"))
        {
            GD.Print($"Swallow Whole failed: {attacker.Name}'s stomach is ruptured and healing!");
            return;
        }

        // 3. Validation: Size check
        if ((int)target.Template.Size > (int)attacker.Template.Size - SizeDifferenceRequired)
        {
            GD.Print($"Swallow Whole failed: {target.Name} is too large to be swallowed.");
            return;
        }

        // 4. Resolve Combat Maneuver (as if pinning)
        int cmb = attacker.GetCMB(ManeuverType.Grapple, true); // +5 bonus for maintaining
        if (attacker.CurrentGrappleState.IsHoldingWithBodyPartOnly) cmb -= 20;

        int maneuverRoll = Dice.Roll(1, 20) + cmb;
        int targetCMD = target.GetCMD(ManeuverType.Grapple);

        GD.Print($"{attacker.Name} attempts to Swallow {target.Name}. CMB Roll: {maneuverRoll} vs CMD {targetCMD}.");

        if (maneuverRoll >= targetCMD)
        {
            GD.PrintRich($"[color=red]Success! {target.Name} is Swallowed Whole![/color]");

            // Apply immediate bite damage (rule: "the opponent takes bite damage")
            var bite = attacker.Template.MeleeAttacks.Find(a => a.AttackName.ToLower().Contains("bite"));
            if (bite != null && bite.DamageInfo.Count > 0)
            {
                int dmg = Dice.Roll(bite.DamageInfo[0].DiceCount, bite.DamageInfo[0].DieSides) + bite.DamageInfo[0].FlatBonus + attacker.StrModifier;
                target.TakeDamage(dmg, bite.DamageInfo[0].DamageType, attacker, null, bite);
            }

            // Apply Swallowed State
            var swallowedEffect = new StatusEffect_SO
            {
                EffectName = "Swallowed Whole",
                ConditionApplied = Condition.Swallowed,
                DurationInRounds = 0 // Permanent until escape
            };
            target.MyEffects.AddEffect(swallowedEffect, attacker);

            var ctrl = new SwallowedController();
            ctrl.Name = "SwallowedController";
            target.AddChild(ctrl);
            ctrl.Initialize(target, attacker, StomachDamage);

            // Clean up the main grapple state. Victim keeps Grappled via SwallowedController logic
            // but the Swallower is free to act.
            CombatManeuvers.BreakGrapple(attacker);
            target.GlobalPosition = attacker.GlobalPosition; // Move inside
        }
        else
        {
            GD.PrintRich($"[color=orange]Failed to swallow {target.Name}.[/color]");
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        var attacker = context.Caster;
        var target = context.PrimaryTarget;
        if (attacker == null || target == null) return 0f;

        if (attacker.MyEffects.HasEffect("Stomach Ruptured")) return 0f;

        bool isGrapplingTarget = attacker.CurrentGrappleState != null && 
                                 attacker.CurrentGrappleState.Controller == attacker && 
                                 attacker.CurrentGrappleState.Target == target;

        if (!isGrapplingTarget) return 0f;

        if ((int)target.Template.Size > (int)attacker.Template.Size - SizeDifferenceRequired) return 0f;

        // Highly valuable if possible
        return 400f;
    }
}