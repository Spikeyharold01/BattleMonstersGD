using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_EmitSound.cs (GODOT VERSION)
// PURPOSE: Emits a distinct SoundEvent from the caster or target location. 
//          Used for spells that create noise (Thunder, Sonic tones, Explosions).
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_EmitSound : AbilityEffectComponent
{
    [ExportGroup("Sound Properties")]
    [Export] 
    [Tooltip("The base intensity of the sound. (Approx: 0.8 = Footstep, 2.0 = 30ft radius, 6.0 = 60ft radius).")]
    public float BaseIntensity = 2.0f;

    [Export]
    [Tooltip("If true, Intensity increases by ScalingFactor * CasterLevel.")]
    public bool ScaleWithLevel = false;

    [Export]
    public float ScalingFactor = 0.2f;

    [Export]
    [Tooltip("How long the sound 'lingers' in memory for AI detection.")]
    public float DurationSeconds = 2.0f;

    [ExportGroup("Origin")]
    [Export]
    [Tooltip("Where does the sound originate? Self = Caster, Area = AimPoint/Target.")]
    public TargetType SoundOrigin = TargetType.Self;

    [Export]
    [Tooltip("The type of sound for AI classification.")]
    public SoundEventType SoundType = SoundEventType.Chanting;

    [Export]
    [Tooltip("Is this an illusionary sound? (Ghost Sound, Ventriloquism).")]
    public bool IsIllusion = false;

    [ExportGroup("Optional Audio")]
    [Export]
    [Tooltip("Optional audio stream path to play with the glyph (example: res://Audio/SFX/Thunderclap.mp3). Leave empty for visual-only sound.")]
    public string AudioStreamPath = "";

    [Export]
    [Tooltip("Volume in decibels for the optional audio playback.")]
    public float AudioVolumeDb = 0f;

    [Export]
    [Tooltip("Pitch multiplier for optional audio playback.")]
    public float AudioPitchScale = 1f;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        // 1. Determine Position
        Vector3 originPos = context.Caster.GlobalPosition;

        if (SoundOrigin == TargetType.Area_EnemiesOnly || SoundOrigin == TargetType.Area_FriendOrFoe || SoundOrigin == TargetType.Area_AlliesOnly)
        {
            originPos = context.AimPoint;
        }
        else if (SoundOrigin == TargetType.SingleEnemy && context.PrimaryTarget != null)
        {
            originPos = context.PrimaryTarget.GlobalPosition;
        }
        else if (context.TargetObject != null)
        {
            originPos = (context.TargetObject as Node3D).GlobalPosition;
        }

        // 2. Calculate Intensity
        float finalIntensity = BaseIntensity;
        if (ScaleWithLevel && context.Caster.Template != null)
        {
            finalIntensity += (context.Caster.Template.CasterLevel * ScalingFactor);
        }

        // 3. Emit
        SoundSystem.EmitSound(
            context.Caster,
            originPos,
            finalIntensity,
            DurationSeconds,
            SoundType,
            IsIllusion,
            direction: null,
            audioStreamPath: AudioStreamPath,
            audioVolumeDb: AudioVolumeDb,
            audioPitchScale: AudioPitchScale);
        
        // Visual debug for editor
        GD.Print($"[Effect_EmitSound] {ability.AbilityName} produced sound at {originPos} with Intensity {finalIntensity:F1}.");
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (context?.Caster == null) return 0f;

        if (IsIllusion) return 12f;

        float score = 0f;

        bool hasStealthEdge = context.Caster.MyEffects.HasCondition(Condition.Invisible);
        if (hasStealthEdge)
        {
            score -= Mathf.Clamp(BaseIntensity * 6f, 8f, 36f);
        }

        var combatants = TurnManager.Instance?.GetAllCombatants();
        if (combatants != null)
        {
            int nearbyUnseenHostiles = 0;
            foreach (var unit in combatants)
            {
                if (unit == null || unit == context.Caster) continue;
                if (unit.IsInGroup("Player") == context.Caster.IsInGroup("Player")) continue;

                bool hasLos = LineOfSightManager.GetVisibility(context.Caster, unit).HasLineOfSight;
                if (!hasLos && context.Caster.GlobalPosition.DistanceTo(unit.GlobalPosition) <= 100f)
                {
                    nearbyUnseenHostiles++;
                }
            }

            if (nearbyUnseenHostiles > 0)
            {
                score -= nearbyUnseenHostiles * Mathf.Clamp(BaseIntensity * 1.5f, 2f, 12f);
            }
        }

        return score;
    }
}