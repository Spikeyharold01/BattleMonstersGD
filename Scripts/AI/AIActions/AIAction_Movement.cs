using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// =================================================================================================
// FILE: AIAction_Movement.cs (GODOT VERSION)
// PURPOSE: Movement-related AI actions (Move, Charge, Withdraw, etc.).
// ATTACH TO: Do not attach (Pure C# Classes).
// =================================================================================================
public class AIAction_MoveToPosition : AIAction
{
private Vector3 position;
private CreatureStats primaryTarget;
private bool useAcrobatics;
private bool fullSpeedAcrobatics;

public AIAction_MoveToPosition(AIController controller, Vector3 position, CreatureStats primaryTarget, bool useAcrobatics = false, bool fullSpeedAcrobatics = false) : base(controller)
{
    this.useAcrobatics = useAcrobatics;
    this.fullSpeedAcrobatics = fullSpeedAcrobatics;
    Name = "Move to position";
    if (useAcrobatics) Name = fullSpeedAcrobatics ? "Acrobatics (Full Speed) to position" : "Acrobatics (Half Speed) to position";
    this.position = position;
    this.primaryTarget = primaryTarget;
}

public override void CalculateScore()
{
    float score = 10f;
    score -= controller.GetParent<Node3D>().GlobalPosition.DistanceTo(position);

    if (useAcrobatics)
    {
        var threateners = AoOManager.Instance.GetThreateningCreatures(controller.MyStats);
        if (!threateners.Any()) { Score = -1; return; }

        int dc = threateners.Max(t => t.GetCMD()) + (threateners.Count - 1) * 2;
		 if (GridManager.Instance.NodeFromWorldPoint(controller.GetParent<Node3D>().GlobalPosition).terrainType == TerrainType.Ice)
        {
            dc += 5;
        }
        if (fullSpeedAcrobatics) dc += 10;
        
        float successChance = Mathf.Clamp((10.5f + controller.MyStats.GetSkillBonus(SkillType.Acrobatics) - dc) / 20f, 0f, 1f);
        
        if (successChance < 0.3f) { Score = -1; return; }
        score += 80f * threateners.Count * successChance; 
    }
    else if (AoOManager.Instance.IsThreatened(controller.MyStats))
    {
        score -= 80f; 
    }

    if (isProne) { score = -1; Name += " (Invalid: Prone)"; }
	
    if (controller.MyStats.Template.HasScent && primaryTarget != null)
    {
        var visibility = LineOfSightManager.GetVisibility(controller.MyStats, primaryTarget);
        if (!visibility.HasLineOfSight)
        {
            Vector3 windDir = WeatherManager.Instance.CurrentWindDirection;
            Vector3 moveDir = (position - controller.GetParent<Node3D>().GlobalPosition).Normalized();
            
            if (moveDir.Dot(-windDir) > 0.5f)
            {
                score += 100f;
                Name += " (Tracking Scent Upwind)";
            }
        }
    }
    Score = score;
}

public override async Task Execute()
{
    Vector3 start = controller.GetParent<Node3D>().GlobalPosition;
    Vector3 moveDirection = (position - start).Normalized();
    Vector3 adjustedDirection = BoundaryEvaluator.BuildEdgeAwareDirection(controller.MyStats, moveDirection, intentIsFlee: false, moraleBroken: false, disengageActionDeclared: false);
    Vector3 adjustedDestination = start + adjustedDirection * start.DistanceTo(position);

    controller.MyActionManager.UseAction(ActionType.Move, controller.GetParent<Node3D>().GlobalPosition.DistanceTo(adjustedDestination));
    if (useAcrobatics)
    {
        await controller.MyMover.AcrobaticMoveAsync(adjustedDestination, fullSpeedAcrobatics);
    }
    else
    {
        bool isFlying = controller.MyStats.Template.Speed_Fly > 0 && GridManager.Instance.NodeFromWorldPoint(controller.GetParent<Node3D>().GlobalPosition).terrainType != TerrainType.Ground;
        await controller.MyMover.MoveToAsync(adjustedDestination, isFlying);
    }
}
}
public class AIAction_MoveToFlank : AIAction
{
private Vector3 destination;
private CreatureStats target;
private bool useAcrobatics;
private bool fullSpeedAcrobatics;


public AIAction_MoveToFlank(AIController controller, Vector3 position, CreatureStats target, CreatureStats ally, bool useAcrobatics = false, bool fullSpeedAcrobatics = false) : base(controller)
{
    this.useAcrobatics = useAcrobatics;
    this.fullSpeedAcrobatics = fullSpeedAcrobatics;
    this.destination = position;
    this.target = target;
    Name = useAcrobatics ? $"Acrobatics to Flank {target.Name}" : $"Move to Flank {target.Name}";
    if(useAcrobatics) Name += fullSpeedAcrobatics ? " (Full Speed)" : " (Half Speed)";
}

public override void CalculateScore()
{
    if (isProne) { Score = -1; return; }
    float score = tactics.W_AchieveFlank * ((float)controller.MyStats.Template.Intelligence / 10f);
    if (target == CombatMemory.GetHighestThreat()) score += tactics.W_TargetHighestThreat * 0.5f;

    if (useAcrobatics)
    {
        var threateners = AoOManager.Instance.GetThreateningCreatures(controller.MyStats);
        if (!threateners.Any()) { Score = -1; return; }

        int dc = threateners.Max(t => t.GetCMD()) + (threateners.Count - 1) * 2;
        if (fullSpeedAcrobatics) dc += 10;
        
        float successChance = Mathf.Clamp((10.5f + controller.MyStats.GetSkillBonus(SkillType.Acrobatics) - dc) / 20f, 0f, 1f);
        
        if (successChance < 0.3f) { Score = -1; return; }
        score += 80f * threateners.Count * successChance;
    } 
    else if (AoOManager.Instance.IsThreatened(controller.MyStats))
    {
         score -= 80f;
    }
    Score = Mathf.Max(0, score);
}

public override async Task Execute()
{
    controller.MyActionManager.UseAction(ActionType.Move, controller.GetParent<Node3D>().GlobalPosition.DistanceTo(destination));
    if (useAcrobatics)
    {
        await controller.MyMover.AcrobaticMoveAsync(destination, fullSpeedAcrobatics);
    }
    else
    {
        bool isFlying = controller.MyStats.Template.Speed_Fly > 0 && GridManager.Instance.NodeFromWorldPoint(controller.GetParent<Node3D>().GlobalPosition).terrainType != TerrainType.Ground;
        await controller.MyMover.MoveToAsync(destination, isFlying);
    }
}
}
public class AIAction_FiveFootStep : AIAction
{
private Vector3 destination;


public AIAction_FiveFootStep(AIController controller, Vector3 destination) : base(controller)
{
    this.destination = destination;
    Name = $"5-Foot Step to {destination}";
}

public override void CalculateScore()
{
    float score = 0;
    bool isCurrentlyThreatened = AoOManager.Instance.IsThreatened(controller.MyStats);
    
    var tempGhost = new CreatureStats();
    tempGhost.GlobalPosition = destination;
    tempGhost.Template = controller.MyStats.Template;
    
    // Need to add to tree for physics checks if LoS/AoO uses Raycasts?
    // AoOManager uses GetThreateningCreatures -> Distance/Reach.
    // Pure distance check doesn't need physics if reach is just float.
    // Assuming simple check. If physics needed, this might fail without AddChild.
    // Assuming simple check for now based on AoOManager implementation.
    
    bool isDestinationSafe = !AoOManager.Instance.IsThreatened(tempGhost);
    tempGhost.QueueFree();

    if (isCurrentlyThreatened && isDestinationSafe)
    {
        score += 150f;
        Name += " (to safety)";
        bool hasProvokingAction = controller.MyStats.Template.KnownAbilities.Any(a => a.SpellLevel > 0);
        if (hasProvokingAction)
        {
            score += 100f * (profile.W_Strategic / 100f);
            Name += " (to enable spell)";
        }
    }
    
    var reach = controller.MyStats.GetEffectiveReach((Item_SO)null);
    bool canAttackFromDest = AISpatialAnalysis.FindVisibleTargets(controller.MyStats).Any(t => destination.DistanceTo(t.GlobalPosition) <= reach.max);
    bool canAttackFromCurr = AISpatialAnalysis.FindVisibleTargets(controller.MyStats).Any(t => controller.GetParent<Node3D>().GlobalPosition.DistanceTo(t.GlobalPosition) <= reach.max);

    if (canAttackFromDest && !canAttackFromCurr)
    {
        score += 80f;
        Name += " (to engage for full-attack)";
    }
    Score = score;
}

public override async Task Execute()
{
    await controller.MyMover.FiveFootStepAsync(destination);
    controller.MyActionManager.UseAction(ActionType.FiveFootStep);
}
}
public class AIAction_Withdraw : AIAction
{
private Vector3 destination;


public AIAction_Withdraw(AIController controller, Vector3 destination) : base(controller)
{
    this.destination = destination;
    Name = $"Withdraw to {destination}";
}

public override void CalculateScore()
{
    if (controller.MyStats.MyEffects.HasCondition(Condition.Blinded))
    {
        Score = -1;
        return;
    }
    float score = 0;
    if (AoOManager.Instance.IsThreatened(controller.MyStats))
    {
        float healthPercent = controller.MyStats.CurrentHP / (float)controller.MyStats.Template.MaxHP;
        if (healthPercent < 0.6f)
        {
            score = (1 - healthPercent) * (tactics.W_GoDefensiveWhenLowHP + 50f);
            Name += $" (Low HP: {healthPercent:P0})";
        }
    }
    else
    {
        score = -1;
    }
    Score = score;
}

public override async Task Execute()
{
    controller.MyActionManager.UseAction(ActionType.FullRound, controller.GetParent<Node3D>().GlobalPosition.DistanceTo(destination));
    await controller.MyMover.WithdrawToAsync(destination);
}
}
public class AIAction_Flee : AIAction
{
private Vector3 fleeDestination;


public AIAction_Flee(AIController controller) : base(controller) { Name = "Flee"; }

public override void CalculateScore()
{
    Score = 1000f;
Vector3? safeFleeDestination = AISpatialAnalysis.FindBestFleePosition(controller.MyStats, controller.MyMover);
    if (!safeFleeDestination.HasValue)
    {
        Score = -1;
        return;
    }

    fleeDestination = safeFleeDestination.Value;
}
public override async Task Execute()
{
GD.PrintRich($"[color=orange]{controller.GetParent().Name} is frightened and flees![/color]");
controller.MyActionManager.UseAction(ActionType.FullRound);

Vector3 start = controller.GetParent<Node3D>().GlobalPosition;
Vector3 fleeDirection = (fleeDestination - start).Normalized();
Vector3 adjustedDirection = BoundaryEvaluator.BuildEdgeAwareDirection(controller.MyStats, fleeDirection, intentIsFlee: true, moraleBroken: true, disengageActionDeclared: false);
Vector3 adjustedDestination = start + adjustedDirection * start.DistanceTo(fleeDestination);

await controller.MyMover.RunAsync(adjustedDestination);
}
}
public class AIAction_Hover : AIAction
{
public AIAction_Hover(AIController controller) : base(controller)
{
Name = "Hover";
}


public override void CalculateScore()
{
    bool isInGoodPosition = AISpatialAnalysis.FindVisibleTargets(controller.MyStats).Any(t => controller.GetParent<Node3D>().GlobalPosition.DistanceTo(t.GlobalPosition) <= controller.MyStats.GetEffectiveReach((Item_SO)null).max);
    
    if (isInGoodPosition)
    {
        Score = 30f;
        Name += " (to maintain position for full attack)";
    }
    else
    {
        Score = -1f; 
    }
    
    float successChance = Mathf.Clamp((10.5f + controller.MyStats.GetSkillBonus(SkillType.Fly) - 15) / 20f, 0f, 1f);
    Score *= successChance;
}

public override async Task Execute()
{
    controller.MyActionManager.UseAction(ActionType.Move);
    await controller.MyMover.HoverAsync();
}
}
public class AIAction_Jump : AIAction
{
private Vector3 destination;
private float distance;
private bool isHighJump;


public AIAction_Jump(AIController controller, Vector3 destination, float distance, bool isHighJump) : base(controller)
{
    this.destination = destination;
    this.distance = distance;
    this.isHighJump = isHighJump;
    Name = isHighJump ? $"High Jump ({distance}ft)" : $"Long Jump ({distance}ft)";
}

public override void CalculateScore()
{
    float score = 0;
    int dc = isHighJump ? Mathf.RoundToInt(distance * 4) : Mathf.RoundToInt(distance);
    
    float successChance = Mathf.Clamp((10.5f + controller.MyStats.GetSkillBonus(SkillType.Acrobatics) - dc) / 20f, 0f, 1f);
    if (successChance < 0.5f) 
    {
        Score = -1f;
        return;
    }

    // Check safety of destination (simulated)
    // AoOManager.IsThreatened check logic requires Node/Position context ideally.
    // Assuming AoOManager overload for position exists or we create dummy.
    // AoOManager.IsThreatenedAtPosition logic:
    
    var dummy = new CreatureStats();
    dummy.GlobalPosition = destination;
    dummy.Template = controller.MyStats.Template;
    
    bool isDestinationSafe = !AoOManager.Instance.IsThreatened(dummy);
    dummy.QueueFree();
    
    if (isDestinationSafe && AoOManager.Instance.IsThreatened(controller.MyStats))
    {
        score += 100f; 
        Name += " (to safety)";
    }
    
    if(destination.Y > controller.GetParent<Node3D>().GlobalPosition.Y)
    {
        score += 50f;
        Name += " (for high ground)";
    }

    Score = score * successChance;
}

public override async Task Execute()
{
    controller.MyActionManager.UseAction(ActionType.Move);
    await controller.MyMover.MoveToAsync(destination);
}
}
public class AIAction_Climb : AIAction
{
private Vector3 destination;


public AIAction_Climb(AIController controller, Vector3 destination) : base(controller)
{
    this.destination = destination;
    Name = $"Climb to {destination}";
}

public override void CalculateScore()
{
    float score = 0;
    GridNode startNode = GridManager.Instance.NodeFromWorldPoint(controller.GetParent<Node3D>().GlobalPosition);
    GridNode endNode = GridManager.Instance.NodeFromWorldPoint(destination);

    bool canAttackFromCurrent = AISpatialAnalysis.FindVisibleTargets(controller.MyStats).Any(t => startNode.worldPosition.DistanceTo(t.GlobalPosition) <= controller.MyStats.GetEffectiveReach((Item_SO)null).max);
    bool canAttackFromDest = AISpatialAnalysis.FindVisibleTargets(controller.MyStats).Any(t => endNode.worldPosition.DistanceTo(t.GlobalPosition) <= controller.MyStats.GetEffectiveReach((Item_SO)null).max);

    if (!canAttackFromCurrent && canAttackFromDest)
    {
        score += 150f;
        Name += " (to engage)";
    }

    if (endNode.gridY > startNode.gridY)
    {
        score += 40f;
    }

    Score = score;
}

public override async Task Execute()
{
    controller.MyActionManager.UseAction(ActionType.Move, controller.GetParent<Node3D>().GlobalPosition.DistanceTo(destination));
    await controller.MyMover.MoveToAsync(destination);
}
}
public class AIAction_Charge : AIAction
{
private CreatureStats target;
private List<Vector3> chargePath;


public AIAction_Charge(AIController controller, CreatureStats target) : base(controller)
{
    this.target = target;
    Name = $"Charge {target.Name}";
}

public override void CalculateScore()
{
    if (controller.MyStats.MyEffects.HasCondition(Condition.Blinded))
    {
        Score = -1;
        Name += " (Invalid: Blinded)";
        return;
    }
    chargePath = Pathfinding.Instance.FindChargePath(controller.MyStats, controller.GetParent<Node3D>().GlobalPosition, target);
    if (chargePath == null)
    {
        Score = -1;
        return;
    }
    float score = 80f;
	// --- NEW: HASTE SCORING BOOST ---
    bool hasHastePounce = controller.MyStats.Template.SpecialAttacks.Any(a => a.AbilityName.Contains("Haste", System.StringComparison.OrdinalIgnoreCase)) ||
                          controller.MyStats.Template.SpecialQualities.Contains("Pounce");
    if (hasHastePounce)
    {
        score += 100f; // Significant boost for full attack potential
        Name += " (Haste Full Attack)";
    }
    // --------------------------------
    if (target == CombatMemory.GetHighestThreat()) score += 40f;
    score += controller.GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition) * 1.5f;
    float healthPercent = controller.MyStats.CurrentHP / (float)controller.MyStats.Template.MaxHP;
    if (healthPercent < 0.5f) score *= healthPercent;
    Score = score;
}

public override async Task Execute()
{
    if (chargePath == null || !chargePath.Any()) return;
    controller.MyActionManager.UseAction(ActionType.FullRound, (chargePath.Count * 5f));
	        float currentSpeed = controller.MyMover.GetEffectiveMovementSpeed();
        if ((chargePath.Count * 5f) > currentSpeed * 2f)
        {
            var sprintAbility = controller.MyStats.Template.KnownAbilities.FirstOrDefault(a => a.AbilityName.Contains("Sprint"));
            if (sprintAbility != null)
            {
                GD.PrintRich($"[color=cyan]{controller.MyStats.Name} triggers Sprint to close the distance![/color]");
                controller.MyStats.GetNodeOrNull<AbilityCooldownController>("AbilityCooldownController")?.PutOnCooldown(sprintAbility);
                controller.MyStats.MyUsage?.ConsumeUse(sprintAbility);
                var sprintEffect = sprintAbility.EffectComponents.OfType<ApplyStatusEffect>().FirstOrDefault()?.EffectToApply;
                if (sprintEffect != null) controller.MyStats.MyEffects.AddEffect((StatusEffect_SO)sprintEffect.Duplicate(), controller.MyStats);
            }
        }
    
    var chargingEffect = new StatusEffect_SO();
    chargingEffect.EffectName = "Charging";
    chargingEffect.DurationInRounds = 1;
    chargingEffect.ConditionApplied = Condition.Charging;
    controller.MyStats.MyEffects.AddEffect(chargingEffect, controller.MyStats);

    await controller.MyMover.MoveAlongPathAsync(chargePath);
    
    if (GodotObject.IsInstanceValid(controller) && controller.MyStats.CurrentHP > 0)
    {
        CombatManager.ResolveChargeAttack(controller.MyStats, target);
    }
}
}
// Dependen