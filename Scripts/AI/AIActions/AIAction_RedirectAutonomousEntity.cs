using Godot;
using System.Threading.Tasks;

public class AIAction_RedirectAutonomousEntity : AIAction
{
    private AutonomousEntityController myEntity;
    private CreatureStats newTarget;
    private Ability_SO redirectAbility;

    public AIAction_RedirectAutonomousEntity(AIController controller, AutonomousEntityController entity, CreatureStats newTarget) : base(controller)
    {
        this.myEntity = entity;
        this.newTarget = newTarget;
        Name = $"Redirect {entity.EntityGroupId} to {newTarget.Name}";

        redirectAbility = new Ability_SO { AbilityName = "Redirect Entity", ActionCost = ActionType.Move, TargetType = TargetType.SingleEnemy };
        
        var redirectEffect = new Effect_RedirectAutonomousEntity();
        redirectEffect.TargetEntityIdentifier = entity.EntityGroupId;
        redirectAbility.EffectComponents.Add(redirectEffect);
    }

    public override void CalculateScore()
    {
        bool needsRedirect = myEntity.CurrentTarget == null || myEntity.CurrentTarget.CurrentHP <= 0;

        if (needsRedirect && newTarget == controller.GetPerceivedHighestThreat())
        {
            Score = 150f; 
        }
        else
        {
            Score = 0f; 
        }
    }

    public override async Task Execute()
    {
        controller.MyActionManager.UseAction(ActionType.Move);
        await CombatManager.ResolveAbility(controller.MyStats, newTarget, newTarget, newTarget.GlobalPosition, redirectAbility, false);
    }
}