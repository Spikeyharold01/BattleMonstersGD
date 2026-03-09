using Godot;

// =================================================================================================
// FILE: IllusionPerceptionEmitter.cs (GODOT VERSION)
// PURPOSE: Generic data-driven emitter for illusionary perception channels (sound/scent/thermal proxy).
// ATTACH TO: Runtime child on illusion nodes that should project sensory cues.
// =================================================================================================
public partial class IllusionPerceptionEmitter : Node
{
    private CreatureStats caster;

    private bool emitSound;
    private float soundIntensity;
    private float soundInterval;
    private float soundDuration;

    private bool emitScent;
    private ScentType scentType;
    private float scentIntensity;
    private float scentDecayRate;
    private float scentVerticalBias;
    private bool scentIsTrail;
    private float scentDuration;

    private bool emitThermal;
    private ScentType thermalScentType;
    private float thermalIntensity;
    private float thermalDecayRate;
    private float thermalDuration;

    private float scentInterval;

    private double nextSoundAt;
    private double nextScentAt;

    public void Configure(
        CreatureStats caster,
        bool emitSound,
        float soundIntensity,
        float soundInterval,
        float soundDuration,
        bool emitScent,
        ScentType scentType,
        float scentIntensity,
        float scentDecayRate,
        float scentVerticalBias,
        bool scentIsTrail,
        float scentDuration,
        bool emitThermal,
        ScentType thermalScentType,
        float thermalIntensity,
        float thermalDecayRate,
        float thermalDuration,
        float scentInterval)
    {
        this.caster = caster;

        this.emitSound = emitSound;
        this.soundIntensity = Mathf.Max(0.1f, soundIntensity);
        this.soundInterval = Mathf.Max(0.1f, soundInterval);
        this.soundDuration = Mathf.Max(0.1f, soundDuration);

        this.emitScent = emitScent;
        this.scentType = scentType;
        this.scentIntensity = Mathf.Max(0f, scentIntensity);
        this.scentDecayRate = Mathf.Max(0.05f, scentDecayRate);
        this.scentVerticalBias = Mathf.Clamp(scentVerticalBias, -1f, 1f);
        this.scentIsTrail = scentIsTrail;
        this.scentDuration = Mathf.Max(0.25f, scentDuration);

        this.emitThermal = emitThermal;
        this.thermalScentType = thermalScentType;
        this.thermalIntensity = Mathf.Max(0f, thermalIntensity);
        this.thermalDecayRate = Mathf.Max(0.05f, thermalDecayRate);
        this.thermalDuration = Mathf.Max(0.25f, thermalDuration);

        this.scentInterval = Mathf.Max(0.1f, scentInterval);

        nextSoundAt = Time.GetTicksMsec() / 1000.0;
        nextScentAt = nextSoundAt;
    }

    public override void _Process(double delta)
    {
        if (!emitSound && !emitScent && !emitThermal) return;

        var host = GetParentOrNull<Node3D>();
        if (host == null) return;

        double now = Time.GetTicksMsec() / 1000.0;

        if (emitSound && now >= nextSoundAt)
        {
            SoundSystem.EmitSound(caster, host.GlobalPosition, soundIntensity, soundDuration, SoundEventType.Illusion, isIllusion: true);
            nextSoundAt = now + soundInterval;
        }

        if ((emitScent || emitThermal) && now >= nextScentAt)
        {
            if (emitScent)
            {
                ScentSystem.EmitScent(caster, host.GlobalPosition, scentIntensity, scentDecayRate, scentVerticalBias, scentType, isTrail: scentIsTrail, durationSeconds: scentDuration);
            }

            if (emitThermal)
            {
                ScentSystem.EmitScent(caster, host.GlobalPosition, thermalIntensity, thermalDecayRate, 0f, thermalScentType, isTrail: false, durationSeconds: thermalDuration);
            }

            nextScentAt = now + scentInterval;
        }
    }
}