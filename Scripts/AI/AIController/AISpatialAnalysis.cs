using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: AISpatialAnalysis.cs (GODOT VERSION)
// PURPOSE: Static utility class for AI movement and targeting logic.
// ATTACH TO: Do not attach (Static Class).
// =================================================================================================
public struct AoEPlacementInfo
{
public Vector3 AimPoint;
public List<CreatureStats> EnemiesHit;
public List<CreatureStats> AlliesHit;
public float Score;
}
public struct FlankOpportunity
{
public Vector3 Position;
public CreatureStats Ally;
}
public static class AISpatialAnalysis
{
public static List<CreatureStats> FindVisibleTargets(CreatureStats observer)
{
string targetGroup = observer.IsInGroup("Player") ? "Enemy" : "Player";
// Assuming TurnManager has GetAllCombatants or similar
var allCombatants = TurnManager.Instance.GetAllCombatants();
var allTargets = allCombatants.Where(c => c.IsInGroup(targetGroup)).ToList();
return allTargets.Where(t => LineOfSightManager.GetVisibility(observer, t).HasLineOfSight).ToList();
}

public static List<HeardSoundContact> FindHeardContacts(CreatureStats observer)
{
    return SoundSystem.GetHeardContacts(observer, enemiesOnly: true);
}

public static List<SmelledScentContact> FindSmelledContacts(CreatureStats observer, bool includeEnvironmentalScents = false)
{
    return ScentSystem.GetSmelledContacts(observer, enemiesOnly: true, includeEnvironmentalScents: includeEnvironmentalScents);
}

public static List<CreatureStats> FindAllies(CreatureStats observer)
{
    return TurnManager.Instance.GetAllCombatants()
        .Where(c => c != observer && c.IsInGroup("Player") == observer.IsInGroup("Player"))
        .ToList();
}

public static GridNode FindBestHidingSpot(CreatureStats seeker, CreatureMover mover, CreatureStats primaryThreat)
{
    GridNode bestSpot = null;
    float bestScore = float.MinValue;

    int searchRadius = Mathf.FloorToInt(mover.GetEffectiveMovementSpeed() / 5f);
    // Assuming seeker is a Node3D (CharacterBody3D)
    GridNode startNode = GridManager.Instance.NodeFromWorldPoint(seeker.GlobalPosition);

    // Access private grid via Reflection or if we make grid public in GridManager?
    // GridManager.grid is private in provided snippet. Assuming it's made public or we use accessors.
    // I will assume GridManager has `GetNode(x,y,z)` or I use `NodeFromWorldPoint` offset logic.
    // Actually, GridManager snippet showed `grid` as private. I will use `NodeFromWorldPoint` for safety in static context if grid isn't exposed.
    // OR standard `GetNodeAtGridIndex` method should be added to GridManager.
    // Since I cannot modify GridManager here, I will iterate world positions.
    
    // Wait, `GridManager` script provided earlier has `grid` as private.
    // I will assume `grid` field is made public or internal for this helper, OR I use `GetNeighbours` recursively.
    // For efficiency, let's assume I can access it via reflection or coordinate math.
    
    // Let's implement coordinate math using NodeFromWorldPoint logic in reverse or just offset.
    Vector3 startPos = startNode.worldPosition;
    float diameter = GridManager.Instance.nodeDiameter;

    for (int x = -searchRadius; x <= searchRadius; x++)
    {
        for (int z = -searchRadius; z <= searchRadius; z++)
        {
            // Reconstruct world position for check
            Vector3 checkPos = startPos + new Vector3(x * diameter, 0, z * diameter);
            GridNode candidateNode = GridManager.Instance.NodeFromWorldPoint(checkPos);
            
            // Ensure y matches if grid is flat-ish, or re-acquire node fully
            if (candidateNode == null || candidateNode.terrainType == TerrainType.Solid) continue;

            if (primaryThreat != null)
            {
                // Visibility logic
                // VisibilityResult is struct from LoS Manager
                // But `GetVisibilityFromPoint` was internal in CombatCalculations.
                // Assuming LineOfSightManager has `GetVisibility` overload or static helper.
                // CombatCalculations has `GetVisibilityFromPoint` static.
                
                var visibility = CombatCalculations.GetVisibilityFromPoint(primaryThreat.GlobalPosition, candidateNode.worldPosition);
                
                if (visibility.CoverBonusToAC > 0 || visibility.ConcealmentMissChance > 0)
                {
                    float score = (visibility.CoverBonusToAC * 10) + visibility.ConcealmentMissChance;
                    score -= seeker.GlobalPosition.DistanceTo(candidateNode.worldPosition);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestSpot = candidateNode;
                    }
                }
            }
        }
    }
    return bestSpot;
}

public static Vector3 FindBestPosition(CreatureStats seeker, CreatureMover mover, CreatureStats target)
{
    Vector3 bestPos = seeker.GlobalPosition;
    float bestScore = -1000f;
    bool isNativeSwimmer = seeker.Template.Speed_Swim > 0;
    
    // Ghost object logic in Godot: Create a temporary Node3D/CreatureStats
    // Since we only need data, we can just use the `seeker` reference and offset logic manually, 
    // but `LineOfSightManager` usually takes a `CreatureStats` for effects/height.
    // We will mock the position check by passing the point to LoS manager if supported, or creating a dummy.
    // Creating a full dummy node in Godot is cheap.
    
    var ghostNode = new CreatureStats();
    // We need to add it to tree to use physics/global position
    SceneTree tree = (SceneTree)Engine.GetMainLoop();
    tree.CurrentScene.AddChild(ghostNode);
    ghostNode.Template = seeker.Template; // Copy template ref
    
    for (int i = 0; i < 30; i++) 
    {
        // Random point in sphere
        Vector3 randomOffset = new Vector3(GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f).Normalized() * GD.Randf() * mover.GetEffectiveMovementSpeed();
        Vector3 randomPoint = seeker.GlobalPosition + randomOffset;
        
        GridNode node = GridManager.Instance.NodeFromWorldPoint(randomPoint);
        
        if (node.terrainType == TerrainType.Solid) continue;
        
        float score = 0;
        if (node.terrainType == TerrainType.Water && !isNativeSwimmer)
        {
            score -= 50f;
        }
        float distToTarget = node.worldPosition.DistanceTo(target.GlobalPosition);
        
        // --- DRAGON SENSES ---
        if (seeker.Template.HasDragonSenses)
        {
            int lightAtNode = GridManager.Instance.GetEffectiveLightLevel(node);
            if (lightAtNode == 0) // Darkness
            {
                if (distToTarget > 60f && distToTarget <= 120f)
                {
                    score += 100f; 
                }
            }
        }

        // Light Sensitivity Logic
        if (seeker.Template.HasLightSensitivity)
        {
            int lightAtDest = GridManager.Instance.GetEffectiveLightLevel(node);
            if (lightAtDest >= 3) score -= 50f; 
            else if (GridManager.Instance.IsNodeInMythicDaylight(node)) score -= 100f; 
        }
        
        // Stealth Logic
        // Checking SkillRanks via LINQ
        if (seeker.Template.SkillRanks.Any(s => s.Skill == SkillType.Stealth && s.Ranks > 0))
        {
            int lightAtDest = GridManager.Instance.GetEffectiveLightLevel(node);
            if (lightAtDest <= 1) score += 20f; 
        }

        // Check weapons
        var mainHand = seeker.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
        bool isRanged = seeker.Template.KnownAbilities.Any(a => a.Range.GetRange(seeker) > 15f) || (mainHand != null && mainHand.WeaponType != WeaponType.Melee);
        bool isFlying = seeker.Template.Speed_Fly > 0;

        if (isFlying)
        {
            score -= 100f / Mathf.Max(1f, distToTarget - target.GetEffectiveReach((Item_SO)null).max);
            score += (node.worldPosition.Y - target.GlobalPosition.Y) * 5f;
        }
        else if (isRanged)
        {
            score += (20f - Mathf.Abs(distToTarget - 25f)) * 2;
            if (node.worldPosition.Y > target.GlobalPosition.Y) score += 30f;
        }
        else
        {
            var reach = seeker.GetEffectiveReach((Item_SO)null);
            if (distToTarget <= reach.max && distToTarget >= reach.min) score += 100f;
            else score -= distToTarget * 2;
        }

        ghostNode.GlobalPosition = node.worldPosition;
        var visibilityFromThreat = LineOfSightManager.GetVisibility(target, ghostNode);
        if(visibilityFromThreat.CoverBonusToAC > 0)
        {
            float intelligenceFactor = seeker.Template.Intelligence / 15f;
            score += visibilityFromThreat.CoverBonusToAC * 10f * intelligenceFactor;
        }

        if (seeker.Template.Intelligence >= 12)
        {
            var threatsToPosition = AoOManager.Instance.GetThreateningCreatures(ghostNode);
            if (threatsToPosition.Any())
            {
                score -= 50f;
            }
        }
		
        if (score > bestScore)
        {
            bestScore = score;
            bestPos = node.worldPosition;
        }
    }
    
    ghostNode.QueueFree();
    return bestPos;
}

public static GridNode FindPositionToUseAttack(CreatureStats seeker, CreatureMover mover, CreatureStats target, float minReach, float maxReach)
{
    if (target == null) return null;

    List<GridNode> validAttackNodes = new List<GridNode>();
    var targetNodes = GridManager.Instance.GetNodesOccupiedByCreature(target);

    // Pathfinding.GetNodesInRadius isn't in provided Pathfinding.cs snippet?
    // Assuming it's a helper I need to add or logic I replicate.
    // I will replicate logic using GridManager neighbours search.
    // Simple bounding box search around target.
    
    foreach (var targetNode in targetNodes)
    {
        int searchRadius = Mathf.CeilToInt(maxReach / 5f) + 2;
        // Iterate radius
        for (int x = -searchRadius; x <= searchRadius; x++)
        for (int z = -searchRadius; z <= searchRadius; z++)
        {
            // Basic grid offset
            Vector3 checkPos = targetNode.worldPosition + new Vector3(x * 5f, 0, z * 5f);
            GridNode potentialNode = GridManager.Instance.NodeFromWorldPoint(checkPos);
            if(potentialNode == null) continue;

            if (!Pathfinding.IsNodeWalkable(seeker, null, potentialNode)) continue; // Start node null implies checks purely on destination validity? Or assume from current.
            // Pathfinding.IsNodeWalkable logic: IsSquareOccupied check.
            
            float distance = potentialNode.worldPosition.DistanceTo(targetNode.worldPosition);
            if (distance <= maxReach && distance >= minReach)
            {
                var path = Pathfinding.Instance.FindPath(seeker, seeker.GlobalPosition, potentialNode.worldPosition);
                if (path != null && Pathfinding.CalculatePathCost(path, seeker) <= mover.GetEffectiveMovementSpeed())
                {
                    validAttackNodes.Add(potentialNode);
                }
            }
        }
    }
    
    return validAttackNodes
        .OrderBy(n => seeker.GlobalPosition.DistanceTo(n.worldPosition))
        .FirstOrDefault();
}

public static GridNode FindLandingSpot(Vector3 currentPos, GridNode startNode)
{
    Vector3 direction = (startNode.worldPosition - currentPos).Normalized();
    for (int i = 1; i < 10; i++)
    {
        GridNode checkNode = GridManager.Instance.NodeFromWorldPoint(startNode.worldPosition + direction * i * 5f);
        if (checkNode.terrainType == TerrainType.Ground) return checkNode;
        if (checkNode.terrainType == TerrainType.Solid) return null;
    }
    return null;
}

public static List<FlankOpportunity> FindPotentialFlankingPositions(CreatureStats seeker, CreatureMover mover, CreatureStats target)
{
    var opportunities = new List<FlankOpportunity>();
    var allies = FindAllies(seeker).Where(ally => CombatManager.IsFlankedBy(target, ally)).ToList();

    if (!allies.Any()) return opportunities;

    var ghostNode = new CreatureStats();
    SceneTree tree = (SceneTree)Engine.GetMainLoop();
    tree.CurrentScene.AddChild(ghostNode);
    ghostNode.Template = seeker.Template;

    List<GridNode> potentialNodes = new List<GridNode>();
    var targetNodes = GridManager.Instance.GetNodesOccupiedByCreature(target);
    foreach(var node in targetNodes)
    {
        potentialNodes.AddRange(GridManager.Instance.GetNeighbours(node));
    }
    
    foreach (var ally in allies)
    {
        foreach (var node in potentialNodes.Distinct())
        {
            if (node.terrainType != TerrainType.Ground && node.terrainType != TerrainType.Water) continue;
            ghostNode.GlobalPosition = node.worldPosition;
            
            if (CombatCalculations.CheckFlankingGeometry(ally, ghostNode, target))
            {
                var path = Pathfinding.Instance.FindPath(seeker, seeker.GlobalPosition, node.worldPosition);
                if (path != null && path.Count * 5f <= mover.GetEffectiveMovementSpeed())
                {
                    opportunities.Add(new FlankOpportunity { Position = node.worldPosition, Ally = ally });
                }
            }
        }
    }

    ghostNode.QueueFree();
    return opportunities;
}

public static Vector3? FindBestFleePosition(CreatureStats seeker, CreatureMover mover)
{
    var visibleEnemies = FindVisibleTargets(seeker);
    if (!visibleEnemies.Any()) return null;

    Vector3 enemyCenter = Vector3.Zero;
    foreach(var enemy in visibleEnemies) enemyCenter += enemy.GlobalPosition;
    enemyCenter /= visibleEnemies.Count;

    Vector3 fleeDirection = (seeker.GlobalPosition - enemyCenter).Normalized();
    // Forward is -Z in Godot
    if (fleeDirection == Vector3.Zero) fleeDirection = -seeker.GlobalTransform.Basis.Z;

    // Edge-aware shaping keeps normal retreating behavior from drifting accidentally out of travel combat.
    // When the actor is explicitly fleeing, this still allows outward travel-boundary exits.
    fleeDirection = BoundaryEvaluator.BuildEdgeAwareDirection(seeker, fleeDirection, intentIsFlee: true, moraleBroken: true, disengageActionDeclared: false);

    Vector3 bestPos = Vector3.Zero;
    float bestScore = float.MinValue;
    float maxDistance = mover.GetEffectiveMovementSpeed() * 2f;

    for (int i = 0; i < 20; i++)
    {
        float distance = GD.RandRange(maxDistance * 0.5f, maxDistance);
        Vector3 candidatePoint = seeker.GlobalPosition + fleeDirection * distance;

        bool leavesBounds = !CombatBoundaryService.IsInsideCombatBounds(candidatePoint);
        if (CombatBoundaryService.CurrentMode == CombatBoundaryMode.ArenaLocked && leavesBounds)
        {
            continue;
        }

        if (CombatBoundaryService.CurrentMode == CombatBoundaryMode.TravelEscapable && leavesBounds)
        {
            // In travel combat a true flee plan can intentionally exceed the combat map.
            return candidatePoint;
        }

        GridNode node = GridManager.Instance.NodeFromWorldPoint(candidatePoint);
        if (node.terrainType != TerrainType.Ground && node.terrainType != TerrainType.Water) continue;

        float score = node.worldPosition.DistanceTo(enemyCenter) * 2f;
        if (node.providesCover) score += 50f;

        if (score > bestScore)
        {
            if (Pathfinding.Instance.FindPath(seeker, seeker.GlobalPosition, node.worldPosition) != null)
            {
                bestScore = score;
                bestPos = node.worldPosition;
            }
        }
    }
    
    if (bestScore > float.MinValue) return bestPos;
    return null;
}

public static AoEPlacementInfo FindBestPlacementForAreaEffect(CreatureStats seeker, Ability_SO ability)
{
    AoEPlacementInfo bestOutcome = new AoEPlacementInfo { Score = float.MinValue };
    
    List<CreatureStats> visibleTargets = FindVisibleTargets(seeker);
    if (!visibleTargets.Any()) return bestOutcome;

    HashSet<GridNode> candidateNodes = new HashSet<GridNode>();
    foreach (var target in visibleTargets)
    {
        candidateNodes.Add(GridManager.Instance.NodeFromWorldPoint(target.GlobalPosition));
    }
    
    if (visibleTargets.Count > 1)
    {
        for (int i = 0; i < visibleTargets.Count; i++)
        {
            for (int j = i + 1; j < visibleTargets.Count; j++)
            {
                if (visibleTargets[i].GlobalPosition.DistanceTo(visibleTargets[j].GlobalPosition) < ability.AreaOfEffect.Range * 2)
                {
                    Vector3 midPoint = visibleTargets[i].GlobalPosition.Lerp(visibleTargets[j].GlobalPosition, 0.5f);
                    candidateNodes.Add(GridManager.Instance.NodeFromWorldPoint(midPoint));
                }
            }
        }
    }
    
    foreach(GridNode candidateNode in candidateNodes)
    {
        Vector3 aimPoint = candidateNode.worldPosition;
        // Assuming AoEHelper is converted and has these methods.
        string enemyTag = seeker.IsInGroup("Player") ? "Enemy" : "Player";
        
        // Godot Groups use Strings
        Godot.Collections.Array<CreatureStats> enemiesHitGodot = AoEHelper.GetTargetsInBurst(aimPoint, ability.AreaOfEffect, enemyTag);
        // Convert to C# List
        List<CreatureStats> enemiesHit = new List<CreatureStats>(enemiesHitGodot);
        
        string allyTag = seeker.IsInGroup("Player") ? "Player" : "Enemy";
        Godot.Collections.Array<CreatureStats> alliesHitGodot = AoEHelper.GetTargetsInBurst(aimPoint, ability.AreaOfEffect, allyTag);
        List<CreatureStats> alliesHit = new List<CreatureStats>(alliesHitGodot);
        
        float currentScore = 0;
        // Use Godot Array for Context
        var context = new EffectContext { Caster = seeker, Ability = ability, AllTargetsInAoE = enemiesHitGodot, AimPoint = aimPoint };
        
        foreach(var component in ability.EffectComponents)
        {
            currentScore += component.GetAIEstimatedValue(context);
        }
        currentScore -= alliesHit.Count * 75f;

        if(currentScore > bestOutcome.Score)
        {
            bestOutcome.Score = currentScore;
            bestOutcome.AimPoint = aimPoint;
            bestOutcome.EnemiesHit = enemiesHit;
            bestOutcome.AlliesHit = alliesHit;
        }
    }
    return bestOutcome;
}
}