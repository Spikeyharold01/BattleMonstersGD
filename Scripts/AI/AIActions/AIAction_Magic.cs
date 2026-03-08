using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// =================================================================================================
// FILE: AIAction_Magic.cs (GODOT VERSION)
// PURPOSE: Magic and ability related AI actions.
// ATTACH TO: Do not attach (Pure C# Classes).
// =================================================================================================
public class AIAction_CastGenericAbility : AIAction
{
private Ability_SO ability;
private CreatureStats target;
private Vector3 aimPoint;
private bool isMythicCast;
private CommandWord chosenCommand = CommandWord.None;
private Node3D targetObject;

public override CreatureStats GetTarget() => this.target;

private Resource selectedResource;

public AIAction_CastGenericAbility(AIController controller, Ability_SO ability, CreatureStats target, Vector3 aimPoint, CommandWord command = CommandWord.None, bool isMythic = false, Resource resource = null, Node3D targetObject = null) : base(controller)
{
    this.targetObject = targetObject;
    this.ability = ability;
    this.selectedResource = resource;
    this.target = target;
    this.aimPoint = aimPoint;
    this.chosenCommand = command;
    this.isMythicCast = isMythic;

    string mythicTag = isMythic ? " (Mythic)" : "";
    string commandTag = (command != CommandWord.None) ? $" ({command})" : "";
    Name = $"Use {ability.AbilityName}{mythicTag}{commandTag}";
    if (target != null && target != controller.MyStats) Name += $" on {target.Name}";
}

public override void CalculateScore()
{
    float totalScore = 0;
    var context = new EffectContext
    {
        Caster = controller.MyStats,
        PrimaryTarget = this.target,
        AimPoint = this.aimPoint,
		Ability = this.ability,
        AllTargetsInAoE = new Godot.Collections.Array<CreatureStats>(),
        SelectedCommand = this.chosenCommand,
        IsMythicCast = this.isMythicCast,
        SelectedResource = this.selectedResource
    };

    if (ability.TargetType == TargetType.Area_EnemiesOnly || ability.TargetType == TargetType.Area_FriendOrFoe || ability.TargetType == TargetType.Area_AlliesOnly)
    {
        var targets = TurnManager.Instance.GetAllCombatants()
            .Where(t => aimPoint.DistanceTo(t.GlobalPosition) <= ability.AreaOfEffect.Range).ToList();
        foreach (var t in targets) context.AllTargetsInAoE.Add(t);
    }
    else if (target != null)
    {
        context.AllTargetsInAoE.Add(target);
    }

    if (context.AllTargetsInAoE.Count == 0 && target == null && ability.TargetType != TargetType.Self)
    {
        // Allow Self spells to proceed even if AoE count is 0 (though normally Self adds caster to AoE)
        // Dimension Door has special logic below, so we don't return -1 here for it.
        if (ability.AbilityName != "Dimension Door")
        {
            Score = -1f;
            return;
        }
    }

    foreach (var component in ability.EffectComponents)
    {
        if (component is CompelActionEffect compelEffect)
        {
            totalScore += compelEffect.GetAIEstimatedValueForCommand(context, this.chosenCommand);
        }
        else
        {
            totalScore += component.GetAIEstimatedValue(context);
        }
    }

    if (AoOManager.Instance.IsThreatened(controller.MyStats) && ability.SpellLevel > 0)
    {
        totalScore *= 0.6f;
    }

    if (isMythicCast)
    {
        foreach (var mythicComponent in ability.MythicComponents)
        {
            totalScore += 100f;
        }
    }

    if (ability.Range.GetRange(controller.MyStats) > 5f && target != null)
    {
        int meleePenalty = CombatAttacks.CalculateFiringIntoMeleePenalty(controller.MyStats, target);
        if (meleePenalty < 0)
        {
            totalScore *= 0.6f;
            Name += " (firing into melee)";
        }
    }

    if (controller.MyStats.MyEffects.HasCondition(Condition.Grappled))
    {
        var grappleState = controller.MyStats.CurrentGrappleState;
        if (grappleState != null)
        {
            var grappler = (grappleState.Controller == controller.MyStats) ? grappleState.Target : grappleState.Controller;

            int concentrationDC = 10 + grappler.GetCMB() + ability.SpellLevel;
            float averageConcentrationResult = 10.5f + controller.MyStats.GetConcentrationBonus();
            float chanceToSucceed = Mathf.Clamp((averageConcentrationResult - concentrationDC + 1) / 20f, 0f, 1f);

            totalScore *= chanceToSucceed;
            Name += $" (Grappled, {chanceToSucceed:P0} success chance)";
        }
    }

    var sampleTargetForSR = target ?? (context.AllTargetsInAoE.Count > 0 ? context.AllTargetsInAoE[0] : null);

    if (ability.AttackRollType != AttackRollType.None && sampleTargetForSR != null)
    {
        bool isTouchAttack = ability.AttackRollType == AttackRollType.Melee_Touch || ability.AttackRollType == AttackRollType.Ranged_Touch;

        int predictedAC = CombatCalculations.CalculateFinalAC(sampleTargetForSR, isTouchAttack, 0, controller.MyStats);
        int attackBonus = controller.MyStats.Template.BaseAttackBonus + controller.MyStats.StrModifier;

        int rollNeeded = predictedAC - attackBonus;
        float chanceToHit = Mathf.Clamp((21f - rollNeeded) / 20f, 0f, 1f);

        totalScore *= chanceToHit;
        if (isTouchAttack) Name += $" (vs Touch AC {predictedAC})";
    }

    if (sampleTargetForSR != null)
    {
        float srSuccessChance = controller.PredictSuccessChanceVsSR(sampleTargetForSR, ability);
        totalScore *= srSuccessChance;
        if (srSuccessChance < 1.0f) Name += $" (SR success: {srSuccessChance:P0})";
    }

    if (context.AllTargetsInAoE.Count > 0)
    {
        var damageComponent = ability.EffectComponents.OfType<DamageEffect>().FirstOrDefault();
        if (damageComponent != null)
        {
            string damageType = damageComponent.Damage.DamageType;
            foreach (var aoeTarget in context.AllTargetsInAoE)
            {
                string knownResistance = CombatMemory.GetKnownAdaptiveResistance(aoeTarget);
                if (knownResistance == damageType)
                {
                    totalScore *= 0.1f;
                    Name += $" (vs Adaptive Resist)";
                    break;
                }
            }
        }
    }

    HealthStatus targetStatus = CombatMemory.GetKnownHealthStatus(target);

    // Assuming School is accessible (It is in Ability_SO)
    // Note: Checking description for "mind-affecting" is brittle but matches source logic
    if (ability.School == MagicSchool.Enchantment || (ability.DescriptionForTooltip != null && ability.DescriptionForTooltip.Contains("mind-affecting")))
    {
        if (targetStatus == HealthStatus.Undead || targetStatus == HealthStatus.Construct)
        {
            totalScore = -1f;
            Name += " (Invalid Target Type)";
        }
    }

    bool? isSentient = CombatMemory.IsKnownToBeSentient(target);
    if (isSentient.HasValue)
    {
        if (isSentient.Value == false)
        {
            if (ability.IsLanguageDependent)
            {
                totalScore = -1f;
                Name += " (Target is Non-Sentient)";
            }
            if (ability.School == MagicSchool.Illusion && ability.AbilityName.Contains("Phantasm"))
            {
                totalScore = -1f;
                Name += " (Mindless/Animal)";
            }
        }
    }

    if (ability.AbilityName == "Dimension Door")
    {
        GridNode destinationNode = GridManager.Instance.NodeFromWorldPoint(aimPoint);
        if (destinationNode == null || destinationNode.terrainType == TerrainType.Solid)
        {
            totalScore = -1f;
            Name += " (invalid destination)";
        }
        else
        {
            bool knownDestination = LineOfSightManager.HasLineOfEffect(controller.MyStats, controller.MyStats.GlobalPosition + Vector3.Up, destinationNode.worldPosition + Vector3.Up * 0.5f);
            bool occupiedByEnemy = TurnManager.Instance.GetAllCombatants().Any(c =>
                c != controller.MyStats &&
                c.IsInGroup("Player") != controller.MyStats.IsInGroup("Player") &&
                GridManager.Instance.NodeFromWorldPoint(c.GlobalPosition) == destinationNode);

            if (occupiedByEnemy)
            {
                totalScore = -1f;
                Name += " (occupied by enemy)";
            }
            else
            {
                if (!knownDestination)
                {
                    totalScore -= 60f;
                    Name += " (risky destination)";
                }

                float distance = controller.MyStats.GlobalPosition.DistanceTo(destinationNode.worldPosition);
                totalScore += Mathf.Clamp(distance * 0.25f, 5f, 40f);
            }
        }
    }

    if (ability.AbilityName.Contains("Wind Walk"))
    {
        bool desperate = (float)controller.MyStats.CurrentHP / controller.MyStats.Template.MaxHP < 0.15f;
        if (desperate)
        {
            totalScore = 200f;
            Name += " (Desperate Escape)";
        }
        else
        {
            totalScore = -1f;
        }
    }
    Score = totalScore;
}

public override async Task Execute()
{
    bool wasInterrupted = false;
    if (ability.SpellLevel > 0)
    {
        await AoOManager.Instance.CheckAndResolve(
            controller.MyStats,
            ProvokingActionType.Spellcasting,
            ability,
            (success) => wasInterrupted = !success
        );
    }

    if (wasInterrupted || !GodotObject.IsInstanceValid(controller) || controller.MyStats.CurrentHP <= 0)
    {
        controller.MyActionManager.UseAction(ability.ActionCost);
        return;
    }

    if ((ability.TargetType == TargetType.SingleEnemy || ability.TargetType == TargetType.SingleAlly) && target == null)
    {
        controller.MyActionManager.UseAction(ability.ActionCost);
        return;
    }

    controller.MyActionManager.UseAction(ability.ActionCost);
    controller.MyStats.MyUsage?.ConsumeUse(ability);
    if (isMythicCast) controller.MyStats.ConsumeMythicPower();

    // TargetObject (GameObject) replacement -> Node3D. Target is CreatureStats (Node3D).
    await CombatManager.ResolveAbility(controller.MyStats, target, this.targetObject ?? target, aimPoint, ability, isMythicCast, chosenCommand);
    await controller.ToSignal(controller.GetTree().CreateTimer(1.5f), "timeout");
}
}
public class AIAction_Concentrate : AIAction
{
private bool canConcentrate = false;
private DetectMagicController detectMagic; // Keep for legacy support if needed

public AIAction_Concentrate(AIController controller) : base(controller)
{
    Name = "Concentrate";
    // Check for generic concentration condition (applied by Enthrall on Self)
    if (controller.MyStats.MyEffects.HasCondition(Condition.Concentrating))
    {
        canConcentrate = true;
        Name += " on Spell";
    }

    // Legacy check for Detect Magic controller
    detectMagic = controller.GetParent().GetNodeOrNull<DetectMagicController>("DetectMagicController");
    if (detectMagic != null) canConcentrate = true;
}

public override void CalculateScore()
{
    if (detectMagic == null && !canConcentrate)
    {
        Score = -1;
        return;
    }

    // Default base score for maintaining concentration on a generic spell
    Score = 50f;

    // Specific logic for Detect Magic scoring
    if (detectMagic != null)
    {
        var unknownMagicalTargets = controller.FindVisibleTargets()
            .Count(t => CombatMemory.IsKnownToBeMagical(t) && CombatMemory.GetKnownAuraStrength(t) < AuraStrength.Strong);

        if (unknownMagicalTargets > 0)
        {
            Score = 70f;
            Name += " (to identify potent auras)";
        }
        else
        {
            Score = 2f; // Low priority if no interesting magic
        }
    }
}

public override async Task Execute()
{
    controller.MyActionManager.UseAction(ActionType.Standard);

    var scanner = controller.GetParent().GetNodeOrNull<ConcentrationEffect_Scanner>("ConcentrationEffect_Scanner");
    if (scanner != null) scanner.Concentrate();

    if (detectMagic != null)
    {
        detectMagic.Concentrate();
    }
    else
    {
        // Generic concentration simply refreshes the duration of the "Concentrating" status effect
        // or assumes the StatusEffectController handles the logic.
        GD.Print($"{controller.GetParent().Name} concentrates to maintain the spell.");
    }
    await controller.ToSignal(controller.GetTree().CreateTimer(1.0f), "timeout");
}
}