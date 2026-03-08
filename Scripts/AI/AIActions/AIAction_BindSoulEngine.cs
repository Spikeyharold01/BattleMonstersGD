using Godot;
using System.Threading.Tasks;

// =================================================================================================
// FILE: AIAction_BindSoulEngine.cs
// PURPOSE: Allows AI to attach a victim to a dying or deactivated Soul Engine construct.
// =================================================================================================
public class AIAction_BindSoulEngine : AIAction
{
    private CreatureStats victimTarget;
    private Ability_SO bindAbility;

    public AIAction_BindSoulEngine(AIController controller, CreatureStats victimTarget) : base(controller)
    {
        this.victimTarget = victimTarget;
        Name = $"Bind {victimTarget.Name} to Soul Engine";

        bindAbility = new Ability_SO { AbilityName = "Bind Soul Engine", ActionCost = ActionType.FullRound, TargetType = TargetType.SingleEnemy };
        bindAbility.EffectComponents.Add(new Effect_BindSoulEngine());
    }

    public override void CalculateScore()
    {
        bool engineNeedsBody = false;
        
        // 1. Is the AI itself a dying construct?
        var selfEngine = controller.MyStats.GetNodeOrNull<SoulEngineController>("SoulEngineController");
        if (selfEngine != null && !selfEngine.IsBodyAttached)
        {
            engineNeedsBody = true;
        }
        else
        {
            // 2. Or does the AI have an allied construct nearby that needs saving?
            var allies = AISpatialAnalysis.FindAllies(controller.MyStats);
            foreach (var ally in allies)
            {
                var ctrl = ally.GetNodeOrNull<SoulEngineController>("SoulEngineController");
                if (ctrl != null && !ctrl.IsBodyAttached && ally.GlobalPosition.DistanceTo(victimTarget.GlobalPosition) <= 10f)
                {
                    engineNeedsBody = true;
                    break;
                }
            }
        }

        if (!engineNeedsBody)
        {
            Score = -1f;
            return;
        }

        bool isHelpless = victimTarget.MyEffects.HasCondition(Condition.Helpless) || victimTarget.MyEffects.HasCondition(Condition.Unconscious);
        bool isAlly = victimTarget.IsInGroup("Player") == controller.MyStats.IsInGroup("Player");

        // The target must be helpless (enemy) or willing (an allied cultist)
        if (!isHelpless && !isAlly)
        {
            Score = -1f;
            return;
        }

        // Extremely high priority to save the construct from permanent death
        Score = 1500f;
    }

    public override async Task Execute()
    {
        controller.MyActionManager.UseAction(ActionType.FullRound);
        await CombatManager.ResolveAbility(controller.MyStats, victimTarget, victimTarget, victimTarget.GlobalPosition, bindAbility, false);
        await controller.ToSignal(controller.GetTree().CreateTimer(1.0f), "timeout");
    }
}