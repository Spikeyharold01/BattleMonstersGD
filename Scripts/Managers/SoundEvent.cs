using Godot;

// =================================================================================================
// FILE: SoundEvent.cs (GODOT VERSION)
// PURPOSE: Runtime sound events used as a second perception channel.
// ATTACH TO: Do not attach (Data helper).
// =================================================================================================

public enum SoundEventType
{
    Movement,
    Combat,
    Chanting,
    Laughter,
    Illusion
}

public readonly struct SoundEvent
{
    public readonly CreatureStats SourceCreature;
    public readonly Vector3 WorldPosition;
    public readonly float NoiseIntensity;
    public readonly float DurationSeconds;
    public readonly float CreatedAtSeconds;
    public readonly SoundEventType Type;
    public readonly bool IsIllusion;
    public readonly Vector3 Direction;
	
    public SoundEvent(
        CreatureStats sourceCreature,
        Vector3 worldPosition,
        float noiseIntensity,
        float durationSeconds,
        SoundEventType type,
        bool isIllusion = false,
        Vector3? direction = null)
    {
        SourceCreature = sourceCreature;
        WorldPosition = worldPosition;
        NoiseIntensity = noiseIntensity;
        DurationSeconds = Mathf.Max(0.1f, durationSeconds);
        CreatedAtSeconds = Time.GetTicksMsec() / 1000f;
        Type = type;
        IsIllusion = isIllusion;
        Direction = (direction ?? Vector3.Zero).Normalized();
    }

    public float AgeSeconds => (Time.GetTicksMsec() / 1000f) - CreatedAtSeconds;
    public bool IsExpired => AgeSeconds >= DurationSeconds;
}

public readonly struct HeardSoundContact
{
    public readonly CreatureStats Source;
    public readonly Vector3 Position;
    public readonly SoundEventType Type;
    public readonly float Confidence;
    public readonly float ThreatEstimate;
    public readonly bool IsIllusion;

    public HeardSoundContact(CreatureStats source, Vector3 position, SoundEventType type, float confidence, float threatEstimate, bool isIllusion)
    {
        Source = source;
        Position = position;
        Type = type;
        Confidence = confidence;
        ThreatEstimate = threatEstimate;
        IsIllusion = isIllusion;
    }
}