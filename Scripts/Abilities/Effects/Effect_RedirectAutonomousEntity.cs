using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class Effect_RedirectAutonomousEntity : AbilityEffectComponent
{
    [Export] public string TargetEntityIdentifier = "Spiritual Weapon"; 

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        var entities = context.Caster.GetTree().GetNodesInGroup("AutonomousEntities");
        
        foreach (GridNode n in entities)
        {
            if (n is AutonomousEntityController entity && entity.Caster == context.Caster && entity.EntityGroupId == TargetEntityIdentifier)
            {
                entity.Redirect(context.PrimaryTarget);
                return; 
            }
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        return 0f; 
    }
}