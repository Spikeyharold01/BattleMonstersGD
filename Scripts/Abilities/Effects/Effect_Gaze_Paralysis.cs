using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: Effect_Gaze_Paralysis.cs (GODOT VERSION)
// PURPOSE: Applies paralysis. Handles racial penalties.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_Gaze_Paralysis : AbilityEffectComponent
{
[ExportGroup("Configuration")]
[Export] public StatusEffect_SO ParalyzedEffect;
[Export] public int DurationDiceCount = 2;
[Export] public int DurationDieSides = 4;

[ExportGroup("Racial Penalties")]
[Export]
[Tooltip("List of subtypes/races that take a penalty on the save.")]
public Godot.Collections.Array<string> PenalizedRaces = new Godot.Collections.Array<string> { "Tiefling", "Half-Fiend" };

[Export] public int RacialPenalty = -2;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    var target = context.PrimaryTarget;
    var caster = context.Caster;
    if (target == null || ParalyzedEffect == null) return;

    // 2. Calculate Save
    int saveBonus = target.GetWillSave(caster, ability);
    
    bool isVulnerableRace = false;
    // Godot Array doesn't have Exists, use Linq or loop
    foreach (string r in PenalizedRaces)
    {
        if (target.Template.Race.Contains(r) || (target.Template.SubTypes != null && target.Template.SubTypes.Contains(r)))
        {
            isVulnerableRace = true;
            break;
        }
    }
    
    if (isVulnerableRace)
    {
        saveBonus += RacialPenalty;
        GD.Print($"{target.Name} takes a {RacialPenalty} penalty on save vs Gaze (Race).");
    }

    // 3. Roll Save
    int dc = ability.SavingThrow.BaseDC; 
    if (ability.SavingThrow.IsDynamicDC) 
    {
        int hd = caster.Template.CasterLevel; 
        dc = 10 + (hd/2) + caster.ChaModifier;
    }

    int roll = Dice.Roll(1, 20) + saveBonus;
    GD.Print($"{target.Name} rolls Will Save vs Gaze (DC {dc}): {roll}");

    if (roll >= dc)
    {
        GD.Print($"{target.Name} resists the gaze!");
        // Grant 24hr immunity
        // AddImmunityEffect isn't in CreatureStats provided earlier.
        // I will implement it here using StatusEffectController.ApplyImmunityEffect (converted in Part 16)
        // But ApplyImmunityEffect was private in StatusEffectController in Part 16.
        // I should assume it's public or implement inline.
        // Inline:
        var immunityEffect = new StatusEffect_SO();
        immunityEffect.EffectName = $"Immunity to Gaze ({caster.Name})";
        immunityEffect.DurationInRounds = 14400; // 24 hours
        target.MyEffects.AddEffect(immunityEffect, caster);
    }
    else
    {
        GD.PrintRich($"[color=red]{target.Name} is PARALYZED by the gaze![/color]");
        var instance = (StatusEffect_SO)ParalyzedEffect.Duplicate();
        instance.DurationInRounds = Dice.Roll(DurationDiceCount, DurationDieSides);
        target.MyEffects.AddEffect(instance, caster, ability);
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    return 100f; 
}
}