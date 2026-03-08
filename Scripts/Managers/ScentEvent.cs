using Godot;

// =================================================================================================
// FILE: ScentEvent.cs (GODOT VERSION)
// PURPOSE: Runtime scent events used as an alternate perception channel.
// ATTACH TO: Do not attach (Data helper).
// =================================================================================================

public enum ScentType
{
    Creature,
    Undead,
    Blood,
    Fire,
    Smoke,
    Poison,
    Magic,
    Environment
}

public readonly struct ScentEvent
{
    public readonly CreatureStats SourceCreature;
    public readonly Vector3 Position;
    public readonly float Intensity;
    public readonly float DecayRate;
    public readonly float VerticalBias;
    public readonly ScentType Type;
    public readonly int SourceSize;
    public readonly bool IsTrail;
    public readonly float DurationSeconds;
    public readonly float CreatedAtSeconds;

    public ScentEvent(
        CreatureStats sourceCreature,
        Vector3 position,
        float intensity,
        float decayRate,
        float verticalBias,
        ScentType type,
        int sourceSize,
        bool isTrail,
        float durationSeconds)
    {
        SourceCreature = sourceCreature;
        Position = position;
        Intensity = Mathf.Max(0f, intensity);
        DecayRate = Mathf.Max(0.05f, decayRate);
        VerticalBias = Mathf.Clamp(verticalBias, -1f, 1f);
        Type = type;
        SourceSize = sourceSize;
        IsTrail = isTrail;
        DurationSeconds = Mathf.Max(0.25f, durationSeconds);
        CreatedAtSeconds = Time.GetTicksMsec() / 1000f;
    }

    public float AgeSeconds => (Time.GetTicksMsec() / 1000f) - CreatedAtSeconds;
    public float Freshness => Mathf.Clamp(1f - (AgeSeconds * DecayRate), 0f, 1f);
    public bool IsExpired => AgeSeconds >= DurationSeconds || Freshness <= 0.01f;
}

public readonly struct SmelledScentContact
{
    public readonly CreatureStats Source;
    public readonly Vector3 Position;
    public readonly ScentType Type;
    public readonly float Confidence;
    public readonly float Suspicion;
    public readonly bool IsPinpointed;

    public SmelledScentContact(CreatureStats source, Vector3 position, ScentType type, float confidence, float suspicion, bool isPinpointed)
    {
        Source = source;
        Position = position;
        Type = type;
        Confidence = confidence;
        Suspicion = suspicion;
        IsPinpointed = isPinpointed;
    }
}