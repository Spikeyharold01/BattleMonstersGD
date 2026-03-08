using Godot;

// =================================================================================================
// FILE: ActiveStatusEffect.cs (GODOT VERSION)
// PURPOSE: A data container class that represents a specific instance of a status effect.
// This is NOT a component and should not be attached to any Node.
// =================================================================================================

/// <summary>
/// A simple data container class that represents a specific instance of a status effect currently
/// active on a creature. It links the effect's blueprint (the Resource) with its
/// live, changing data (like duration, source, and suppression state).
/// </summary>
public class ActiveStatusEffect
{
    // A reference to the Resource that defines what this effect is (e.g., "Bless", "Poison").
    public StatusEffect_SO EffectData;

    // A reference to the creature who created this effect instance.
    // Crucial for conditional effects like Protection from Evil (is the source Evil?).
    public CreatureStats SourceCreature;

    // A flag to indicate if this effect is being temporarily suppressed (e.g., by Protection from Evil).
    public bool IsSuppressed = false;

    // The number of rounds left before this specific instance of the effect expires.
    public int RemainingDuration;

    // Authoritative duration bucket in seconds so travel and arena can share one timing model.
    public float RemainingDurationSeconds;
    
    // The number of uses left for a dischargeable effect.
    public int RemainingCharges;

    // How much typed damage this effect can still absorb before it expires naturally.
    // Output expected: decreases whenever matching incoming damage is intercepted.
    public int RemainingAbsorptionPool;
    
    public int SourcePersistentEffectID = 0; // ID of the lingering effect that applied this status.
    public int SourceSpellLevel; // The level of the spell that created this effect.
    
    // The original save DC of the effect, stored for recurring save checks.
    public int SaveDC;
	
	  // --- Affliction Runtime Data ---
    public float SecondsUntilNextTick;
    public int SuccessfulSaves;
    public bool IsInOnset = true;
    public float ReanimationTimerSeconds;

    /// <summary>
    /// Constructor for creating a new active status effect from its template data.
    /// </summary>
    /// <param name="data">The StatusEffect_SO that defines the effect.</param>
    /// <param name="source">The creature that applied this effect.</param>
    public ActiveStatusEffect(StatusEffect_SO data, CreatureStats source) 
    { 
        EffectData = data; 
        SourceCreature = source;
        RemainingDuration = data.DurationInRounds; // Initialize the duration from the template.
        RemainingDurationSeconds = data.DurationInRounds > 0 ? data.DurationInRounds * TravelScaleDefinitions.CombatTurnSeconds : 0f;
        
		if (data.IsDischargeable)
        {
            RemainingCharges = data.Charges;
        }
        
        if (data.IsAffliction)
        {
            SecondsUntilNextTick = data.OnsetDelaySeconds > 0 ? data.OnsetDelaySeconds : data.FrequencySeconds;
            IsInOnset = data.OnsetDelaySeconds > 0;
            SuccessfulSaves = 0;
            ReanimationTimerSeconds = -1f;
        }
    }
}
