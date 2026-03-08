using Godot;

/// <summary>
/// Lightweight edge-awareness metrics used by AI movement scoring.
///
/// Expected output:
/// - DistanceToNearestEdge is measured in grid steps.
/// - EdgePressureScore is normalized to 0..1 using the requested formula.
/// - CenterVector points inward so callers can bias away from wall loops.
/// </summary>
public readonly struct BoundaryEvaluation
{
    public BoundaryEvaluation(float distanceToNearestEdge, float edgePressureScore, Vector3 centerVector)
    {
        DistanceToNearestEdge = distanceToNearestEdge;
        EdgePressureScore = edgePressureScore;
        CenterVector = centerVector;
    }

    public float DistanceToNearestEdge { get; }
    public float EdgePressureScore { get; }
    public Vector3 CenterVector { get; }
}

public static class BoundaryEvaluator
{
    /// <summary>
    /// Computes edge pressure for one creature turn.
    /// </summary>
    public static BoundaryEvaluation Evaluate(CreatureStats creature, float maxSpeedPerRound)
    {
        if (creature == null || GridManager.Instance == null)
        {
            return new BoundaryEvaluation(0f, 0f, Vector3.Zero);
        }

        GridNode node = GridManager.Instance.NodeFromWorldPoint(creature.GlobalPosition);
        int xDistance = Mathf.Min(node.gridX, Mathf.Max(0, GridManager.Instance.GridSizeX - 1 - node.gridX));
        int zDistance = Mathf.Min(node.gridZ, Mathf.Max(0, GridManager.Instance.GridSizeZ - 1 - node.gridZ));
        float distanceToNearestEdge = Mathf.Min(xDistance, zDistance);

        float edgePressureScore = Mathf.Clamp((maxSpeedPerRound - distanceToNearestEdge) / Mathf.Max(0.001f, maxSpeedPerRound), 0f, 1f);
        Vector3 centerVector = (GridManager.Instance.GlobalPosition - creature.GlobalPosition).Normalized();

        CombatBoundaryService.RegisterEdgePressure(creature, edgePressureScore);
        return new BoundaryEvaluation(distanceToNearestEdge, edgePressureScore, centerVector);
    }

    /// <summary>
    /// Applies role-aware vector shaping near borders.
    /// </summary>
    public static Vector3 BuildEdgeAwareDirection(CreatureStats creature, Vector3 desiredDirection, bool intentIsFlee, bool moraleBroken, bool disengageActionDeclared)
    {
        float maxSpeedPerRound = Mathf.Max(1f, (creature?.GetNodeOrNull<CreatureMover>("CreatureMover")?.GetEffectiveMovementSpeed() ?? 30f) / 5f);
        BoundaryEvaluation eval = Evaluate(creature, maxSpeedPerRound);

        bool escapeAuthorized = intentIsFlee || moraleBroken || disengageActionDeclared;
        Vector3 inward = eval.CenterVector * CombatBoundaryService.InwardBiasMultiplier;

        bool isAerialOrSkirmisher = creature?.Template?.Speed_Fly > 0;
        bool isRangedArtillery = creature?.Template?.KnownAbilities?.Exists(a => a != null && (a.AbilityName?.Contains("Arrow") == true || a.AbilityName?.Contains("Ray") == true || a.AbilityName?.Contains("Shot") == true)) == true;
        bool isMeleeBrute = !isRangedArtillery && !isAerialOrSkirmisher;

        Vector3 result = desiredDirection;

        if (isAerialOrSkirmisher && eval.EdgePressureScore > 0.5f)
        {
            // Favor lateral drift plus inward bias to avoid straight-line edge retreats.
            Vector3 lateral = new Vector3(-desiredDirection.Z, 0f, desiredDirection.X).Normalized();
            result = (desiredDirection * 0.35f + lateral * 0.25f + inward * 0.4f).Normalized();
        }

        if (isAerialOrSkirmisher && CombatBoundaryService.CurrentMode == CombatBoundaryMode.ArenaLocked && eval.EdgePressureScore > 0.8f)
        {
            result = (inward * 0.75f + desiredDirection * 0.25f).Normalized();
        }

        if (isMeleeBrute && eval.EdgePressureScore > 0.5f)
        {
            result = (desiredDirection * 0.55f + inward * 0.45f).Normalized();
        }

        if (isRangedArtillery && CombatBoundaryService.CurrentMode == CombatBoundaryMode.ArenaLocked && eval.EdgePressureScore > 0.55f)
        {
            Vector3 diagonalInward = (desiredDirection + inward).Normalized();
            result = diagonalInward;
        }

        if (CombatBoundaryService.CurrentMode == CombatBoundaryMode.TravelEscapable && !escapeAuthorized)
        {
            result = (desiredDirection * 0.45f + inward * 0.55f).Normalized();
        }

        return result == Vector3.Zero ? desiredDirection : result;
    }
}
