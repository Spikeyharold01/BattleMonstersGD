using Godot;
using System.Collections.Generic;
using System.Linq;

public static class MultiTargetingHelper
{
    /// <summary>
    /// Builds a compact target cluster where every member is within maxDistanceFeet of every other member.
    /// Uses a greedy pass that prefers the preferredTarget first, then nearest candidates.
    /// </summary>
    public static List<CreatureStats> BuildPairwiseCluster(
        IEnumerable<CreatureStats> candidates,
        CreatureStats preferredTarget,
        int maxTargets,
        float maxDistanceFeet)
    {
        var source = candidates?.Where(t => t != null).Distinct().ToList() ?? new List<CreatureStats>();
        if (source.Count == 0 || maxTargets <= 0)
            return new List<CreatureStats>();

        var selected = new List<CreatureStats>();

        if (preferredTarget != null && source.Contains(preferredTarget))
            selected.Add(preferredTarget);

        var orderingAnchor = preferredTarget ?? source[0];
        var ordered = source
            .Where(t => t != preferredTarget)
            .OrderBy(t => t.GlobalPosition.DistanceTo(orderingAnchor.GlobalPosition))
            .ToList();

        foreach (var candidate in ordered)
        {
            if (selected.Count >= maxTargets)
                break;

            bool passesPairwiseDistance = selected.Count == 0 || selected.All(existing =>
                existing.GlobalPosition.DistanceTo(candidate.GlobalPosition) <= maxDistanceFeet);

            if (passesPairwiseDistance)
                selected.Add(candidate);
        }

        if (selected.Count == 0)
            selected.Add(orderingAnchor);

        return selected.Take(maxTargets).ToList();
    }
}