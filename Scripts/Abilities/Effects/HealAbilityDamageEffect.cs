using Godot;

[GlobalClass]
public partial class HealAbilityDamageEffect : MythicAbilityEffectComponent
{
	    [Export]
    [Tooltip("Amount of ability damage to cure.")]
    public int AmountToHeal = 3;
	
	   [Export]
    [Tooltip("If true, targets can choose which stat to heal. If false, SpecificScore is used.")]
    public bool TargetChoosesStat = true;
	
	 [Export]
    [Tooltip("Used only when TargetChoosesStat is false.")]
    public AbilityScore SpecificScore = AbilityScore.None;
	
	 public override void ExecuteMythicEffect(EffectContext context, Ability_SO ability)
    {
		foreach (var target in context.AllTargetsInAoE)
        {
            if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;
            if (target.Template.Type == CreatureType.Construct || target.Template.Type == CreatureType.Undead) continue;

 target.HealAbilityDamage(TargetChoosesStat ? AbilityScore.None : SpecificScore, AmountToHeal);
        }
    }
	
	 public override string GetMythicDescription()
    {
        return $"Cures {AmountToHeal} points of ability damage.";
    }
}