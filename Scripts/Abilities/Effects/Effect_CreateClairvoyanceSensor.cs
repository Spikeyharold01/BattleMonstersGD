using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_CreateClairvoyanceSensor.cs
// PURPOSE: Creates a fixed, invisible remote viewer for Clairaudience/Clairvoyance.
// =================================================================================================
[GlobalClass]
public partial class Effect_CreateClairvoyanceSensor : AbilityEffectComponent
{
    [Export] public bool RequireCurrentLineOfSight = true;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (context?.Caster == null || ability == null) return;

        var caster = context.Caster;
        Vector3 sensorPosition = context.AimPoint;

        if (RequireCurrentLineOfSight)
        {
            bool hasCurrentSight = LineOfSightManager.HasLineOfEffect(caster, caster.GlobalPosition + Vector3.Up * 1.5f, sensorPosition + Vector3.Up * 1.5f);
            if (!hasCurrentSight)
            {
                GD.PrintRich($"[color=orange]{ability.AbilityName} fails: target location is not currently within line of sight.[/color]");
                return;
            }
        }

        float maxDistance = ability.Range?.GetRange(caster) ?? (400f + (caster.Template.CasterLevel * 40f));
        if (caster.GlobalPosition.DistanceTo(sensorPosition) > maxDistance)
        {
            GD.PrintRich($"[color=orange]{ability.AbilityName} fails: sensor location is out of range.[/color]");
            return;
        }

        float durationSeconds = Mathf.Max(6f, caster.Template.CasterLevel * 60f); // 1 min/level.

        var sensorAnchor = new Node3D();
        sensorAnchor.Name = $"ClairvoyanceSensor_{caster.Name}";
        sensorAnchor.GlobalPosition = sensorPosition;

        var sensorController = new ClairvoyanceSensorController();
        sensorController.Name = "ClairvoyanceSensorController";
        sensorAnchor.AddChild(sensorController);

        var tree = (SceneTree)Engine.GetMainLoop();
        tree.CurrentScene.AddChild(sensorAnchor);

        sensorController.Initialize(caster, durationSeconds, maxDistance);

        GD.PrintRich($"[color=cyan]{caster.Name} creates an unseen clairvoyance sensor at {sensorPosition} for {durationSeconds / 60f:0.#} minute(s).[/color]");
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        return 75f;
    }
}