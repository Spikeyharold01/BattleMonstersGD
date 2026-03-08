using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_DiscernLocation.cs
// PURPOSE: Reveals a previously seen creature to the caster for the rest of the current turn.
// =================================================================================================
[GlobalClass]
public partial class Effect_DiscernLocation : AbilityEffectComponent
{
    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        var caster = context?.Caster;
        var target = context?.PrimaryTarget;
        if (caster == null || target == null)
        {
            GD.PrintRich("[color=orange]Discern Location fails: no creature target selected.[/color]");
            return;
        }

        var stateController = caster.GetNodeOrNull<CombatStateController>("CombatStateController");
        if (stateController == null || !stateController.HasSeenCreature(target))
        {
            GD.PrintRich($"[color=orange]Discern Location fails: {caster.Name} has not seen {target.Name} in this match.[/color]");
            return;
        }

        VisibilityManager.Instance?.GrantTemporaryReveal(caster, target);
        VisibilityManager.Instance?.UpdateVisibility(caster);

        GD.PrintRich($"[color=cyan]{caster.Name} discerns {target.Name}'s location until the end of this turn.[/color]");
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        var caster = context?.Caster;
        if (caster == null) return 0f;

        var stateController = caster.GetNodeOrNull<CombatStateController>("CombatStateController");
        if (stateController == null) return 0f;

        bool hasSeenUnknownEnemy = false;
        foreach (var kvp in stateController.EnemyLocationStates)
        {
            if (kvp.Key != null && stateController.HasSeenCreature(kvp.Key) && kvp.Value < LocationStatus.Pinpointed)
            {
                hasSeenUnknownEnemy = true;
                break;
            }
        }

        return hasSeenUnknownEnemy ? 35f : 0f;
    }
}