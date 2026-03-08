using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class CombatManeuverEffect : AbilityEffectComponent
{
    [Export] public ManeuverType Maneuver;

    [ExportGroup("Targeting")]
    [Export] public bool ApplyToAllTargetsInAoE = false;

    [ExportGroup("Bull Rush Overrides")]
    [Export] public bool UseCasterLevelAndAbilityForBullRushCmb = false;
    [Export] public AbilityScore BullRushAbility = AbilityScore.Strength;
    [Export] public int FlatBullRushBonus = 0;
    [Export] public int MythicBullRushBonus = 0;
    [Export] public bool BullRushDoesNotProvokeAoO = false;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        CreatureStats caster = context.Caster;
        if (caster == null) return;

        var targets = new List<CreatureStats>();
        if (ApplyToAllTargetsInAoE)
        {
            foreach (var aoeTarget in context.AllTargetsInAoE)
            {
                if (aoeTarget != null) targets.Add(aoeTarget);
            }
        }
        else if (context.PrimaryTarget != null)
        {
            targets.Add(context.PrimaryTarget);
        }

        foreach (var target in targets)
        {
            if (target == null || target == caster) continue;
            if (TargetFilter != null && !TargetFilter.IsTargetValid(caster, target)) continue;

            switch (Maneuver)
            {
                case ManeuverType.BullRush:
                    ResolveBullRush(context, caster, target);
                    break;
                case ManeuverType.Trip:
                    CombatManager.ResolveTrip(caster, target);
                    break;
            }
        }
    }

    private void ResolveBullRush(EffectContext context, CreatureStats caster, CreatureStats target)
    {
        bool useOverride = UseCasterLevelAndAbilityForBullRushCmb;
        if (!useOverride && !BullRushDoesNotProvokeAoO && FlatBullRushBonus == 0 && MythicBullRushBonus == 0)
        {
            CombatManager.ResolveBullRush(caster, target);
            return;
        }

        int cmb = useOverride
            ? (caster.Template?.CasterLevel ?? 0) + GetAbilityModifier(caster, BullRushAbility)
            : caster.GetCMB(ManeuverType.BullRush);

        cmb += FlatBullRushBonus;
        if (context.IsMythicCast)
        {
            cmb += MythicBullRushBonus;
        }

        int maneuverRoll = Dice.Roll(1, 20) + cmb;
        int defenderCMD = target.GetCMD(ManeuverType.BullRush);
        if (maneuverRoll < defenderCMD) return;

        int excess = maneuverRoll - defenderCMD;
        int squaresToPush = 1 + (excess / 5);
        float distanceToPush = squaresToPush * 5f;

        Vector3 direction = (target.GlobalPosition - caster.GlobalPosition).Normalized();
        if (direction == Vector3.Zero) direction = Vector3.Forward;

        Vector3 startPosition = target.GlobalPosition;
        target.GlobalPosition = startPosition + direction * distanceToPush;
        if (target.GlobalPosition != startPosition)
        {
            target.TriggerForcedMovement(caster);
        }
    }

    private static int GetAbilityModifier(CreatureStats caster, AbilityScore score)
    {
        return score switch
        {
            AbilityScore.Strength => caster.StrModifier,
            AbilityScore.Dexterity => caster.DexModifier,
            AbilityScore.Constitution => caster.ConModifier,
            AbilityScore.Intelligence => caster.IntModifier,
            AbilityScore.Wisdom => caster.WisModifier,
            AbilityScore.Charisma => caster.ChaModifier,
            _ => 0
        };
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        int targetCount = ApplyToAllTargetsInAoE ? context.AllTargetsInAoE.Count : (context.PrimaryTarget != null ? 1 : 0);
        if (targetCount <= 0) return 0f;
        return 50f * targetCount;
    }
}
