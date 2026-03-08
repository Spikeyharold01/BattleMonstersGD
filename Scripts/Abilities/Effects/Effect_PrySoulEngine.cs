using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_PrySoulEngine.cs
// PURPOSE: Resolves the specialized Grapple check to tear a body off a Soul Engine construct.
// =================================================================================================
[GlobalClass]
public partial class Effect_PrySoulEngine : AbilityEffectComponent
{
    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        CreatureStats attacker = context.Caster;
        CreatureStats construct = context.PrimaryTarget;

        if (attacker == null || construct == null) return;

        var soulEngine = construct.GetNodeOrNull<SoulEngineController>("SoulEngineController");
        if (soulEngine == null || !soulEngine.IsBodyAttached)
        {
            GD.Print("Target does not have an active Soul Engine to pry.");
            return;
        }

        // Rule: The construct gains a +10 bonus to its CMD against this specific attempt.
        int cmb = attacker.GetCMB(ManeuverType.Grapple);
        int maneuverRoll = Dice.Roll(1, 20) + cmb;
        int constructCMD = construct.GetCMD(ManeuverType.Grapple) + 10;

        GD.Print($"{attacker.Name} attempts to tear the body off {construct.Name}'s Soul Engine! Rolls {maneuverRoll} vs CMD {constructCMD}.");

        if (maneuverRoll >= constructCMD)
        {
            GD.PrintRich($"[color=green]Success![/color] The body is ripped free.");
            soulEngine.RemoveBody(attacker);
        }
        else
        {
            GD.PrintRich($"[color=red]Failure.[/color] The construct keeps its victim firmly attached.");
            // Optional: Provoke AoO for failing a reckless maneuver? (Left out to stay strictly RAW, but plausible).
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (context.PrimaryTarget == null) return 0f;
        var soulEngine = context.PrimaryTarget.GetNodeOrNull<SoulEngineController>("SoulEngineController");
        if (soulEngine == null || !soulEngine.IsBodyAttached) return 0f;

        // Highly valuable action because it guarantees a kill within 1d4 rounds.
        float score = 500f + (context.PrimaryTarget.Template.ChallengeRating * 50f);

        int cmb = context.Caster.GetCMB(ManeuverType.Grapple);
        int cmd = context.PrimaryTarget.GetCMD(ManeuverType.Grapple) + 10;
        
        float successChance = Mathf.Clamp((10.5f + cmb - cmd) / 20f, 0f, 1f);

        // Don't try if it's statistically impossible
        if (successChance <= 0.05f) return 0f;

        return score * successChance;
    }
}