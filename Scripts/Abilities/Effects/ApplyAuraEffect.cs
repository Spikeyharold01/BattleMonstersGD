using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: ApplyAuraEffect.cs (GODOT VERSION)
// PURPOSE: An effect component for applying a StatusEffect_SO to targets who failed a save.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class ApplyAuraEffect : AbilityEffectComponent
{
[Export]
[Tooltip("The status effect to apply to creatures who fail their save inside the aura.")]
public StatusEffect_SO EffectToApply;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    foreach (var entry in targetSaveResults)
    {
        if (entry.Value) continue; // Skip targets that saved successfully

        CreatureStats target = entry.Key;

        // 1. Get the duration calculated from the imported spreadsheet data.
        int importedDuration = ability.AuraEffectDuration.GetDurationInRounds();

        // 2. Create a temporary copy of the status effect to apply.
        StatusEffect_SO effectInstance = (StatusEffect_SO)EffectToApply.Duplicate();

        // 3. APPLY THE FALLBACK LOGIC
        if (importedDuration > 0)
        {
            // If the spreadsheet provided a valid duration, OVERRIDE the default.
            effectInstance.DurationInRounds = importedDuration;
        }
        // If importedDuration is 0, we do nothing, so effectInstance KEEPS its original hardcoded duration.

        // 4. Check if the final duration is valid before applying.
        if (effectInstance.DurationInRounds > 0)
        {
            target.MyEffects.AddEffect(effectInstance, context.Caster);
            GD.Print($"{target.Name} failed save vs {ability.AbilityName} and is now {effectInstance.EffectName} for {effectInstance.DurationInRounds} rounds.");
        }
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    return 0f; // Placeholder
}
}