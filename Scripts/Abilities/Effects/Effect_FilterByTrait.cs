using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_FilterByTrait.cs
// PURPOSE: Removes targets from the current execution context based on specific conditions.
//          Replaces hardcoded checks for "Immune to Crits", "Has Blood", etc.
// =================================================================================================
public enum TraitCheckType 
{ 
    ImmuneToCriticalHits, 
    HasBlood, 
    IsLiving, 
    IsUndead,
    IsConstruct
}

[GlobalClass]
public partial class Effect_FilterByTrait : AbilityEffectComponent
{
    [Export] public TraitCheckType TraitToCheck;
    [Export] public bool ExcludeIfTrue = true; // If true, remove targets that MATCH the trait.

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        for (int i = context.AllTargetsInAoE.Count - 1; i >= 0; i--)
        {
            var target = context.AllTargetsInAoE[i];
            bool hasTrait = CheckTrait(target);

            if (hasTrait == ExcludeIfTrue)
            {
                GD.Print($"{target.Name} filtered out by {TraitToCheck} check.");
                context.AllTargetsInAoE.RemoveAt(i);
            }
        }
    }

    private bool CheckTrait(CreatureStats target)
    {
        switch (TraitToCheck)
        {
            case TraitCheckType.ImmuneToCriticalHits:
                return target.IsImmuneToCriticalHits(); // Uses the method added to CreatureStats in previous step
            case TraitCheckType.IsLiving:
                return target.Template.Type != CreatureType.Undead && target.Template.Type != CreatureType.Construct;
            case TraitCheckType.IsUndead:
                return target.Template.Type == CreatureType.Undead;
            case TraitCheckType.IsConstruct:
                return target.Template.Type == CreatureType.Construct;
            default:
                return false;
        }
    }

    public override float GetAIEstimatedValue(EffectContext context) { return 0f; }
}