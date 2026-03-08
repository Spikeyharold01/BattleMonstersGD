using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class Effect_GrantInvisibilityPurge : AbilityEffectComponent
{
    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        var caster = context.Caster;
        
        // Check for existing
        var existing = caster.GetNodeOrNull<InvisibilityPurgeController>("InvisibilityPurgeController");
        if (existing != null) existing.QueueFree();

        var controller = new InvisibilityPurgeController();
        controller.Name = "InvisibilityPurgeController";
        caster.AddChild(controller);
        
        float duration = context.Caster.Template.CasterLevel * 10f * 6f; // 1 min/level
        float radius = context.Caster.Template.CasterLevel * 5f; // 5 ft/level
        
        controller.Initialize(caster, duration, radius);
        GD.Print($"{caster.Name} activates Invisibility Purge ({radius}ft).");
    }
    
    public override float GetAIEstimatedValue(EffectContext context)
    {
        // AI Logic: Use if invisible enemies are suspected
        // Or if took damage from unknown source.
        // We can check CombatMemory "SuspectsInvisible".
        
        var enemies = AISpatialAnalysis.FindVisibleTargets(context.Caster);
        // If we can't see anyone, but are in combat?
        if (enemies.Count == 0 && TurnManager.Instance.GetAllCombatants().Count > 1)
        {
             return 50f; // Good scanning tool
        }
        return 0f;
    }
}