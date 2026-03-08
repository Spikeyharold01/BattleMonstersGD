using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: LearnLanguageEffect.cs (GODOT VERSION)
// PURPOSE: Logic for learning a new language.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class LearnLanguageEffect : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
CreatureStats self = context.Caster;
if (self == null) return;

var allPossibleLanguages = System.Enum.GetValues(typeof(Language)).Cast<Language>();
    var allKnownLanguages = new HashSet<Language>(self.Template.RacialLanguages);
    allKnownLanguages.UnionWith(self.LearnedLanguages);
    
    // Find first unknown
    Language? languageToLearn = null;
    foreach(var lang in allPossibleLanguages)
    {
        if(!allKnownLanguages.Contains(lang))
        {
            languageToLearn = lang;
            break;
        }
    }

    if (languageToLearn.HasValue)
    {
        self.LearnedLanguages.Add(languageToLearn.Value);
        GD.PrintRich($"<color=cyan>{self.Name} has learned {languageToLearn.Value}!</color>");
    }
    else
    {
        GD.Print($"{self.Name} already knows all available languages.");
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var caster = context.Caster;
    // Use AISpatialAnalysis helper
    var visibleEnemies = AISpatialAnalysis.FindVisibleTargets(caster);
    
    var myLanguages = new HashSet<Language>(caster.Template.RacialLanguages);
    myLanguages.UnionWith(caster.LearnedLanguages);

    var unknownLanguages = visibleEnemies
        .SelectMany(e => e.Template.RacialLanguages)
        .Distinct()
        .Where(lang => !myLanguages.Contains(lang))
        .ToList();
    
    if (unknownLanguages.Any())
    {
        return 1f * unknownLanguages.Count;
    }

    return 0f;
}
}