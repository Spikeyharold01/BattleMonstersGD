using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: AllyDecisionNarrativeGenerator.cs
// PURPOSE: Builds narrative explanations for ally AI decisions when a player suggests a plan.
// Fully integrated with AIScoringEngine, TurnPlan, AIController, and CreatureStats.
// =================================================================================================
public static class AllyDecisionNarrativeGenerator
{
    private static readonly string[] OpeningsFollow = new string[]
    {
        "With a quick nod",
        "After a sharp glance",
        "Without missing a beat",
        "Reading the battlefield in a heartbeat",
        "With practiced eyes",
        "After scanning the situation",
        "With deliberate motion",
        "Quickly adjusting its stance",
        "Observing the battlefield carefully",
        "In a split-second decision",
        "Analyzing the threat",
        "With battlefield awareness",
        "After calculating risk",
        "With precise timing",
        "Without hesitation"
    };

    private static readonly string[] OpeningsAdapt = new string[]
    {
        "After weighing the danger",
        "With a cautious breath",
        "As the clash tightens",
        "In a split-second recalculation",
        "After quickly reassessing",
        "With a subtle adjustment",
        "Responding to shifting threats",
        "While analyzing enemy moves",
        "Reacting instinctively",
        "With a tactical pause",
        "After sensing opportunity",
        "In a heartbeat",
        "With quick judgment",
        "Adapting to sudden changes",
        "Considering survival first"
    };

    private static readonly string[] Connectors = new string[]
    {
        "and",
        "while",
        "because",
        "as",
        "which is why",
        "explaining",
        "thus",
        "allowing it to"
    };

    private static readonly string[] FlairFollow = new string[]
    {
        ", turning intent into disciplined action.",
        ", showing practiced trust in your command.",
        ", making the teamwork look effortless.",
        ", with timing that keeps pressure on the enemy.",
        ", coordinating seamlessly with allies.",
        ", ensuring its strikes land effectively.",
        ", making the most of the battlefield.",
        ", demonstrating expert coordination.",
        ", executing the plan with precision.",
        ", keeping allies in optimal positions.",
        ", maintaining focus under pressure.",
        ", displaying remarkable initiative.",
        ", taking advantage of every opportunity.",
        ", reinforcing team synergy.",
        ", demonstrating battlefield awareness."
    };

    private static readonly string[] FlairAdapt = new string[]
    {
        ", prioritizing survival over strict obedience.",
        ", proving instinct can outrun a rigid script.",
        ", keeping the ally alive for the next exchange.",
        ", favoring battlefield sense over direct compliance.",
        ", adjusting tactics in response to danger.",
        ", making a calculated choice.",
        ", avoiding unnecessary risk.",
        ", trusting its own instincts.",
        ", finding a safer angle of engagement.",
        ", leveraging terrain advantage.",
        ", ensuring allies remain supported.",
        ", focusing on long-term survival.",
        ", thinking several steps ahead.",
        ", choosing efficiency over orders.",
        ", demonstrating tactical flexibility."
    };

    private static RandomNumberGenerator rng = new RandomNumberGenerator();

    static AllyDecisionNarrativeGenerator()
    {
        rng.Randomize();
    }

    // ------------------------------------------
    // PUBLIC: Build narrative sentence
    // ------------------------------------------
    public static string BuildNarrative(AIController controller, SuggestedAction suggestion, TurnPlan plan, bool followsSuggestion)
    {
        if (controller == null || suggestion == null || plan == null) return string.Empty;

        string opening = Pick(followsSuggestion ? OpeningsFollow : OpeningsAdapt);
        string subject = BuildSubject(controller.MyStats);
        string primaryReason = GeneratePrimaryReason(controller, suggestion, plan);
        string secondaryReason = GenerateSecondaryReason(controller, suggestion, plan);
        string connector = Pick(Connectors);
        string flair = Pick(followsSuggestion ? FlairFollow : FlairAdapt);

        return $"{opening}, {subject} {(followsSuggestion ? "follows" : "adjusts")} the suggestion {connector} {primaryReason} {connector} {secondaryReason}{flair}";
    }

    // ------------------------------------------
    // Build subject dynamically from creature stats
    // ------------------------------------------
    private static string BuildSubject(CreatureStats creature)
    {
        if (creature == null) return "The ally";

        string size = creature.Template.Size.ToString().ToLower();
        string type = creature.Template.Type.ToString().ToLower();
        string name = creature.Name ?? creature.Template.Name;

        // Add simple adjectives for variety
        string adjective = rng.RandiRange(0, 1) == 0 ? "quick" : "cunning";

        return $"{adjective} {name} ({size} {type})";
    }

    // ------------------------------------------
    // Generate primary reason from AI scoring
    // ------------------------------------------
    private static string GeneratePrimaryReason(AIController controller, SuggestedAction suggestion, TurnPlan plan)
    {
        float hpPct = controller.MyStats.CurrentHP / (float)Math.Max(1, controller.MyStats.Template.MaxHP);
        float threatScore = plan.EvaluateThreatScore(controller); // Use existing scoring
        float survival = hpPct < 0.4f ? 1f : 0f;

        if (survival > 0f) return "it prioritizes survival due to low health";
        if (suggestion.Intent == SuggestedActionIntent.Support) return "it wants to assist allies efficiently";
        if (suggestion.Intent == SuggestedActionIntent.Control) return "it seeks to dominate the battlefield tactically";
        if (suggestion.Intent == SuggestedActionIntent.Attack)
        {
            if (threatScore > 0.7f) return "it seizes the strongest position to maximize attack potential";
            else return "it strikes opportunistically based on enemy vulnerabilities";
        }
        if (suggestion.Intent == SuggestedActionIntent.Defend) return "it prepares to defend key positions";
        if (suggestion.Intent == SuggestedActionIntent.Retreat) return "it moves strategically to minimize exposure";
        return "it evaluates the battlefield carefully before acting";
    }

    // ------------------------------------------
    // Generate secondary reason for narrative
    // ------------------------------------------
    private static string GenerateSecondaryReason(AIController controller, SuggestedAction suggestion, TurnPlan plan)
    {
        List<string> options = new List<string>();

        if (plan.Actions.Any(a => a is AIAction_Movement))
            options.Add("to gain a tactical advantage of terrain");
        if (plan.Actions.Any(a => a is AIAction_Combat || a is AIAction_Magic))
            options.Add("to maximize its offensive impact");
        if (plan.Actions.Any(a => a is AIAction_Utility))
            options.Add("to support allies effectively");
        if (plan.Actions.Any(a => a is AIAction_Defensive))
            options.Add("to minimize risk and exposure");
        if (options.Count == 0) options.Add("after careful assessment of the battlefield");

        return Pick(options);
    }

    // ------------------------------------------
    // Random pick helper
    // ------------------------------------------
    private static string Pick(IReadOnlyList<string> pool)
    {
        if (pool == null || pool.Count == 0) return string.Empty;
        return pool[rng.RandiRange(0, pool.Count - 1)];
    }
}