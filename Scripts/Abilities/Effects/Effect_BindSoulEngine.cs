using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_BindSoulEngine.cs
// PURPOSE: Binds a helpless or willing creature to an adjacent, empty Soul Engine.
// =================================================================================================
[GlobalClass]
public partial class Effect_BindSoulEngine : AbilityEffectComponent
{
    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        CreatureStats caster = context.Caster;
        CreatureStats victim = context.PrimaryTarget;

        if (victim == null) return;

        // Rule: Victim must be living
        if (victim.Template.Type == CreatureType.Construct || victim.Template.Type == CreatureType.Undead)
        {
            GD.Print("Cannot bind a non-living creature to a Soul Engine.");
            return;
        }

        // Rule: Victim must be helpless or willing (For combat AI, allies are willing)
        bool isHelpless = victim.MyEffects.HasCondition(Condition.Helpless) || victim.MyEffects.HasCondition(Condition.Unconscious);
        bool isWilling = victim.IsInGroup(caster.GetGroups()[0].ToString()); 

        if (!isHelpless && !isWilling)
        {
            GD.Print("Victim must be helpless or willing to be bound to the Soul Engine.");
            return;
        }

        // Find an adjacent Soul Engine that needs a body
        SoulEngineController engineToBind = null;

        // Is the caster doing this to itself?
        if (caster.GetNodeOrNull<SoulEngineController>("SoulEngineController") != null)
        {
            engineToBind = caster.GetNode<SoulEngineController>("SoulEngineController");
        }
        else
        {
            // Or is the caster an ally doing it for an adjacent construct?
            var combatants = TurnManager.Instance.GetAllCombatants();
            foreach (var c in combatants)
            {
                var ctrl = c.GetNodeOrNull<SoulEngineController>("SoulEngineController");
                // Check if it's missing a body and adjacent to the victim
                if (ctrl != null && !ctrl.IsBodyAttached && c.GlobalPosition.DistanceTo(victim.GlobalPosition) <= 10f)
                {
                    engineToBind = ctrl;
                    break;
                }
            }
        }

        if (engineToBind == null || engineToBind.IsBodyAttached)
        {
            GD.Print("No valid, empty Soul Engine nearby.");
            return;
        }

        GD.PrintRich($"[color=purple]{caster.Name} binds {victim.Name} to the Soul Engine![/color]");
        engineToBind.AttachBody(victim);
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        return 0f; // Scored explicitly via AIAction_BindSoulEngine
    }
}