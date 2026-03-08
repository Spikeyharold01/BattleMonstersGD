using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: SoundProfile.cs (GODOT VERSION)
// PURPOSE: Lightweight, derived acoustics profile generated at runtime from template + state.
// ATTACH TO: Do not attach (Data helper).
// =================================================================================================

public enum SoundActionType
{
    Idle,
    Move,
    Run,
    Charge,
    Attack,
    Cast,
    Hide,
    Sneak,
    Illusion
}

public readonly struct SoundProfile
{
    public readonly float BodyVolume;
    public readonly float StepNoise;
    public readonly float WingNoise;
    public readonly float WaterNoise;
    public readonly float BurrowNoise;
    public readonly float StealthFactor;
    public readonly float TypeNoiseModifier;

    public SoundProfile(float bodyVolume, float stepNoise, float wingNoise, float waterNoise, float burrowNoise, float stealthFactor, float typeNoiseModifier)
    {
        BodyVolume = bodyVolume;
        StepNoise = stepNoise;
        WingNoise = wingNoise;
        WaterNoise = waterNoise;
        BurrowNoise = burrowNoise;
        StealthFactor = stealthFactor;
        TypeNoiseModifier = typeNoiseModifier;
    }
}

public static class SoundProfileFactory
{
    private static readonly Dictionary<CreatureType, float> TypeNoiseModifier = new()
    {
        { CreatureType.Dragon, 1.5f },
        { CreatureType.Undead, 0.3f },
        { CreatureType.Ooze, 0.5f },
        { CreatureType.Vermin, 0.7f }
    };

    public static SoundProfile Generate(CreatureTemplate_SO template)
    {
        if (template == null)
        {
            return new SoundProfile(1f, 1f, 0f, 0f, 0f, 1f, 1f);
        }
		bool isIncorporeal = template.SubTypes != null && template.SubTypes.Contains("Incorporeal");
        if (isIncorporeal)
        {
             // Movement noises are 0, but BodyVolume (1f) and Stealth (2.0f) are retained for vocal/attack actions.
            return new SoundProfile(1f, 0f, 0f, 0f, 0f, 2.0f, 1.0f);
        }

        float sizeFactor = template.Size switch
        {
            CreatureSize.Fine => 0.05f,
            CreatureSize.Diminituve => 0.08f,
            CreatureSize.Tiny => 0.15f,
            CreatureSize.Small => 0.3f,
            CreatureSize.Medium => 0.6f,
            CreatureSize.Large => 1.2f,
            CreatureSize.Huge => 2.0f,
            CreatureSize.Gargantuan => 3.0f,
            CreatureSize.Colossal => 4.0f,
            _ => 1f
        };

        float massFactor = Mathf.Clamp((template.Strength + template.Constitution) / 30f, 0.4f, 2.5f);
        float agilityFactor = Mathf.Clamp(1f - (template.Dexterity / 30f), 0.3f, 1.2f);
        float bodyVolume = sizeFactor * massFactor;

        float stepNoise = bodyVolume * agilityFactor;
        float wingNoise = template.Speed_Fly > 0 ? bodyVolume * 0.6f : 0f;
        float waterNoise = template.Speed_Swim > 0 ? bodyVolume * 0.5f : 0f;
        float burrowNoise = template.Speed_Burrow > 0 ? bodyVolume * 0.3f : 0f;

        int sizeIndex = (int)template.Size;
        float stealth = Mathf.Clamp(1f + (template.Dexterity - sizeIndex * 2) / 20f, 0.3f, 2.0f);
        float typeModifier = TypeNoiseModifier.TryGetValue(template.Type, out float modifier) ? modifier : 1f;

        return new SoundProfile(bodyVolume, stepNoise, wingNoise, waterNoise, burrowNoise, stealth, typeModifier);
    }

    public static float EstimateActionLoudness(CreatureStats source, SoundActionType actionType, bool isSneaking = false)
    {
        if (source?.Template == null) return 0f;

        SoundProfile profile = Generate(source.Template);
        float movementNoise = source.Template.MovementType switch
        {
            MovementType.Flying => profile.WingNoise,
            MovementType.Swimming => profile.WaterNoise,
            MovementType.Burrowing => profile.BurrowNoise,
            _ => profile.StepNoise
        };
		// If movement makes no noise (Incorporeal), but the creature is actively attacking or casting, use BodyVolume.
        if (movementNoise == 0f && actionType != SoundActionType.Move && actionType != SoundActionType.Run && actionType != SoundActionType.Hide && actionType != SoundActionType.Sneak)
        {
            movementNoise = profile.BodyVolume;
        }

        float actionMultiplier = actionType switch
        {
            SoundActionType.Run => 1.8f,
            SoundActionType.Charge => 2.2f,
            SoundActionType.Attack => 1.6f,
            SoundActionType.Cast => 1.3f,
            SoundActionType.Hide => 0f,
            SoundActionType.Sneak => 0.2f,
            _ => 1f
        };

        float stealthFactor = isSneaking ? 0.5f : profile.StealthFactor;
        float swarmFactor = source.Template.SubTypes != null && source.Template.SubTypes.Contains("Swarm")
            ? Mathf.Max(1f, source.Template.AverageGroupSize * 0.1f)
            : 1f;

        float minNoiseFloor = actionType == SoundActionType.Hide ? 0f : 0.05f;
        return Mathf.Max(minNoiseFloor, movementNoise * actionMultiplier * profile.TypeNoiseModifier * stealthFactor * swarmFactor);
    }
}