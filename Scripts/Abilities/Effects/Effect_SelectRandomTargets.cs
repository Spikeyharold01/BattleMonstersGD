using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: Effect_SelectRandomTargets.cs (GODOT VERSION - UPDATED v2)
// PURPOSE: Reduces or Expands the current target list to a random subset (e.g., 1d4 targets).
//          Allows specifying exactly how far to search for targets (Ability Range vs Custom).
// =================================================================================================
[GlobalClass]
public partial class Effect_SelectRandomTargets : AbilityEffectComponent
{
    [ExportGroup("Selection Count")]
    [Export] public int DiceCount = 1;
    [Export] public int DieSides = 4;
    [Export] public int FlatBonus = 0;
    
    [ExportGroup("Search Settings")]
    [Export]
    [Tooltip("If true, this component will search for new targets if the context list is empty or single.")]
    public bool AutoExpandSearch = true;

    [Export]
    [Tooltip("Where to measure the distance from.")]
    public TargetType CenterPoint = TargetType.Self; // Self = Caster, SingleEnemy = PrimaryTarget

    [ExportGroup("Range Settings")]
    [Export]
    [Tooltip("Inherit: Use the Ability's defined range.\nCustom: Use the specific Type/Distance defined below.")]
    public bool OverrideAbilityRange = false;

    [Export] public RangeType RangeTypeOverride = RangeType.Close; // Uses CombatEnums
    [Export] public float FixedDistanceOverride = 30f; // Used if RangeType is Custom/Touch

    [ExportGroup("Sorting")]
    [Export]
    [Tooltip("If true, prioritizes targets closest to the center point.")]
    public bool PrioritizeClosest = true;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        // 1. Determine Selection Center
        Vector3 centerPos = context.Caster.GlobalPosition;
        if (CenterPoint == TargetType.SingleEnemy && context.PrimaryTarget != null) centerPos = context.PrimaryTarget.GlobalPosition;
        if (CenterPoint == TargetType.Area_EnemiesOnly) centerPos = context.AimPoint;

        // 2. Determine Max Range
        float maxRange = 0f;
        if (!OverrideAbilityRange)
        {
            maxRange = ability.Range.GetRange(context.Caster);
        }
        else
        {
            int cl = context.Caster.Template.CasterLevel;
            switch (RangeTypeOverride)
            {
                case RangeType.Self: maxRange = 0f; break;
                case RangeType.Touch: maxRange = 5f + FixedDistanceOverride; break; // Fixed override acts as reach or bonus
                case RangeType.Close: maxRange = 25f + (Mathf.Floor(cl / 2f) * 5f); break;
                case RangeType.Medium: maxRange = 100f + (cl * 10f); break;
                case RangeType.Long: maxRange = 400f + (cl * 40f); break;
                case RangeType.Custom: maxRange = FixedDistanceOverride; break;
            }
        }

        // 3. Auto Expand Logic
        if (AutoExpandSearch)
        {
            var allCombatants = TurnManager.Instance.GetAllCombatants();
            var potentialCandidates = new HashSet<CreatureStats>();
            
            // Keep existing valid ones
            foreach (var existing in context.AllTargetsInAoE)
            {
                if (centerPos.DistanceTo(existing.GlobalPosition) <= maxRange)
                    potentialCandidates.Add(existing);
            }

            // Add new ones
            foreach(var c in allCombatants)
            {
                if (c == context.Caster && CenterPoint == TargetType.Self) continue; // Don't pick self if centering on self
                
                if (centerPos.DistanceTo(c.GlobalPosition) <= maxRange)
                {
                    if (TargetFilter == null || TargetFilter.IsTargetValid(context.Caster, c))
                    {
                        potentialCandidates.Add(c);
                    }
                }
            }
            
            context.AllTargetsInAoE.Clear();
            foreach(var p in potentialCandidates) context.AllTargetsInAoE.Add(p);
        }

        // 4. Filter Down to Dice Count
        int maxTargets = Dice.Roll(DiceCount, DieSides) + FlatBonus;

        if (context.AllTargetsInAoE.Count > maxTargets)
        {
            List<CreatureStats> selected;
            if (PrioritizeClosest)
            {
                selected = context.AllTargetsInAoE
                    .OrderBy(t => t.GlobalPosition.DistanceTo(centerPos))
                    .Take(maxTargets)
                    .ToList();
            }
            else
            {
                var rng = new RandomNumberGenerator();
                rng.Randomize();
                selected = context.AllTargetsInAoE
                    .OrderBy(_ => rng.Randf())
                    .Take(maxTargets)
                    .ToList();
            }
            
            context.AllTargetsInAoE.Clear();
            foreach(var s in selected) context.AllTargetsInAoE.Add(s);
        }
        
        GD.Print($"Effect_SelectRandomTargets selected {context.AllTargetsInAoE.Count} targets within {maxRange}ft of {CenterPoint}.");
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        float avg = (DiceCount * (DieSides / 2f + 0.5f)) + FlatBonus;
        return avg * 10f;
    }
}