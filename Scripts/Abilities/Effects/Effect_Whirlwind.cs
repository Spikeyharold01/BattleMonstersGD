using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_Whirlwind.cs
// PURPOSE: Transforms the caster into a Whirlwind, attaching the physics controller and setting rules.
// =================================================================================================
[GlobalClass]
public partial class Effect_Whirlwind : AbilityEffectComponent
{
    [ExportGroup("Whirlwind Stats")]
    [Export] public float Height = 30f;
    [Export] public DamageInfo BaseDamage;
    
    [ExportGroup("Extra Payloads (e.g. Lightning's Kiss)")]
    [Export] public Godot.Collections.Array<Ability_SO> PayloadAbilities = new();

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        var caster = context.Caster;
        if (caster == null) return;

        int hd = CreatureRulesUtility.GetHitDiceCount(caster);
        int durationRounds = Mathf.Max(1, hd / 2);

        // Apply Transformation Status
        var formEffect = new StatusEffect_SO
        {
            EffectName = "Whirlwind Form",
            ConditionApplied = Condition.WhirlwindForm,
            DurationInRounds = durationRounds
        };
        caster.MyEffects.AddEffect(formEffect, caster, ability);

        // Remove old controller if re-casting
        caster.GetNodeOrNull<WhirlwindController>("WhirlwindController")?.QueueFree();

        // Attach Runtime Physics/Logic Controller
        var controller = new WhirlwindController();
        controller.Name = "WhirlwindController";
        caster.AddChild(controller);
        
        controller.Initialize(caster, ability, Height, BaseDamage, PayloadAbilities);
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (context.Caster.MyEffects.HasCondition(Condition.WhirlwindForm)) return 0f; // Already active

        var enemies = AISpatialAnalysis.FindVisibleTargets(context.Caster);
        float score = 150f; // Base massive utility

        foreach (var enemy in enemies)
        {
            if ((int)enemy.Template.Size < (int)context.Caster.Template.Size)
            {
                score += 50f; // Great for sweeping up smaller targets
            }
        }
        return score;
    }
}