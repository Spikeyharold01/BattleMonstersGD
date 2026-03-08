using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: RideSkillEffect.cs (GODOT VERSION)
// PURPOSE: An effect component for resolving data-driven Ride skill actions.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
public enum RideActionType { GuideWithKnees, StayInSaddle, FightWithTrainedMount, Cover, SoftFall, Leap, SpurMount, ControlMountInBattle, FastMountOrDismount }
[GlobalClass]
public partial class RideSkillEffect : AbilityEffectComponent
{
[Export]
[Tooltip("The specific Ride action this effect will perform.")]
public RideActionType ActionToPerform;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    CreatureStats rider = context.Caster;
    if (rider == null) return;

    var mountedController = rider.MyMountedCombat;
    if (mountedController == null) return;

    int rideCheck = Dice.Roll(1, 20) + rider.GetSkillBonus(SkillType.Ride);
    int dc = ability.SkillCheck.BaseDC;

    GD.Print($"{rider.Name} attempts Ride Action '{ActionToPerform}' (Roll: {rideCheck} vs DC: {dc}).");
    bool success = rideCheck >= dc;

    switch (ActionToPerform)
    {
        case RideActionType.SpurMount:
            if (success)
            {
                var mount = rider.MyMount;
                if (mount != null && !mount.MyEffects.HasCondition(Condition.Fatigued))
                {
                    GD.Print("Success! Mount's speed increases by 10ft for 1 round.");
                    var speedBuff = new StatusEffect_SO();
                    speedBuff.EffectName = "Spurred";
                    speedBuff.DurationInRounds = 1;
                    mount.MyEffects.AddEffect(speedBuff, rider, ability);
                    mount.TakeDamage(Dice.Roll(1, 3), "Untyped", null, null, null);

                    mountedController.RoundsSpurred++;
                    if (mountedController.RoundsSpurred >= mount.Template.Constitution)
                    {
                        if (ResourceLoader.Exists("res://Data/StatusEffects/Fatigued_Effect.tres"))
                        {
                            var fatiguedEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Fatigued_Effect.tres");
                            if (fatiguedEffect != null) mount.MyEffects.AddEffect((StatusEffect_SO)fatiguedEffect.Duplicate(), rider, ability);
                        }
                    }
                }
            }
            break;

        case RideActionType.Cover: 
            mountedController.IsUsingMountAsCover = success;
            GD.Print(success ? "Success! Using mount as cover." : "Failure! Cover attempt failed.");
            break;

        case RideActionType.FastMountOrDismount:
            if (success)
            {
                if (rider.IsMounted) rider.Dismount();
                else if (context.PrimaryTarget != null) rider.Mount(context.PrimaryTarget);
            }
            else
            {
                GD.Print("Fast Mount/Dismount failed. Consumes a move action instead.");
                rider.GetNodeOrNull<ActionManager>("ActionManager")?.UseAction(ActionType.Move);
                if (rider.IsMounted) rider.Dismount();
                else if (context.PrimaryTarget != null) rider.Mount(context.PrimaryTarget);
            }
            break;
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    float score = 0;
    switch (ActionToPerform)
    {
        case RideActionType.SpurMount:
            var aiController = context.Caster.GetNodeOrNull<AIController>("AIController");
            var primaryTarget = aiController?.GetPerceivedHighestThreat();
            if (primaryTarget != null)
            {
                var mover = context.Caster.GetNodeOrNull<CreatureMover>("CreatureMover");
                if (mover == null) break;

                float currentDoubleMove = mover.GetEffectiveMovementSpeed() * 2f;
                float spurredDoubleMove = (mover.GetEffectiveMovementSpeed() + 10f) * 2f;
                float distanceToTarget = context.Caster.GlobalPosition.DistanceTo(primaryTarget.GlobalPosition);

                if (distanceToTarget > currentDoubleMove && distanceToTarget <= spurredDoubleMove)
                {
                    score = 150f; 
                }
            }
            break;
    }
    return score;
}
}