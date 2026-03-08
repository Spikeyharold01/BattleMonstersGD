using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_SoulStealCorruption.cs
// PURPOSE: First full use of the Corruption Stack Framework via Soul Steal.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_SoulStealCorruption : AbilityEffectComponent
{
    [ExportGroup("Soul Steal Corruption Profile")]
    [Export] public CorruptionEffectDefinition SoulCorruptionDefinition;
    [Export] public string StacksDice = "1d4";
    [Export] public int TemporaryHpPerStack = 5;
    [Export(PropertyHint.Range, "0,1,0.01")] public float TemporaryHpCapPercent = 0.25f;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (SoulCorruptionDefinition == null || context?.Caster == null) return;

        CreatureStats caster = context.Caster;
        int maxTempHpFromSoulSteal = Mathf.CeilToInt(caster.GetEffectiveMaxHP() * TemporaryHpCapPercent);

        foreach (CreatureStats target in context.AllTargetsInAoE)
        {
            if (target == null || target == caster) continue;
            if (TargetFilter != null && !TargetFilter.IsTargetValid(caster, target)) continue;
            if (target.Template.Type == CreatureType.Undead || target.Template.Type == CreatureType.Construct) continue;

            bool didSave = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
            if (didSave) continue;

            int attemptedStacks = Dice.Roll(StacksDice);
            int appliedStacks = target.MyEffects.ApplyCorruption(SoulCorruptionDefinition, caster, attemptedStacks);
            if (appliedStacks <= 0) continue;

            int requestedTempHp = appliedStacks * TemporaryHpPerStack;
            int availableTempHpRoom = Mathf.Max(0, maxTempHpFromSoulSteal - caster.TemporaryHP);
            int grantedTempHp = Mathf.Min(requestedTempHp, availableTempHpRoom);
            if (grantedTempHp > 0)
            {
                caster.AddTemporaryHP(grantedTempHp);
            }

            GD.PrintRich($"<color=purple>{caster.Name} steals soul force from {target.Name}: +{appliedStacks} stack(s), +{grantedTempHp} temporary HP.</color>");
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (context?.Caster == null) return 0f;
        return 180f * Mathf.Max(1, context.AllTargetsInAoE.Count);
    }
}
