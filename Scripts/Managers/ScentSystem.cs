using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: ScentSystem.cs (GODOT VERSION)
// PURPOSE: Central resolver for scent-based perception.
// ATTACH TO: Do not attach (Static system).
// =================================================================================================
public static class ScentSystem
{
    private static readonly List<ScentEvent> activeEvents = new();

    private enum ScentSenseProfile
    {
        None,
        Scent,
        AcuteScent,
        BlindsenseScent,
        BlindsightScent,
        AquaticScent,
        TremorScentHybrid,
        EtherealScent,
        LifeSense,
        DeathSense,
        FireScent,
        PoisonScent,
        BloodScent
    }

    public static void Reset()
    {
        activeEvents.Clear();
    }

    public static ScentEvent EmitScent(
        CreatureStats source,
        Vector3 position,
        float intensity,
        float decayRate,
        float verticalBias,
        ScentType type,
        bool isTrail = false,
        float durationSeconds = 6f)
    {
        int sourceSize = source != null && source.Template != null ? (int)source.Template.Size : (int)CreatureSize.Medium;
        var scent = new ScentEvent(source, position, intensity, decayRate, verticalBias, type, sourceSize, isTrail, durationSeconds);
        activeEvents.Add(scent);
        ScentGlyphRenderer.TryRender(scent);
        PruneExpired();
        return scent;
    }

   public static void EmitCreatureScent(CreatureStats source, bool isTrail = true)
    {
        if (source == null || source.Template == null) return;
        
        if (source.MyEffects != null && source.MyEffects.ActiveEffects.Any(e => !e.IsSuppressed && e.EffectData.SuppressesScentTrails))
        {
            isTrail = false;
        }

        float intensity = GetBaseScentIntensity(source);
        if (intensity <= 0.01f) return;

        ScentType type = GetDominantCreatureScentType(source);
        float verticalBias = GetVerticalBiasForSource(source, type);
        float duration = isTrail ? 8f : 4f;

        EmitScent(source, source.GlobalPosition, intensity, decayRate: isTrail ? 0.18f : 0.25f, verticalBias: verticalBias, type: type, isTrail: isTrail, durationSeconds: duration);
    }

    public static bool CanSmell(CreatureStats listener, ScentEvent scent, out bool isPinpointed)
    {
        isPinpointed = false;
        if (listener == null || listener.Template == null || scent.IsExpired) return false;

        var profile = ResolveSenseProfile(listener);
        if (profile == ScentSenseProfile.None) return false;
        if (!IsScentTypeSupported(profile, scent.Type)) return false;

        float range = ResolveRange(listener, profile);
        if (range <= 0f) return false;

        Vector3 delta = scent.Position - listener.GlobalPosition;
        float horizontalDistance = new Vector2(delta.X, delta.Z).Length();
        float verticalDistance = Mathf.Abs(delta.Y);

        if (profile == ScentSenseProfile.AquaticScent)
        {
            bool listenerInWater = IsInWater(listener.GlobalPosition);
            bool scentInWater = IsInWater(scent.Position);
            if (!listenerInWater || !scentInWater) return false;
            verticalDistance *= 2.0f;
        }

        if (profile == ScentSenseProfile.TremorScentHybrid)
        {
            if (verticalDistance > 1.5f) return false;
            if (!HasGrounding(listener) || !HasGrounding(scent.SourceCreature)) return false;
        }

        float verticalWeight = 1f + (Mathf.Sign(delta.Y) * scent.VerticalBias * 0.5f);
        float effectiveDistance = horizontalDistance + (verticalDistance * verticalWeight);
        if (effectiveDistance > range * 2f) return false;

        float falloff = 1f / (1f + ((effectiveDistance * effectiveDistance) / Mathf.Max(25f, range * range)));
        float sizeBoost = 1f + ((scent.SourceSize - (int)CreatureSize.Medium) * 0.12f);
        float signal = scent.Intensity * scent.Freshness * Mathf.Max(0.3f, sizeBoost) * falloff;

        if (IsUpwind(listener, scent.Position))
        {
            signal *= LineOfSightManager.SCENT_MULTIPLIER_UPWIND;
        }
        else
        {
            signal *= LineOfSightManager.SCENT_MULTIPLIER_DOWNWIND;
        }

        float perception = listener.GetPerceptionBonus() / 12f;
        float threshold = 0.24f + Mathf.Max(0f, (0.5f - perception) * 0.2f);
        bool detected = signal >= threshold;
        if (!detected) return false;

        bool scentBlindsight = profile == ScentSenseProfile.BlindsightScent;
        bool closePinpoint = effectiveDistance <= Mathf.Max(5f, range * 0.15f);
        isPinpointed = scentBlindsight || closePinpoint;
        return true;
    }

    public static List<SmelledScentContact> GetSmelledContacts(CreatureStats listener, bool enemiesOnly = true, bool includeEnvironmentalScents = false)
    {
        PruneExpired();
        var contacts = new List<SmelledScentContact>();
        if (listener == null) return contacts;

        foreach (var scent in activeEvents)
        {
            if (!CanSmell(listener, scent, out bool pinpointed)) continue;
            if (!includeEnvironmentalScents && IsEnvironmentalScent(scent)) continue;

            if (enemiesOnly && scent.SourceCreature != null && scent.SourceCreature.IsInGroup("Player") == listener.IsInGroup("Player"))
            {
                continue;
            }

            float distance = listener.GlobalPosition.DistanceTo(scent.Position);
            float confidence = Mathf.Clamp((scent.Freshness * 0.6f) + (1f - (distance / 120f)) * 0.4f, 0.1f, 0.98f);
            float suspicion = Mathf.Clamp(scent.Intensity * (scent.IsTrail ? 0.9f : 1.2f), 0.1f, 25f);

            contacts.Add(new SmelledScentContact(scent.SourceCreature, scent.Position, scent.Type, confidence, suspicion, pinpointed));
            CombatMemory.RecordScent(listener, scent, confidence, suspicion);
        }

        return contacts
            .OrderByDescending(c => c.Suspicion)
            .ThenByDescending(c => c.Confidence)
            .ToList();
    }

    public static float GetBaseScentIntensity(CreatureStats creature)
    {
        if (creature == null || creature.Template == null) return 0f;
		if (creature.MyEffects != null && creature.MyEffects.HasCondition(Condition.Incorporeal)) return 0f;
        if (creature.Template.SubTypes != null && creature.Template.SubTypes.Contains("Incorporeal")) return 0f;

        float baseValue = creature.Template.Type switch
        {
            CreatureType.Undead => 1.2f,
            CreatureType.Dragon => 1.0f,
            CreatureType.Animal => 0.7f,
            CreatureType.MagicalBeast => 0.8f,
            CreatureType.Humanoid => 0.4f,
            CreatureType.Plant => 0.6f,
            CreatureType.Construct => 0f,
            CreatureType.Ooze => 1.1f,
            _ => 0.2f
        };

        if (creature.Template.MaxHP > 0 && (float)creature.CurrentHP / creature.Template.MaxHP <= 0.5f)
        {
            baseValue += 0.5f; // blood scent from wounded creatures
        }

        if (HasEffectTag(creature, EffectTag.Poison) || (creature.MyEffects != null && creature.MyEffects.HasCondition(Condition.Sickened)))
        {
            baseValue += 0.4f;
        }

        if (IsOnFire(creature))
        {
            baseValue += 0.8f;
        }

        if (IsInWater(creature.GlobalPosition))
        {
            baseValue *= 0.5f;
        }

        return Mathf.Max(0f, baseValue);
    }



    private static bool IsEnvironmentalScent(ScentEvent scent)
    {
        if (scent.SourceCreature == null) return true;
        return scent.Type == ScentType.Environment
            || scent.Type == ScentType.Fire
            || scent.Type == ScentType.Smoke
            || scent.Type == ScentType.Poison
            || scent.Type == ScentType.Magic;
    }

    private static bool IsScentTypeSupported(ScentSenseProfile profile, ScentType type)
    {
        return profile switch
        {
            ScentSenseProfile.LifeSense => type == ScentType.Creature || type == ScentType.Blood,
            ScentSenseProfile.DeathSense => type == ScentType.Undead,
            ScentSenseProfile.FireScent => type == ScentType.Fire || type == ScentType.Smoke,
            ScentSenseProfile.PoisonScent => type == ScentType.Poison,
            ScentSenseProfile.BloodScent => type == ScentType.Blood || type == ScentType.Creature,
            _ => true
        };
    }

    private static ScentSenseProfile ResolveSenseProfile(CreatureStats creature)
    {
        if (creature == null || creature.Template == null) return ScentSenseProfile.None;

        if (creature.Template.HasLifesense) return ScentSenseProfile.LifeSense;
        if (creature.Template.Type == CreatureType.Undead && creature.Template.HasScent) return ScentSenseProfile.DeathSense;
        if (creature.Template.SpecialQualities.Any(q => q.Equals("Ethereal Scent", StringComparison.OrdinalIgnoreCase))) return ScentSenseProfile.EtherealScent;
        if (creature.Template.SpecialQualities.Any(q => q.Equals("Aquatic Scent", StringComparison.OrdinalIgnoreCase))) return ScentSenseProfile.AquaticScent;
        if (creature.Template.SpecialQualities.Any(q => q.Equals("Tremor/Scent Hybrid", StringComparison.OrdinalIgnoreCase))) return ScentSenseProfile.TremorScentHybrid;
        if (creature.Template.SpecialQualities.Any(q => q.Equals("Fire Scent", StringComparison.OrdinalIgnoreCase))) return ScentSenseProfile.FireScent;
        if (creature.Template.SpecialQualities.Any(q => q.Equals("Poison Scent", StringComparison.OrdinalIgnoreCase))) return ScentSenseProfile.PoisonScent;
        if (creature.Template.SpecialQualities.Any(q => q.Equals("Blood Scent", StringComparison.OrdinalIgnoreCase))) return ScentSenseProfile.BloodScent;
        if (creature.Template.HasBlindsight && creature.Template.SpecialQualities.Any(q => q.Contains("Scent", StringComparison.OrdinalIgnoreCase))) return ScentSenseProfile.BlindsightScent;
        if (creature.Template.SpecialQualities.Any(q => q.Equals("Blindsense (Scent)", StringComparison.OrdinalIgnoreCase))) return ScentSenseProfile.BlindsenseScent;
        if (creature.Template.SpecialQualities.Any(q => q.Equals("Acute Scent", StringComparison.OrdinalIgnoreCase))) return ScentSenseProfile.AcuteScent;
        if (creature.Template.HasScent) return ScentSenseProfile.Scent;

        return ScentSenseProfile.None;
    }

    private static float ResolveRange(CreatureStats creature, ScentSenseProfile profile)
    {
        float baseRange = creature.Template.ScentRange > 0 ? creature.Template.ScentRange : 30f;
        return profile switch
        {
            ScentSenseProfile.AcuteScent => Mathf.Max(baseRange, 60f),
            ScentSenseProfile.BlindsightScent => Mathf.Max(baseRange, 60f),
            ScentSenseProfile.BlindsenseScent => Mathf.Max(baseRange, 30f),
            ScentSenseProfile.BloodScent => Mathf.Max(baseRange, 60f),
            ScentSenseProfile.AquaticScent => Mathf.Max(baseRange, 60f),
            ScentSenseProfile.LifeSense => Mathf.Max(baseRange, creature.Template.LifesenseRange > 0 ? creature.Template.LifesenseRange : 60f),
            ScentSenseProfile.DeathSense => Mathf.Max(baseRange, 60f),
            ScentSenseProfile.FireScent => Mathf.Max(baseRange, 60f),
            _ => baseRange
        };
    }

    private static ScentType GetDominantCreatureScentType(CreatureStats creature)
    {
        if (creature.Template.Type == CreatureType.Undead) return ScentType.Undead;
        if (creature.Template.MaxHP > 0 && (float)creature.CurrentHP / creature.Template.MaxHP <= 0.5f) return ScentType.Blood;
        if (HasEffectTag(creature, EffectTag.Poison) || (creature.MyEffects != null && creature.MyEffects.HasCondition(Condition.Sickened))) return ScentType.Poison;
        if (IsOnFire(creature)) return ScentType.Fire;
        return ScentType.Creature;
    }

    private static float GetVerticalBiasForSource(CreatureStats source, ScentType type)
    {
        if (type == ScentType.Fire || type == ScentType.Smoke) return 0.75f;
        if (source.Template.Speed_Burrow > 0f) return -0.6f;
        if (source.Template.Speed_Fly > 0f) return 0.35f;
        return 0f;
    }

    private static bool HasGrounding(CreatureStats creature)
    {
        if (creature == null) return false;
        if (creature.Template.Speed_Fly > 0f) return false;
        return !IsInWater(creature.GlobalPosition);
    }

    private static bool IsOnFire(CreatureStats creature)
    {
        if (creature == null || creature.MyEffects == null) return false;
        return creature.MyEffects.ActiveEffects.Any(e =>
            !e.IsSuppressed
            && e.EffectData != null
            && (e.EffectData.EffectName.Contains("Burn", StringComparison.OrdinalIgnoreCase)
                || e.EffectData.EffectName.Contains("Fire", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool HasEffectTag(CreatureStats creature, EffectTag tag)
    {
        if (creature == null || creature.MyEffects == null) return false;
        return creature.MyEffects.ActiveEffects.Any(e => !e.IsSuppressed && e.EffectData != null && e.EffectData.Tag == tag);
    }

    private static bool IsInWater(Vector3 position)
    {
        var node = GridManager.Instance?.NodeFromWorldPoint(position);
        return node != null && node.terrainType == TerrainType.Water;
    }

    private static bool IsUpwind(CreatureStats listener, Vector3 sourcePosition)
    {
        var weather = WeatherManager.Instance;
        if (weather == null) return true;

        if (weather.CurrentWindDirection == Vector3.Zero) return true;

        Vector3 toSource = (sourcePosition - listener.GlobalPosition).Normalized();
        float dot = toSource.Dot(weather.CurrentWindDirection.Normalized());
        return dot < 0f;
    }

    private static void PruneExpired()
    {
        activeEvents.RemoveAll(e => e.IsExpired || (e.SourceCreature != null && !GodotObject.IsInstanceValid(e.SourceCreature)));
    }
}