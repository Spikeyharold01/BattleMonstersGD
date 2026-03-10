using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: SoundSystem.cs (GODOT VERSION)
// PURPOSE: Central resolver for hearing-based perception.
// ATTACH TO: Do not attach (Static system).
// =================================================================================================
public static class SoundSystem
{
    private static readonly List<SoundEvent> activeEvents = new();
    private const float GhostSoundIntensityPerHuman = 0.6f;
    private const int GhostSoundHumansPerCasterLevel = 4;
    private const int GhostSoundHumanCap = 40;

    private readonly struct ResolvedSensoryEmissionModifier
    {
        public readonly float IntensityMultiplier;
        public readonly float MinimumIntensityFraction;
        public readonly float PositionalUncertaintyFeet;
        public readonly float ConfidenceMultiplier;
        public readonly float LocationDecayMultiplier;

        public ResolvedSensoryEmissionModifier(float intensityMultiplier, float minimumIntensityFraction, float positionalUncertaintyFeet, float confidenceMultiplier, float locationDecayMultiplier)
        {
            IntensityMultiplier = intensityMultiplier;
            MinimumIntensityFraction = minimumIntensityFraction;
            PositionalUncertaintyFeet = positionalUncertaintyFeet;
            ConfidenceMultiplier = confidenceMultiplier;
            LocationDecayMultiplier = locationDecayMultiplier;
        }

        public static ResolvedSensoryEmissionModifier Neutral => new(1f, 0.2f, 0f, 1f, 1f);
    }


    public static void Reset()
    {
        activeEvents.Clear();
    }

    public static bool IsPointSilenced(Vector3 point)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return false;
        var silenceAuras = tree.GetNodesInGroup("SilenceAuras");
        foreach (Node n in silenceAuras)
        {
            if (n is SilenceAuraController aura && aura.IsPointInside(point)) return true;
        }
        return false;
    }

    public static SoundEvent EmitSound(
        CreatureStats source,
        Vector3 position,
        float intensity,
        float durationSeconds,
        SoundEventType type,
        bool isIllusion = false,
        Vector3? direction = null,
        string audioStreamPath = "",
        float audioVolumeDb = 0f,
        float audioPitchScale = 1f)
    {
        if (IsPointSilenced(position) || (source != null && source.MyEffects != null && source.MyEffects.HasCondition(Condition.Silenced)))
        {
            return new SoundEvent(source, position, 0f, 0.1f, type, isIllusion, direction);
        }

        var modifier = ResolveSensoryEmissionModifier(source);
        float shapedIntensity = Mathf.Max(intensity * modifier.MinimumIntensityFraction, intensity * modifier.IntensityMultiplier);
        shapedIntensity = Mathf.Max(0.05f, shapedIntensity);

        var sound = new SoundEvent(source, position, shapedIntensity, durationSeconds, type, isIllusion, direction);
        activeEvents.Add(sound);

        // Only provide player-facing cues if the single player listener can hear.
        // Allied AI should not grant hearing cues to the player character.
        if (ShouldRenderSoundCuesForPlayerListener(sound))
        {
            SoundGlyphRenderer.TryRender(sound);
            SoundAudioRenderer.TryPlay(position, audioStreamPath, audioVolumeDb, audioPitchScale);
        }

        PruneExpired();
        return sound;
    }

    public static void EmitCreatureActionSound(CreatureStats source, SoundActionType actionType, bool isSneaking = false, float durationSeconds = 1.25f, Vector3? direction = null)
    {
        if (source == null) return;
        float intensity = SoundProfileFactory.EstimateActionLoudness(source, actionType, isSneaking);
        SoundEventType eventType = actionType == SoundActionType.Illusion ? SoundEventType.Illusion :
                                   actionType == SoundActionType.Cast ? SoundEventType.Chanting :
                                   (actionType == SoundActionType.Attack || actionType == SoundActionType.Charge)
                                        ? SoundEventType.Combat
                                        : SoundEventType.Movement;

        EmitSound(source, source.GlobalPosition, intensity, durationSeconds, eventType, false, direction);
    }

    public static void EmitGhostSound(CreatureStats caster, Vector3 position, float baseIntensity, float durationSeconds)
    {
        float tunedIntensity = Mathf.Max(0.5f, baseIntensity);
        EmitSound(caster, position, tunedIntensity, durationSeconds, SoundEventType.Illusion, isIllusion: true);
    }

     public static int GetGhostSoundMaxHumanVolume(int casterLevel)
    {
        return Mathf.Clamp(casterLevel * GhostSoundHumansPerCasterLevel, 0, GhostSoundHumanCap);
    }

    public static float ConvertGhostSoundHumanVolumeToIntensity(float humanVolume)
    {
        return Mathf.Max(0.1f, humanVolume * GhostSoundIntensityPerHuman);
    }

    public static void EmitGhostSoundByHumanVolume(CreatureStats caster, Vector3 position, float humanVolume, float durationSeconds)
    {
        int casterLevel = caster?.Template?.CasterLevel ?? 0;
        float maxHumans = GetGhostSoundMaxHumanVolume(casterLevel);
        float clampedHumans = Mathf.Clamp(humanVolume, 0.1f, Mathf.Max(0.1f, maxHumans));
        float intensity = ConvertGhostSoundHumanVolumeToIntensity(clampedHumans);
        EmitGhostSound(caster, position, intensity, durationSeconds);
    }

    public static bool CanHear(CreatureStats listener, SoundEvent sound)
    {
        if (listener == null || listener.Template == null || sound.IsExpired) return false;
        if (IsDeafened(listener)) return false;
        if (IsPointSilenced(listener.GlobalPosition)) return false;
		
        float distance = listener.GlobalPosition.DistanceTo(sound.WorldPosition);
        if (distance > 200f) return false; // hard cap for perf and omniscience prevention

        int perceptionBonus = listener.GetPerceptionBonus();

        int sourceStealthBonus = 0;
        if (sound.SourceCreature != null)
        {
            sourceStealthBonus = sound.SourceCreature.GetStealthBonus(isMoving: true);
        }

        // Distance attenuation model: strong quadratic loss over range to avoid omniscient hearing.
        // This keeps loud creatures/audible events detectable farther while tiny sources fade out quickly.
        float distanceFalloff = 1f / (1f + ((distance * distance) / 400f));
        float ageFalloff = Mathf.Clamp(1f - (sound.AgeSeconds / sound.DurationSeconds), 0f, 1f);
        float effectiveSignal = sound.NoiseIntensity * distanceFalloff * ageFalloff;

        float stealthPenalty = Mathf.Max(0f, sourceStealthBonus * 0.08f);
        float hearingScore = perceptionBonus + (effectiveSignal * 12f) - stealthPenalty;

        return hearingScore >= 8f;
    }

    public static bool CanHear(CreatureStats listener, CreatureStats source)
    {
        if (listener == null || source == null || listener == source) return false;
        if (IsDeafened(listener)) return false;
        if (IsPointSilenced(listener.GlobalPosition) || IsPointSilenced(source.GlobalPosition)) return false;
        if (source.MyEffects != null && source.MyEffects.HasCondition(Condition.Silenced)) return false;
		
        var stealthController = source.GetNodeOrNull<StealthController>("StealthController");
        bool isHiddenAndStill = stealthController != null && stealthController.IsRemainingStill;
        if (isHiddenAndStill)
        {
            // Hiding means staying still and producing no meaningful noise.
            return false;
        }

        bool sneaking = stealthController != null && stealthController.IsActivelyHiding;
        float intensity = SoundProfileFactory.EstimateActionLoudness(source, sneaking ? SoundActionType.Sneak : SoundActionType.Move, sneaking);
        var inferredSound = new SoundEvent(source, source.GlobalPosition, intensity, 1.5f, SoundEventType.Movement);
        return CanHear(listener, inferredSound);
    }

    public static List<HeardSoundContact> GetHeardContacts(CreatureStats listener, bool enemiesOnly = true)
    {
        PruneExpired();
        var contacts = new List<HeardSoundContact>();
        if (listener == null) return contacts;
        if (IsDeafened(listener)) return contacts;
		
        foreach (var sound in activeEvents)
        {
            if (!CanHear(listener, sound)) continue;

            if (enemiesOnly && sound.SourceCreature != null && sound.SourceCreature.IsInGroup("Player") == listener.IsInGroup("Player"))
            {
                continue;
            }

            float distance = listener.GlobalPosition.DistanceTo(sound.WorldPosition);
            float confidence = Mathf.Clamp(1f - (distance / 120f), 0.1f, 0.95f);
            float threatMultiplier = sound.Type switch
            {
                SoundEventType.Combat => 1.4f,
                SoundEventType.Chanting => 1.2f,
                SoundEventType.Laughter => 1.1f,
                _ => 1f
            };
            float threat = Mathf.Clamp(sound.NoiseIntensity * threatMultiplier, 0.1f, 20f);

            var sourceModifier = ResolveSensoryEmissionModifier(sound.SourceCreature);
            confidence *= sourceModifier.ConfidenceMultiplier;
            confidence = Mathf.Clamp(confidence, 0.05f, 0.95f);

            Vector3 perceivedPosition = sound.WorldPosition;
            if (sourceModifier.PositionalUncertaintyFeet > 0.01f)
            {
                perceivedPosition = ApplyPositionalAmbiguity(sound, listener, sourceModifier.PositionalUncertaintyFeet);
            }

            if (sound.IsIllusion)
            {
                bool hasVisualConfirmation = sound.SourceCreature != null && LineOfSightManager.GetVisibility(listener, sound.SourceCreature).HasLineOfSight;
                if (!hasVisualConfirmation)
                {
                    confidence *= Mathf.Clamp(1f - (sound.AgeSeconds / (sound.DurationSeconds * 1.1f)), 0f, 1f);
                }
                if (confidence <= 0.05f) continue;
            }

            contacts.Add(new HeardSoundContact(sound.SourceCreature, perceivedPosition, sound.Type, confidence, threat, sound.IsIllusion));

            CombatMemory.RecordHeardSound(listener, sound.SourceCreature, perceivedPosition, sound.Type, threat, confidence, isReliable: false);

            if (sound.SourceCreature != null)
            {
                var stateController = listener.GetNodeOrNull<CombatStateController>("CombatStateController");
                var locationStatus = confidence >= 0.55f ? LocationStatus.KnownDirection : LocationStatus.DetectedPresence;
                stateController?.UpdateEnemyLocation(sound.SourceCreature, locationStatus);
                stateController?.RegisterSoundUncertainty(sound.SourceCreature, sourceModifier.LocationDecayMultiplier, sound.DurationSeconds);
            }
        }

        return contacts
            .OrderByDescending(c => c.ThreatEstimate)
            .ThenByDescending(c => c.Confidence)
            .ToList();
    }


    public static float EstimateAudibleRangeAtBaseline(float intensity, float requiredEffectiveSignal = 0.6f)
    {
        // Solves intensity/(1 + d^2/400) >= requiredEffectiveSignal for d.
        if (intensity <= requiredEffectiveSignal) return 0f;
        float ratio = (intensity / requiredEffectiveSignal) - 1f;
        return Mathf.Sqrt(ratio * 400f);
    }

    public static float EstimateAudibleRange(CreatureStats source, SoundActionType actionType, bool isSneaking = false)
    {
        if (source == null) return 0f;
        float intensity = SoundProfileFactory.EstimateActionLoudness(source, actionType, isSneaking);
        return EstimateAudibleRangeAtBaseline(intensity);
    }

    private static void PruneExpired()
    {
        activeEvents.RemoveAll(e => e.IsExpired || (e.SourceCreature != null && !GodotObject.IsInstanceValid(e.SourceCreature)));
    }

    public static bool IsDeafened(CreatureStats listener)
   {
        if (listener == null) return false;
        bool hasCondition = listener.MyEffects != null && listener.MyEffects.HasCondition(Condition.Deafened);
        return hasCondition || listener.HasSpecialRule("Deaf");
    }

   private static bool ShouldRenderSoundCuesForPlayerListener(SoundEvent sound)
{
    var playerListener = ResolvePlayerSoundListener();
    if (playerListener == null) return false;
    if (IsDeafened(playerListener)) return false;

    return CanHear(playerListener, sound);
}


    private static Vector3 ApplyPositionalAmbiguity(SoundEvent sound, CreatureStats listener, float radiusFeet)
    {
        if (radiusFeet <= 0.01f) return sound.WorldPosition;

        Vector3 sourcePos = sound.WorldPosition;
        Vector3 toListener = listener.GlobalPosition - sourcePos;
        Vector3 basis = toListener.LengthSquared() > 0.01f ? toListener.Normalized() : Vector3.Forward;
        Vector3 tangent = basis.Cross(Vector3.Up);
        if (tangent.LengthSquared() < 0.001f) tangent = Vector3.Right;
        tangent = tangent.Normalized();

        float phase = Mathf.Abs(Mathf.Sin((sourcePos.X * 0.37f) + (sourcePos.Z * 0.19f) + (listener.GlobalPosition.X * 0.13f)));
        float offset = radiusFeet * phase;
        return sourcePos + (tangent * offset);
    }

    private static ResolvedSensoryEmissionModifier ResolveSensoryEmissionModifier(CreatureStats source)
    {
        if (source?.Template?.SensoryEmissionModifiers == null || source.Template.SensoryEmissionModifiers.Count == 0)
        {
            return ResolvedSensoryEmissionModifier.Neutral;
        }

        float intensityMultiplier = 1f;
        float minimumIntensityFraction = 0.2f;
        float positionalUncertainty = 0f;
        float positionalUncertaintyCap = 0f;
        float confidenceMultiplier = 1f;
        float locationDecayMultiplier = 1f;
        float aggregateMaximumEffectCap = 0f;

        foreach (var modifier in source.Template.SensoryEmissionModifiers)
        {
            if (modifier == null || !modifier.IsEnabled) continue;

            float strength = Mathf.Max(0f, modifier.ModifierStrength);
            float effectiveStrength = 1f - Mathf.Exp(-strength);

            aggregateMaximumEffectCap = Mathf.Max(
                aggregateMaximumEffectCap,
                Mathf.Clamp(modifier.MaximumEffectCap, 0f, 0.95f));

            intensityMultiplier *= Mathf.Lerp(1f, modifier.NoiseIntensityMultiplier, effectiveStrength);
            minimumIntensityFraction = Mathf.Max(minimumIntensityFraction, Mathf.Clamp(modifier.MinimumIntensityFraction, 0.05f, 0.95f));

            positionalUncertainty += Mathf.Max(0f, modifier.PositionalUncertaintyFeet) * effectiveStrength;
            positionalUncertaintyCap = Mathf.Max(positionalUncertaintyCap, Mathf.Max(0f, modifier.MaxPositionalUncertaintyFeet));

            confidenceMultiplier *= Mathf.Lerp(1f, modifier.SoundOnlyConfidenceMultiplier, effectiveStrength);
            locationDecayMultiplier *= Mathf.Lerp(1f, modifier.LocationUncertaintyDecayMultiplier, effectiveStrength);
        }

        if (aggregateMaximumEffectCap > 0f)
        {
            // Apply a post-aggregation cap to prevent multiplicative stacking exploits.
            // Example: 0.92 * 0.92 * 0.92 can no longer exceed the configured max reduction.
            float intensityReduction = Mathf.Clamp(1f - intensityMultiplier, 0f, 1f);
            intensityMultiplier = 1f - Mathf.Min(intensityReduction, aggregateMaximumEffectCap);

            float confidenceReduction = Mathf.Clamp(1f - confidenceMultiplier, 0f, 1f);
            confidenceMultiplier = 1f - Mathf.Min(confidenceReduction, aggregateMaximumEffectCap);

            float decayIncrease = Mathf.Max(0f, locationDecayMultiplier - 1f);
            locationDecayMultiplier = 1f + Mathf.Min(decayIncrease, aggregateMaximumEffectCap);
        }

        if (positionalUncertaintyCap > 0f)
        {
            positionalUncertainty = Mathf.Clamp(positionalUncertainty, 0f, positionalUncertaintyCap);
        }

        intensityMultiplier = Mathf.Clamp(intensityMultiplier, 0.05f, 1f);
        confidenceMultiplier = Mathf.Clamp(confidenceMultiplier, 0.5f, 1f);
        locationDecayMultiplier = Mathf.Max(1f, locationDecayMultiplier);

        return new ResolvedSensoryEmissionModifier(intensityMultiplier, minimumIntensityFraction, positionalUncertainty, confidenceMultiplier, locationDecayMultiplier);
    }

    private static CreatureStats ResolvePlayerSoundListener()
    {
        var turnManager = TurnManager.Instance;
        var playerLeader = turnManager?.GetPlayerLeader();
        if (playerLeader != null && GodotObject.IsInstanceValid(playerLeader)) return playerLeader;

        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return null;

        foreach (var node in tree.GetNodesInGroup("Player"))
        {
            if (node is CreatureStats player
                && GodotObject.IsInstanceValid(player)
                && player.GetNodeOrNull<PlayerActionController>("PlayerActionController") != null)
            {
                return player;
            }
        }

        return null;
    }
}