using Godot;
using System.Threading.Tasks;

// =================================================================================================
// FILE: AIAction_PrySoulEngine.cs
// PURPOSE: Allows AI to decide to rip the body off a Soul Engine construct.
// =================================================================================================
public class AIAction_PrySoulEngine : AIAction
{
    private CreatureStats targetConstruct;
    private Ability_SO pryAbility;

    public AIAction_PrySoulEngine(AIController controller, CreatureStats target) : base(controller)
    {
        this.targetConstruct = target;
        Name = $"Pry body off {target.Name}'s Soul Engine";

        // Generate the ability dynamically so we don't need a hardcoded asset file.
        pryAbility = new Ability_SO();
        pryAbility.AbilityName = "Pry Soul Engine";
        pryAbility.ActionCost = ActionType.Standard;
        pryAbility.TargetType = TargetType.SingleEnemy;
        pryAbility.EffectComponents.Add(new Effect_PrySoulEngine());
    }

    public override void CalculateScore()
    {
        var context = new EffectContext { Caster = controller.MyStats, PrimaryTarget = targetConstruct };
        Score = pryAbility.EffectComponents[0].GetAIEstimatedValue(context);

        // If the AI is very smart, they recognize this weakness faster.
        if (controller.MyStats.Template.Intelligence > 10)
        {
            Score *= 1.5f; 
        }
    }

    public override async Task Execute()
    {
        // Prying provokes an Attack of Opportunity like a normal grapple.
        await AoOManager.Instance.CheckAndResolve(controller.MyStats, ProvokingActionType.CombatManeuver);
        if (!GodotObject.IsInstanceValid(controller) || controller.MyStats.CurrentHP <= 0) return;

        controller.MyActionManager.UseAction(ActionType.Standard);
        await CombatManager.ResolveAbility(controller.MyStats, targetConstruct, targetConstruct, targetConstruct.GlobalPosition, pryAbility, false);
        await controller.ToSignal(controller.GetTree().CreateTimer(1.0f), "timeout");
    }
}