using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: Effect_Silence.cs
// PURPOSE: Casts Silence. Can target a creature (mobile, allows Will save) or a point (stationary).
// =================================================================================================
[GlobalClass]
public partial class Effect_Silence : AbilityEffectComponent
{
    [Export]
    [Tooltip("The StatusEffect_SO granting Condition.Silenced and Sonic damage immunity.")]
    public StatusEffect_SO SilencedStatusTemplate;

    [Export] public float Radius = 20f;
    [Export] public int RoundsPerLevel = 1;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (SilencedStatusTemplate == null)
        {
            GD.PrintErr("Effect_Silence is missing its StatusEffect_SO template.");
            return;
        }
        
        float durationSeconds = Mathf.Max(1, context.Caster.Template.CasterLevel) * RoundsPerLevel * 6f;

        // TARGETING A SPECIFIC CREATURE (Mobile Aura)
        if (context.PrimaryTarget != null)
        {
            // If the target succeeded on a Will Save or Spell Resistance, the effect fails to latch onto them.
            if (!context.AllTargetsInAoE.Contains(context.PrimaryTarget)) return;

            bool saved = targetSaveResults.ContainsKey(context.PrimaryTarget) && targetSaveResults[context.PrimaryTarget];
            if (saved)
            {
                GD.Print($"{context.PrimaryTarget.Name} resists the Silence spell. The spell is negated.");
                return;
            }

            var aura = new SilenceAuraController();
            aura.Name = "SilenceAuraController";
            context.PrimaryTarget.AddChild(aura);
            aura.Initialize(context.Caster, durationSeconds, Radius, SilencedStatusTemplate);
            GD.PrintRich($"[color=cyan]{context.Caster.Name} centers Silence on {context.PrimaryTarget.Name}. The aura moves with them![/color]");
        }
        // TARGETING A POINT IN SPACE (Stationary Aura)
        else
        {
            var auraAnchor = new Node3D();
            auraAnchor.Name = "SilenceAuraAnchor";
            
            var tree = (SceneTree)Engine.GetMainLoop();
            tree.CurrentScene.AddChild(auraAnchor);
            auraAnchor.GlobalPosition = context.AimPoint;

            var aura = new SilenceAuraController();
            aura.Name = "SilenceAuraController";
            auraAnchor.AddChild(aura);
            aura.Initialize(context.Caster, durationSeconds, Radius, SilencedStatusTemplate);
            GD.PrintRich($"[color=cyan]{context.Caster.Name} casts Silence at {context.AimPoint}. The aura is stationary.[/color]");
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        float score = 0f;
        var caster = context.Caster;
        
        Vector3 center = context.PrimaryTarget != null ? context.PrimaryTarget.GlobalPosition : context.AimPoint;
        var targetsInRadius = TurnManager.Instance.GetAllCombatants().Where(c => c.GlobalPosition.DistanceTo(center) <= Radius).ToList();

        int silencedEnemyCasters = 0;
        int silencedAllyCasters = 0;

        foreach (var t in targetsInRadius)
        {
            // Simple heuristic to identify casters: do they have spells or a primary casting stat?
            bool isCaster = t.Template.KnownAbilities.Any(a => a.Category == AbilityCategory.Spell && a.Components != null && a.Components.HasVerbal) || t.Template.PrimaryCastingStat != AbilityScore.None;
            if (!isCaster) continue;

            if (t.IsInGroup("Player") != caster.IsInGroup("Player")) silencedEnemyCasters++;
            else silencedAllyCasters++;
        }

        score += silencedEnemyCasters * 150f;
        score -= silencedAllyCasters * 150f; // Strongly disincentivize silencing allied casters

        if (context.PrimaryTarget != null)
        {
            if (context.PrimaryTarget.IsInGroup("Player") != caster.IsInGroup("Player"))
            {
                // Targeting an enemy directly allows a Will save and Spell Resistance. Evaluate risk.
                float saveChance = Mathf.Clamp((10.5f + context.PrimaryTarget.GetWillSave(caster) - context.Ability.SavingThrow.BaseDC) / 20f, 0.05f, 0.95f);
                float srChance = caster.GetNodeOrNull<AIController>("AIController")?.PredictSuccessChanceVsSR(context.PrimaryTarget, context.Ability) ?? 1.0f;
                score *= (1f - saveChance) * srChance;
            }
            else
            {
                // Classic tactical trick: Cast on allied melee fighter (no save) running into the enemy backline.
                score *= 1.35f; 
            }
        }

        return Mathf.Max(0f, score);
    }
}