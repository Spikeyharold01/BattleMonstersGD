using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class Effect_StartScanning : AbilityEffectComponent
{
    [Export] public ScanType Mode;
    [Export] public float DurationOverride = 0f; // 0 = Use spell duration logic (e.g. 1 min/level)

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        var existing = context.Caster.GetNodeOrNull<ConcentrationEffect_Scanner>("ConcentrationEffect_Scanner");
        if (existing != null) existing.QueueFree(); // Replace old scan

        var scanner = new ConcentrationEffect_Scanner();
        scanner.Name = "ConcentrationEffect_Scanner";
        context.Caster.AddChild(scanner);
        
        float duration = DurationOverride;
        if (duration <= 0) duration = context.Caster.Template.CasterLevel * 10f * 6f; // Default 10 min/level (600s) for some, 1 min for others. Config via override best.

        scanner.Initialize(context.Caster, duration, Mode, ability.AreaOfEffect);
    }
    
    public override float GetAIEstimatedValue(EffectContext context)
    {
        // AI Logic: If we don't know much about the enemies, scanning is good.
        var enemies = AISpatialAnalysis.FindVisibleTargets(context.Caster);
        if (enemies.Count == 0) return 0f;
        
        // Simple heuristic: 10 points per unknown enemy
        return 10f * enemies.Count;
    }
}