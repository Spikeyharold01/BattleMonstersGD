using Godot;
using System.Collections.Generic;
using System.Linq;

[GlobalClass]
public partial class Effect_DimensionDoor : AbilityEffectComponent
{
    [ExportGroup("Rules")]
    [Export] public bool IncludeAdjacentAllies = true;
    [Export] public bool RequireVisibleDestination = true;

    [ExportGroup("Mishap Distances (ft)")]
    [Export] public float NearMishapDistanceFt = 100f;
    [Export] public float FarMishapDistanceFt = 1000f;

    [ExportGroup("Mishap Damage Dice")]
    [Export] public int NearMishapDiceCount = 1;
    [Export] public int NearMishapDiceSides = 6;
    [Export] public int FarMishapDiceCount = 3;
    [Export] public int FarMishapDiceSides = 6;
    [Export] public int HardFailDiceCount = 7;
    [Export] public int HardFailDiceSides = 6;

    [ExportGroup("Post Teleport Lock")]
    [Export] public int DisorientationRounds = 1;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (context?.Caster == null || GridManager.Instance == null)
        {
            return;
        }

        var caster = context.Caster;
        Vector3 requestedDestination = context.AimPoint;

        bool isVisible = IsDestinationVisible(caster, requestedDestination);
        bool tileIsValid = IsTileValidForTeleport(caster, requestedDestination, null);
        bool destinationIsValid = (!RequireVisibleDestination || isVisible) && tileIsValid;

        Vector3 finalDestination = requestedDestination;

        if (!destinationIsValid)
        {
            float attemptedDistance = caster.GlobalPosition.DistanceTo(requestedDestination);
            bool hasRecoveryDestination = ResolveMishap(caster, requestedDestination, attemptedDistance, out Vector3 recoveryDestination, out int damage);

            if (damage > 0)
            {
                caster.TakeDamage(damage, "Force", caster);
            }

            if (!hasRecoveryDestination)
            {
                GD.PrintRich($"[color=orange]{caster.Name}'s {ability.AbilityName} fails: no safe destination found.[/color]");
                return;
            }

            finalDestination = recoveryDestination;
        }

        var travelers = CollectTravelers(caster);
        var occupiedDestinations = new HashSet<GridNode>();

        for (int i = 0; i < travelers.Count; i++)
        {
            var traveler = travelers[i];
            if (!GodotObject.IsInstanceValid(traveler) || traveler.CurrentHP <= 0) continue;

            Vector3 landingPoint = i == 0
                ? finalDestination
                : FindNearbyOpenLanding(caster, finalDestination, occupiedDestinations);

            if (landingPoint == Vector3.Zero)
            {
                continue;
            }

            GridNode landingNode = GridManager.Instance.NodeFromWorldPoint(landingPoint);
            if (landingNode != null)
            {
                occupiedDestinations.Add(landingNode);
            }

            traveler.GlobalPosition = landingPoint;
        }

        ApplyDisorientation(caster, ability);
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (context?.Caster == null)
        {
            return -1f;
        }

        GridNode destinationNode = GridManager.Instance?.NodeFromWorldPoint(context.AimPoint);
        if (destinationNode == null || destinationNode.terrainType == TerrainType.Solid)
        {
            return -1f;
        }

        float score = 15f;
        if (RequireVisibleDestination && !IsDestinationVisible(context.Caster, context.AimPoint))
        {
            score -= 40f;
        }

        return score;
    }

    private bool ResolveMishap(CreatureStats caster, Vector3 requestedDestination, float attemptedDistanceFt, out Vector3 recoveredDestination, out int damage)
    {
        recoveredDestination = Vector3.Zero;

        damage = RollDamageForMishapDistance(attemptedDistanceFt);
        float radius = attemptedDistanceFt <= NearMishapDistanceFt ? NearMishapDistanceFt : FarMishapDistanceFt;

        if (TryFindRandomValidDestination(caster, requestedDestination, radius, out Vector3 randomDestination))
        {
            recoveredDestination = randomDestination;
            return true;
        }

        damage = Dice.Roll(HardFailDiceCount, HardFailDiceSides);
        return false;
    }

    private int RollDamageForMishapDistance(float attemptedDistanceFt)
    {
        if (attemptedDistanceFt <= NearMishapDistanceFt)
        {
            return Dice.Roll(NearMishapDiceCount, NearMishapDiceSides);
        }

        if (attemptedDistanceFt <= FarMishapDistanceFt)
        {
            return Dice.Roll(FarMishapDiceCount, FarMishapDiceSides);
        }

        return Dice.Roll(HardFailDiceCount, HardFailDiceSides);
    }

    private bool TryFindRandomValidDestination(CreatureStats caster, Vector3 center, float radiusFt, out Vector3 destination)
    {
        destination = Vector3.Zero;

        float nodeStep = Mathf.Max(1f, GridManager.Instance.nodeDiameter);
        int attempts = 64;

        for (int i = 0; i < attempts; i++)
        {
            float randomRadius = (float)GD.RandRange(0, radiusFt);
            float angle = (float)GD.RandRange(0.0, Mathf.Tau);
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * randomRadius;
            Vector3 candidate = center + offset;

            GridNode snappedNode = GridManager.Instance.NodeFromWorldPoint(candidate);
            if (snappedNode == null) continue;

            candidate = snappedNode.worldPosition;
            if (!IsTileValidForTeleport(caster, candidate, null)) continue;

            destination = candidate;
            return true;
        }

        // Deterministic fallback around nearby tiles.
        int ringCount = Mathf.CeilToInt(radiusFt / nodeStep);
        GridNode centerNode = GridManager.Instance.NodeFromWorldPoint(center);
        if (centerNode == null) return false;

        for (int ring = 1; ring <= ringCount; ring++)
        {
            for (int x = -ring; x <= ring; x++)
            {
                for (int z = -ring; z <= ring; z++)
                {
                    Vector3 probe = centerNode.worldPosition + new Vector3(x * nodeStep, 0f, z * nodeStep);
                    GridNode node = GridManager.Instance.NodeFromWorldPoint(probe);
                    if (node == null) continue;

                    if (!IsTileValidForTeleport(caster, node.worldPosition, null)) continue;

                    destination = node.worldPosition;
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsDestinationVisible(CreatureStats caster, Vector3 destination)
    {
        if (!RequireVisibleDestination)
        {
            return true;
        }

        Vector3 eye = caster.GlobalPosition + Vector3.Up * 1.2f;
        Vector3 target = destination + Vector3.Up * 0.5f;
        return LineOfSightManager.HasLineOfEffect(caster, eye, target);
    }

    private bool IsTileValidForTeleport(CreatureStats caster, Vector3 destination, HashSet<GridNode> extraBlockedNodes)
    {
        GridNode node = GridManager.Instance.NodeFromWorldPoint(destination);
        if (node == null || node.terrainType == TerrainType.Solid)
        {
            return false;
        }

        if (extraBlockedNodes != null && extraBlockedNodes.Contains(node))
        {
            return false;
        }

        bool occupiedByAnyCreature = TurnManager.Instance.GetAllCombatants()
            .Any(c => GodotObject.IsInstanceValid(c) && c.CurrentHP > 0 && GridManager.Instance.NodeFromWorldPoint(c.GlobalPosition) == node);

        return !occupiedByAnyCreature;
    }

    private List<CreatureStats> CollectTravelers(CreatureStats caster)
    {
        var travelers = new List<CreatureStats> { caster };
        if (!IncludeAdjacentAllies)
        {
            return travelers;
        }

        var allCombatants = TurnManager.Instance.GetAllCombatants();
        foreach (var ally in allCombatants)
        {
            if (ally == caster || !GodotObject.IsInstanceValid(ally) || ally.CurrentHP <= 0) continue;
            if (ally.IsInGroup("Player") != caster.IsInGroup("Player")) continue;

            float contactDistance = (caster.Template.Space + ally.Template.Space) * 0.5f + 0.5f;
            if (caster.GlobalPosition.DistanceTo(ally.GlobalPosition) <= contactDistance)
            {
                travelers.Add(ally);
            }
        }

        return travelers;
    }

    private Vector3 FindNearbyOpenLanding(CreatureStats caster, Vector3 desiredPoint, HashSet<GridNode> blockedNodes)
    {
        GridNode centerNode = GridManager.Instance.NodeFromWorldPoint(desiredPoint);
        if (centerNode == null)
        {
            return Vector3.Zero;
        }

        if (IsTileValidForTeleport(caster, centerNode.worldPosition, blockedNodes))
        {
            return centerNode.worldPosition;
        }

        float step = Mathf.Max(1f, GridManager.Instance.nodeDiameter);
        for (int ring = 1; ring <= 3; ring++)
        {
            for (int x = -ring; x <= ring; x++)
            {
                for (int z = -ring; z <= ring; z++)
                {
                    Vector3 probe = centerNode.worldPosition + new Vector3(x * step, 0f, z * step);
                    GridNode probeNode = GridManager.Instance.NodeFromWorldPoint(probe);
                    if (probeNode == null) continue;

                    if (!IsTileValidForTeleport(caster, probeNode.worldPosition, blockedNodes)) continue;
                    return probeNode.worldPosition;
                }
            }
        }

        return Vector3.Zero;
    }

    private void ApplyDisorientation(CreatureStats caster, Ability_SO sourceAbility)
    {
        var disorientation = new StatusEffect_SO
        {
            EffectName = "Dimensional Disorientation",
            Description = "Cannot act until the start of your next turn.",
            DurationInRounds = Mathf.Max(1, DisorientationRounds),
            ConditionApplied = Condition.Dazed
        };

        caster.MyEffects.AddEffect(disorientation, caster, sourceAbility);
    }
}