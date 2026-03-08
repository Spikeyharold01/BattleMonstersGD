using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: EscapeGrappleEffect.cs (GODOT VERSION)
// PURPOSE: Logic for escaping a grapple using Escape Artist.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class EscapeGrappleEffect : AbilityEffectComponent
{
[Export]
[Tooltip("The condition this action attempts to remove (Grappled or Pinned).")]
public Condition ConditionToEscape;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    CreatureStats self = context.Caster;
    if (self == null || !self.MyEffects.HasCondition(ConditionToEscape)) return;
    if (self.CurrentGrappleState == null) return;

    var grappler = (self.CurrentGrappleState.Controller == self) ? self.CurrentGrappleState.Target : self.CurrentGrappleState.Controller;
    if (grappler == null) return;

    int dc = grappler.GetCMD();
    int escapeRoll = Dice.Roll(1, 20) + self.GetSkillBonus(SkillType.EscapeArtist);

    GD.Print($"{self.Name} attempts to escape {ConditionToEscape} from {grappler.Name} with Escape Artist. Roll: {escapeRoll} vs DC: {dc}.");

    if (escapeRoll >= dc)
    {
        GD.PrintRich($"<color=green>Success!</color>");
        if (ConditionToEscape == Condition.Pinned)
        {
            self.MyEffects.RemoveEffect("Pinned");
            
            if (ResourceLoader.Exists("res://Data/StatusEffects/Grappled_Effect.tres"))
            {
                var grappleEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Grappled_Effect.tres");
                if (grappleEffect != null) self.MyEffects.AddEffect((StatusEffect_SO)grappleEffect.Duplicate(), grappler, null);
            }
            GD.Print($"{self.Name} is no longer Pinned, but is still Grappled.");
        }
        else // Escaping Grappled
        {
            CombatManeuvers.BreakGrapple(self);
            GD.Print($"{self.Name} has escaped the grapple.");
        }
    }
    else
    {
        GD.PrintRich($"<color=red>Failure.</color> The escape attempt fails.");
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    CreatureStats self = context.Caster;
    if (!self.MyEffects.HasCondition(ConditionToEscape)) return 0f;
    if (self.CurrentGrappleState == null) return 0f;

    var grappler = (self.CurrentGrappleState.Controller == self) ? self.CurrentGrappleState.Target : self.CurrentGrappleState.Controller;
    if (grappler == null) return 0f;
    
    float score = (ConditionToEscape == Condition.Pinned) ? 500f : 200f;
    int dc = grappler.GetCMD();
    float successChance = Mathf.Clamp((10.5f + self.GetSkillBonus(SkillType.EscapeArtist) - dc) / 20f, 0f, 1f);
    
    return score * successChance;
}
}