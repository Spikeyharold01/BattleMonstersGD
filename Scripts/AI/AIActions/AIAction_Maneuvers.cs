using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// =================================================================================================
// FILE: AIAction_Maneuvers.cs (GODOT VERSION)
// PURPOSE: Combat Maneuver AI Actions (Bull Rush, Grapple, etc.).
// ATTACH TO: Do not attach (Pure C# Classes).
// =================================================================================================
public class AIAction_BullRush : AIAction
{
private CreatureStats target;

public AIAction_BullRush(AIController controller, CreatureStats target) : base(controller)
{
    this.target = target;
    Name = $"Bull Rush {target.Name}";
}

public override void CalculateScore()
{
    if (isProne) { Score = -1; return; }
    if ((int)target.Template.Size > (int)controller.MyStats.Template.Size + 1) { Score = -1; return; }
    float score = 5f;
    Vector3 pushDirection = (target.GlobalPosition - controller.GetParent<Node3D>().GlobalPosition).Normalized();
    Vector3 destinationPos = target.GlobalPosition + pushDirection * 5f;
    GridNode destinationNode = GridManager.Instance.NodeFromWorldPoint(destinationPos);
    if (destinationNode.heightOfDropBelow > 2) score += 150f;
    if (destinationNode.movementCost > 10) score += 75f;
	
    // Find PersistentEffect_AcidPool using Group or Find logic. Assuming PersistentEffectController base.
    // Or search group "AcidPools"
    var acidPools = controller.GetTree().GetNodesInGroup("AcidPools").Cast<PersistentEffect_AcidPool>(); // Assuming AcidPool adds to group
    // If not, generic FindObjects via loop
    // We will assume "PersistentEffect_AcidPool" inherits Node3D and is identifiable
    
    // Fallback search logic:
    // Since we can't efficiently `FindObjectsOfType` in Godot without Groups or Root traversal.
    // Assuming Acid Pools are registered in EffectManager if possible, or using Groups.
    // Let's assume AcidPools add themselves to group "AcidPool".
    
    foreach(var pool in controller.GetTree().GetNodesInGroup("AcidPool"))
    {
        var acidPool = pool as PersistentEffect_AcidPool; // Needs AcidPool script converted
        if(acidPool != null && acidPool.GlobalPosition.DistanceTo(destinationPos) <= acidPool.PoolRadius)
        {
            bool isKnownImmune = CombatMemory.IsTraitIdentified(target, "Immunity:Acid");

            if (isKnownImmune)
            {
                score += 5f; 
            }
            else
            {
                score += 250f; 
                if (controller.MyStats.HasFeat("Improved Bull Rush")) 
                {
                    score += 100f; 
                }
            }
        }
    }

    bool hasImprovedFeat = controller.MyStats.HasFeat("Improved Bull Rush");
    if (!hasImprovedFeat && AoOManager.Instance.IsThreatened(controller.MyStats)) score -= 60f;
    Score = score;
}

public override async Task Execute()
{
    bool hasImprovedFeat = controller.MyStats.HasFeat("Improved Bull Rush");
    if (!hasImprovedFeat)
    {
        await AoOManager.Instance.CheckAndResolve(controller.MyStats, ProvokingActionType.CombatManeuver, null, null);
        if (!GodotObject.IsInstanceValid(controller) || controller.MyStats.CurrentHP <= 0) return;
    }
    controller.MyActionManager.UseAction(ActionType.Standard);
    await CombatManeuvers.ResolveBullRushCoroutine(controller.MyStats, target);
    await controller.ToSignal(controller.GetTree().CreateTimer(1.0f), "timeout");
}
}
public class AIAction_InitiateGrapple : AIAction
{
private CreatureStats target;


public AIAction_InitiateGrapple(AIController controller, CreatureStats target) : base(controller)
{
    this.target = target;
    Name = $"Grapple {target.Name}";
}

public override void CalculateScore()
{
    if (isProne || controller.MyStats.Template.Intelligence < 6 || target.CurrentGrappleState != null) { Score = -1; return; }
    float score = 20f;
    if (target == CombatMemory.GetHighestThreat())
    {
        score += 80;
        if (target.Template.KnownAbilities.Any(a => a.SpellLevel > 0)) score += 50;
    }
    bool hasImprovedFeat = controller.MyStats.HasFeat("Improved Grapple");
    if (!hasImprovedFeat && AoOManager.Instance.IsThreatened(controller.MyStats)) score -= 80f;
    Score = score;
}

public override async Task Execute()
{
    bool hasImprovedFeat = controller.MyStats.HasFeat("Improved Grapple");
    if (!hasImprovedFeat)
    {
        await AoOManager.Instance.CheckAndResolve(controller.MyStats, ProvokingActionType.CombatManeuver, null, null);
        if (!GodotObject.IsInstanceValid(controller) || controller.MyStats.CurrentHP <= 0) return;
    }
    controller.MyActionManager.UseAction(ActionType.Standard);
    // Note: ResolveGrapple is async Task now in our conversion
	await CombatManeuvers.ResolveGrappleAsync(controller.MyStats, target, false, null);
    await controller.ToSignal(controller.GetTree().CreateTimer(1.0f), "timeout");
}
}

public class AIAction_MaintainGrapple : AIAction
{
private GrappleSubAction chosenSubAction;


public AIAction_MaintainGrapple(AIController controller) : base(controller) { Name = "Maintain Grapple"; }

public override void CalculateScore()
{
    var target = controller.MyStats.CurrentGrappleState.Target;
    float pinScore = 50;
    float damageScore = 40;
    if (target == CombatMemory.GetHighestThreat()) pinScore += 100;
    if (!target.GetNode<StatusEffectController>("StatusEffectController").HasCondition(Condition.Pinned))
    {
        chosenSubAction = GrappleSubAction.Pin;
        Score = pinScore;
        Name += " (Pin)";
    }
    else
    {
        chosenSubAction = GrappleSubAction.Damage;
        Score = damageScore;
        Name += " (Damage)";
    }
}

public override async Task Execute()
{
    controller.MyActionManager.UseAction(ActionType.Standard);
    CombatManeuvers.ResolveMaintainGrapple(controller.MyStats, chosenSubAction);
    await controller.ToSignal(controller.GetTree().CreateTimer(1.0f), "timeout");
}
}
public class AIAction_BreakGrapple : AIAction
{
public AIAction_BreakGrapple(AIController controller) : base(controller) { Name = "Break Grapple"; }
public override void CalculateScore() { Score = 500f; }
public override async Task Execute()
{
controller.MyActionManager.UseAction(ActionType.Standard);
CombatManeuvers.ResolveBreakGrapple(controller.MyStats);
await controller.ToSignal(controller.GetTree().CreateTimer(1.0f), "timeout");
}
}
public class AIAction_Feint : AIAction
{
private CreatureStats target;
private AIAction followupAttack;


public override CreatureStats GetTarget() => this.target;

public AIAction_Feint(AIController controller, CreatureStats target, AIAction followupAttack) : base(controller)
{
    this.target = target;
    this.followupAttack = followupAttack;
    Name = $"Feint against {target.Name}, then {followupAttack.Name}";
}

public override void CalculateScore()
{
    followupAttack.CalculateScore();
    if(followupAttack.Score <= 0)
    {
        Score = -1f;
        return;
    }

    int senseMotiveBonus = target.GetSkillBonus(SkillType.SenseMotive);
    int babWisDc = 10 + target.Template.BaseAttackBonus + target.WisModifier;
    int dc = Mathf.Max(senseMotiveBonus + 10, babWisDc);
    int bluffBonus = controller.MyStats.GetSkillBonus(SkillType.Bluff);
    var weapon = controller.MyStats.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
    if (weapon != null && weapon.HasDistractingFeature)
    {
        bluffBonus += 2;
    }
    float successChance = Mathf.Clamp((10.5f + bluffBonus - dc) / 20f, 0f, 1f);
    if (successChance < 0.35f) 
    {
        Score = -1f;
        return;
    }

    int dexBonusValue = Mathf.Max(0, target.DexModifier);
    float valueOfFeint = dexBonusValue * 15f;

    Score = followupAttack.Score + (valueOfFeint * successChance);
}

public override async Task Execute()
{
    controller.MyActionManager.UseAction(ActionType.Standard);
    
    int senseMotiveBonus = target.GetSkillBonus(SkillType.SenseMotive);
    int babWisDc = 10 + target.Template.BaseAttackBonus + target.WisModifier;
    int dc = Mathf.Max(senseMotiveBonus + 10, babWisDc);
	int bluffBonus = controller.MyStats.GetSkillBonus(SkillType.Bluff);
    var weapon = controller.MyStats.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
    if (weapon != null && weapon.HasDistractingFeature)
    {
        bluffBonus += 2;
    }
    int bluffRoll = Dice.Roll(1, 20) + bluffBonus;
    
    GD.Print($"{controller.GetParent().Name} attempts to Feint {target.Name}. Bluff: {bluffRoll} vs DC: {dc}");

    if (bluffRoll >= dc)
    {
        GD.PrintRich($"[color=green]Feint Successful![/color] {target.Name} is denied their Dex bonus to AC.");
        var feintedEffect = new StatusEffect_SO();
        feintedEffect.EffectName = "Feinted";
        feintedEffect.ConditionApplied = Condition.Feinted;
        feintedEffect.DurationInRounds = 1; 
        target.MyEffects.AddEffect(feintedEffect, controller.MyStats);
    }
    else
    {
        GD.PrintRich("<color=red>Feint Failed.</color>");
    }
    
    await controller.ToSignal(controller.GetTree().CreateTimer(1f), "timeout");
}
}
public class AIAction_AwesomeBlow : AIAction
{
private CreatureStats target;
private Ability_SO ability;


public AIAction_AwesomeBlow(AIController controller, CreatureStats target, Ability_SO ability) : base(controller)
{
    this.target = target;
    this.ability = ability;
    Name = $"Awesome Blow on {target.Name}";
}

public override void CalculateScore()
{
    var context = new EffectContext { Caster = controller.MyStats, PrimaryTarget = target };
    var effect = ability.EffectComponents.OfType<Effect_AwesomeBlow>().FirstOrDefault();
    if (effect != null)
    {
        Score = effect.GetAIEstimatedValue(context);
    }
    else
    {
        Score = 0f;
    }
}

public override async Task Execute()
{
    controller.MyActionManager.UseAction(ActionType.Standard);
    CombatManeuvers.ResolveAwesomeBlow(controller.MyStats, target);
    await controller.ToSignal(controller.GetTree().CreateTimer(1.5f), "timeout");
}
}