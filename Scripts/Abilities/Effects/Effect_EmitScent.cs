using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_EmitScent.cs (GODOT VERSION)
// PURPOSE: Emits a ScentEvent from caster/target/environment for scent-capable AI.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_EmitScent : AbilityEffectComponent
{
    [ExportGroup("Scent Properties")]
    [Export] public ScentType ScentType = ScentType.Environment;
    [Export] public float Intensity = 0.8f;
    [Export] public float DecayRate = 0.22f;
    [Export] public float VerticalBias = 0f;
    [Export] public bool IsTrail = false;
    [Export] public float DurationSeconds = 6f;

    [ExportGroup("Origin")]
    [Export] public TargetType ScentOrigin = TargetType.Self;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (context?.Caster == null) return;

        Vector3 origin = context.Caster.GlobalPosition;

        if (ScentOrigin == TargetType.Area_EnemiesOnly || ScentOrigin == TargetType.Area_FriendOrFoe || ScentOrigin == TargetType.Area_AlliesOnly)
        {
            origin = context.AimPoint;
        }
        else if (ScentOrigin == TargetType.SingleEnemy && context.PrimaryTarget != null)
        {
            origin = context.PrimaryTarget.GlobalPosition;
        }
        else if (context.TargetObject is Node3D targetNode)
        {
            origin = targetNode.GlobalPosition;
        }

        ScentSystem.EmitScent(context.Caster, origin, Intensity, DecayRate, VerticalBias, ScentType, IsTrail, DurationSeconds);
        GD.Print($"[Effect_EmitScent] {ability.AbilityName} emitted {ScentType} scent at {origin} (I={Intensity:F2}).");
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        return 0f;
    }
}