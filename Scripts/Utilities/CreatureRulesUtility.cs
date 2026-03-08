using System;
using System.Text.RegularExpressions;

// =================================================================================================
// FILE: CreatureRulesUtility.cs
// PURPOSE: Shared rule helpers for creature-rule queries used by abilities and AI.
// =================================================================================================
public static class CreatureRulesUtility
{
    public static int GetHitDiceCount(CreatureStats creature, int fallback = 1)
    {
        if (creature?.Template == null || string.IsNullOrWhiteSpace(creature.Template.HitDice)) return fallback;

        Match hdMatch = Regex.Match(creature.Template.HitDice, @"^\s*(\d+)");
        if (hdMatch.Success && int.TryParse(hdMatch.Groups[1].Value, out int hd) && hd > 0)
        {
            return hd;
        }

        return fallback;
    }

    public static bool HasAlignmentComponent(CreatureStats creature, string alignmentComponent)
    {
        if (creature?.Template == null || string.IsNullOrWhiteSpace(alignmentComponent)) return false;
        return creature.Template.Alignment?.Contains(alignmentComponent, StringComparison.OrdinalIgnoreCase) == true;
    }

    public static bool IsExtraplanar(CreatureStats creature)
    {
        if (creature?.Template == null) return false;
        if (creature.IsSummoned) return true;

        foreach (var subtype in creature.Template.SubTypes)
        {
            if (!string.IsNullOrWhiteSpace(subtype) && subtype.Contains("extraplanar", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}