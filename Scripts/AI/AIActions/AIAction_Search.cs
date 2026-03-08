using Godot;
using System.Threading.Tasks;
// =================================================================================================
// FILE: AIAction_Search.cs (GODOT VERSION)
// PURPOSE: AI Action to actively search for hidden enemies.
// ATTACH TO: Do not attach (Pure C# Class).
// =================================================================================================
public class AIAction_Search : AIAction
{
public AIAction_Search(AIController controller) : base(controller)
{
Name = "Search for hidden foes";
}

public override void CalculateScore()
{
    // 1. Did we just take damage from an unknown source?
    bool tookInvisibleDamage = controller.MyStats.LastDamageSource == null && controller.MyStats.LastDamageAmount > 0;
    
    // 2. Do we have a memory of an enemy vanishing?
    // Note: Checking highest threat's condition directly might be "cheating" if not remembered. 
    // Using CombatMemory access to object reference implies known object state? 
    // If they are invisible, can we access them? Assuming yes for logic, visibility handled elsewhere.
    var highestThreat = CombatMemory.GetHighestThreat();
    bool knownInvisibleEnemy = highestThreat != null && highestThreat.MyEffects.HasCondition(Condition.Invisible);

    // 3. Do we have senses that make searching good? (Scent, Spiritsense)
    float senseBonus = 0;
    if (controller.MyStats.Template.HasScent) senseBonus += 20;
    if (controller.MyStats.Template.HasSpiritsense) senseBonus += 20; 

    if (tookInvisibleDamage || knownInvisibleEnemy)
    {
        Score = 100f + senseBonus;
        Name += " (Suspects Invisible Enemy)";
    }
    else
    {
        Score = -1f; 
    }
}

public override async Task Execute()
{
    controller.MyActionManager.UseAction(ActionType.Move);
    GD.PrintRich($"<color=yellow>{controller.GetParent().Name} actively searches the area!</color>");

    // Apply a temporary "Searching" buff
    var searchEffect = new StatusEffect_SO();
    searchEffect.EffectName = "Actively Searching";
    searchEffect.DurationInRounds = 1;
    // Godot Arrays/Lists
    searchEffect.Modifications.Add(new StatModification { StatToModify = StatToModify.Perception, ModifierValue = 5, BonusType = BonusType.Circumstance });
    
    controller.MyStats.MyEffects.AddEffect(searchEffect, controller.MyStats);

    // Force a re-scan of targets immediately
    // Note: Re-entering DecideAndExecuteBestTurnPlan recursively is dangerous if it generates another search action.
    // It should check remaining actions.
    // We will execute the logic.
    await controller.DecideAndExecuteBestTurnPlan();
}
}