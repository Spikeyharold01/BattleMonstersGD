using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: CombatMemory.cs (GODOT VERSION)
// PURPOSE: Tracks the state of the current battle for AI analysis.
// ATTACH TO: Do not attach (Static Class).
// =================================================================================================
/// <summary>
/// Defines the health states a creature can be in, as perceived by Deathwatch.
/// </summary>
public enum HealthStatus { Unknown, Healthy, Wounded, Fragile, Undead, Construct, Dead }
public static class CombatMemory
{
// --- PRIVATE STATIC FIELDS ---
// Tracks which specific creatures have been observed to have Spell Resistance in this combat.
private static HashSet<CreatureStats> creaturesWithConfirmedSR = new HashSet<CreatureStats>();

private static HashSet<CreatureStats> soulBoundVictims = new HashSet<CreatureStats>();

// Key: (Attacker, Defender). Value: True if saved successfully, False if failed.
// ValueTuple is supported in modern C# / Godot .NET
private static Dictionary<(CreatureStats, CreatureStats), bool> sanctuarySaveResults = new Dictionary<(CreatureStats, CreatureStats), bool>();

public static void RecordSanctuaryResult(CreatureStats attacker, CreatureStats defender, bool success)
{
    var key = (attacker, defender);
    sanctuarySaveResults[key] = success;
}

public static bool? GetSanctuaryResult(CreatureStats attacker, CreatureStats defender)
{
    var key = (attacker, defender);
    if (sanctuarySaveResults.TryGetValue(key, out bool result)) return result;
    return null; // No attempt made yet
}

// Tracks player threat levels (damage dealt).
private static Dictionary<CreatureStats, int> playerThreatLevels = new Dictionary<CreatureStats, int>();
// Tracks failed Intimidate attempts. Key1: Intimidator, Key2: Target, Value: Failure Count
private static Dictionary<CreatureStats, Dictionary<CreatureStats, int>> intimidateRetryCounters = new Dictionary<CreatureStats, Dictionary<CreatureStats, int>>();
// Tracks adaptive resistance types.
private static Dictionary<CreatureStats, string> knownAdaptiveResistances = new Dictionary<CreatureStats, string>();
// Tracks offensive actions for Bluff.
private static HashSet<CreatureStats> creaturesThatHaveActedOffensively = new HashSet<CreatureStats>();
// Key: Observer, Value: HashSet of creatures whose illusions they have disbelieved.
private static Dictionary<CreatureStats, HashSet<CreatureStats>> disbelievedIllusions = new Dictionary<CreatureStats, HashSet<CreatureStats>>();
// Tracks identified traits.
private static Dictionary<CreatureStats, HashSet<string>> identifiedCreatureTraits = new Dictionary<CreatureStats, HashSet<string>>();
private static Dictionary<CreatureStats, HashSet<Ability_SO>> identifiedSpells = new Dictionary<CreatureStats, HashSet<Ability_SO>>();
// Tracks magical auras.
private static Dictionary<CreatureStats, AuraStrength> knownMagicalAuras = new Dictionary<CreatureStats, AuraStrength>();
// Tracks health statuses via Deathwatch.
private static Dictionary<CreatureStats, HealthStatus> knownHealthStatuses = new Dictionary<CreatureStats, HealthStatus>();

// Tracks known alignments.
private static Dictionary<CreatureStats, string> knownAlignments = new Dictionary<CreatureStats, string>();
// Tracks enemies detected by Scent.
private static Dictionary<CreatureStats, HashSet<CreatureStats>> smelledEnemies = new Dictionary<CreatureStats, HashSet<CreatureStats>>();

// Tracks if a creature has checked scent direction this turn.
private static Dictionary<CreatureStats, bool> hasCheckedScentDirection = new Dictionary<CreatureStats, bool>();


private static Dictionary<CreatureStats, BehaviorTag> knownBehaviors = new Dictionary<CreatureStats, BehaviorTag>();

private static Dictionary<CreatureStats, List<HeardMemoryEntry>> heardSoundMemory = new Dictionary<CreatureStats, List<HeardMemoryEntry>>();
private static Dictionary<CreatureStats, List<ScentMemoryEntry>> smelledScentMemory = new Dictionary<CreatureStats, List<ScentMemoryEntry>>();
private static Dictionary<CreatureStats, ScentEvent> lastScentByObserver = new Dictionary<CreatureStats, ScentEvent>();
private static Dictionary<CreatureStats, float> scentSuspicionByObserver = new Dictionary<CreatureStats, float>();

public readonly struct HeardMemoryEntry
{
    public readonly CreatureStats Source;
    public readonly Vector3 Position;
    public readonly SoundEventType Type;
    public readonly float ThreatEstimate;
    public readonly float Confidence;
    public readonly bool IsReliable;

    public HeardMemoryEntry(CreatureStats source, Vector3 position, SoundEventType type, float threatEstimate, float confidence, bool isReliable)
    {
        Source = source;
        Position = position;
        Type = type;
        ThreatEstimate = threatEstimate;
        Confidence = confidence;
        IsReliable = isReliable;
    }
}

public readonly struct ScentMemoryEntry
{
    public readonly CreatureStats Source;
    public readonly Vector3 Position;
    public readonly ScentType Type;
    public readonly float Confidence;
    public readonly float Suspicion;
    public readonly bool IsTrail;

    public ScentMemoryEntry(CreatureStats source, Vector3 position, ScentType type, float confidence, float suspicion, bool isTrail)
    {
        Source = source;
        Position = position;
        Type = type;
        Confidence = confidence;
        Suspicion = suspicion;
        IsTrail = isTrail;
    }
}

    public static void RecordBehaviorTag(CreatureStats creature, BehaviorTag tag)
    {
        if (creature == null) return;
        knownBehaviors[creature] = tag;
        GD.Print($"[Combat Memory] Identified {creature.Name} behavior as {tag}.");
    }

    public static BehaviorTag GetKnownBehavior(CreatureStats creature)
    {
        if (knownBehaviors.TryGetValue(creature, out var tag)) return tag;
        return BehaviorTag.Unknown;
    }

public static void RecordSmelledEnemy(CreatureStats observer, CreatureStats target)
{
    if (!smelledEnemies.ContainsKey(observer)) smelledEnemies[observer] = new HashSet<CreatureStats>();
    smelledEnemies[observer].Add(target);
}

public static void MarkScentDirectionChecked(CreatureStats observer)
{
    hasCheckedScentDirection[observer] = true;
}

public static bool HasCheckedScentDirection(CreatureStats observer)
{
    return hasCheckedScentDirection.ContainsKey(observer) && hasCheckedScentDirection[observer];
}

public static List<CreatureStats> GetSmelledEnemies(CreatureStats observer)
{
    if (smelledEnemies.ContainsKey(observer)) return smelledEnemies[observer].ToList();
    return new List<CreatureStats>();
}

public static void RecordAlignment(CreatureStats creature, string alignment)
{
    if (creature == null) return;
    if (!knownAlignments.ContainsKey(creature))
    {
        knownAlignments[creature] = alignment;
        GD.Print($"[Combat Memory] Identified {creature.Name} as {alignment}.");
    }
}
public static bool IsKnownToBeChaotic(CreatureStats creature)
{
if (knownAlignments.TryGetValue(creature, out string align))
{
return align.Contains("Chaos") || align.Contains("Chaotic") || align.Contains("CE") || align.Contains("CN") || align.Contains("CG");
}
return false;
}


public static bool IsKnownToBeEvil(CreatureStats creature)
{
    if (knownAlignments.TryGetValue(creature, out string align))
    {
        return align.Contains("Evil") || align.Contains("LE") || align.Contains("NE") || align.Contains("CE");
    }
    return false;
}

public static bool IsKnownToBeGood(CreatureStats creature)
{
    if (knownAlignments.TryGetValue(creature, out string align))
    {
        return align.Contains("Good") || align.Contains("LG") || align.Contains("NG") || align.Contains("CG");
    }
    return false;
}

public static bool IsKnownToBeLawful(CreatureStats creature)
{
    if (knownAlignments.TryGetValue(creature, out string align))
    {
        return align.Contains("Law") || align.Contains("LG") || align.Contains("LN") || align.Contains("LE");
    }
    return false;
}

// Tracks sentience (Int >= 3).
private static Dictionary<CreatureStats, bool> knownSentience = new Dictionary<CreatureStats, bool>();

public static void RecordSentience(CreatureStats creature, bool isSentient)
{
    if (creature == null) return;
    if (!knownSentience.ContainsKey(creature))
    {
        knownSentience[creature] = isSentient;
        string type = isSentient ? "Sentient" : "Non-Sentient";
        GD.Print($"[Combat Memory] Thoughtsense identified {creature.Name} as {type}.");
    }
}

public static bool? IsKnownToBeSentient(CreatureStats creature)
{
    if (knownSentience.TryGetValue(creature, out bool sentient)) return sentient;
    return null; // Unknown
}

// --- PUBLIC STATIC PROPERTIES ---

public static float PlayerAggressionIndex { get; private set; } = 0.5f;
public static float PlayerDebuffReliance { get; private set; } = 0.5f;


// --- PUBLIC STATIC METHODS ---

public static void RecordPlayerDamage(CreatureStats player, int damageDealt)
{
    if (playerThreatLevels.ContainsKey(player))
    {
        playerThreatLevels[player] += damageDealt;
    }
    else
    {
        playerThreatLevels.Add(player, damageDealt);
    }
}

public static CreatureStats GetHighestThreat()
{
    if (playerThreatLevels.Count == 0) return null;

    // Key is CreatureStats (Node)
    var livingThreats = playerThreatLevels.Where(p => GodotObject.IsInstanceValid(p.Key));

    if(!livingThreats.Any()) return null;

    return livingThreats.OrderByDescending(p => p.Value).First().Key;
}

 public static event Action<CreatureStats> OnOffensiveActionRecorded;

    public static void RecordOffensiveAction(CreatureStats creature)
    {
        if (creature != null)
        {
            creaturesThatHaveActedOffensively.Add(creature);
            OnOffensiveActionRecorded?.Invoke(creature);
        }
    }

public static bool HasActedOffensively(CreatureStats creature)
{
    return creaturesThatHaveActedOffensively.Contains(creature);
}

public static void RecordIdentifiedSpell(CreatureStats caster, Ability_SO spell)
{
    if (caster == null || spell == null) return;
    if (!identifiedSpells.ContainsKey(caster))
    {
        identifiedSpells[caster] = new HashSet<Ability_SO>();
    }
    identifiedSpells[caster].Add(spell);
}

public static bool IsSpellKnown(CreatureStats caster, Ability_SO spell)
{
    return identifiedSpells.ContainsKey(caster) && identifiedSpells[caster].Contains(spell);
}

public static void RecordIntimidateFailure(CreatureStats intimidator, CreatureStats target)
{
    if (intimidator == null || target == null) return;

    if (!intimidateRetryCounters.ContainsKey(intimidator))
    {
        intimidateRetryCounters[intimidator] = new Dictionary<CreatureStats, int>();
    }

    if (!intimidateRetryCounters[intimidator].ContainsKey(target))
    {
        intimidateRetryCounters[intimidator][target] = 0;
    }

    intimidateRetryCounters[intimidator][target]++;
    GD.Print($"[Combat Memory] Recorded Intimidate failure from {intimidator.Name} to {target.Name}. Next DC will be +{intimidateRetryCounters[intimidator][target] * 5}.");
}

public static int GetIntimidateRetryPenalty(CreatureStats intimidator, CreatureStats target)
{
    if (intimidator != null && target != null && intimidateRetryCounters.ContainsKey(intimidator) && intimidateRetryCounters[intimidator].ContainsKey(target))
    {
        return intimidateRetryCounters[intimidator][target] * 5;
    }
    return 0;
}

public static void RecordSRSuccess(CreatureStats creature)
{
    if (creature != null && !creaturesWithConfirmedSR.Contains(creature))
    {
        creaturesWithConfirmedSR.Add(creature);
        GD.Print($"[Combat Memory] Recorded that {creature.Name} has Spell Resistance.");
    }
}

public static bool IsKnownToHaveSR(CreatureStats creature)
{
    return creaturesWithConfirmedSR.Contains(creature);
}

public static void RecordMagicalAura(CreatureStats creature, AuraStrength strength)
{
    if (creature == null) return;

    if (knownMagicalAuras.ContainsKey(creature))
    {
        if (strength > knownMagicalAuras[creature])
        {
            knownMagicalAuras[creature] = strength;
        }
    }
    else
    {
        knownMagicalAuras.Add(creature, strength);
    }
    GD.Print($"[Combat Memory] Recorded that {creature.Name} has a {strength} magical aura.");
}

public static bool IsKnownToBeMagical(CreatureStats creature)
{
    return knownMagicalAuras.ContainsKey(creature);
}

public static AuraStrength GetKnownAuraStrength(CreatureStats creature)
{
    if (creature != null && knownMagicalAuras.TryGetValue(creature, out AuraStrength strength))
    {
        return strength;
    }
    return AuraStrength.Dim;
}

public static void RecordIdentifiedTrait(CreatureStats creature, string trait)
{
    if (creature == null || string.IsNullOrEmpty(trait)) return;

    if (!identifiedCreatureTraits.ContainsKey(creature))
    {
        identifiedCreatureTraits[creature] = new HashSet<string>();
    }

    if (identifiedCreatureTraits[creature].Add(trait))
    {
        GD.Print($"[Combat Memory] Recorded that {creature.Name} has trait: {trait}.");
    }
}

public static bool IsTraitIdentified(CreatureStats creature, string trait)
{
    if (creature == null) return false;

    return identifiedCreatureTraits.ContainsKey(creature) && identifiedCreatureTraits[creature].Contains(trait);
}

public static HashSet<string> GetIdentifiedTraits(CreatureStats creature)
{
    if (identifiedCreatureTraits.TryGetValue(creature, out var traits))
    {
        return traits;
    }
    return new HashSet<string>();
}

private static HashSet<CreatureStats> knownMythicCreatures = new HashSet<CreatureStats>();

public static void RecordMythicStatus(CreatureStats creature)
{
    if (knownMythicCreatures.Add(creature))
    {
        GD.Print($"[Combat Memory] Recorded that {creature.Name} is a mythic threat!");
    }
}

public static bool IsKnownToBeMythic(CreatureStats creature)
{
    return knownMythicCreatures.Contains(creature);
}

public static void RecordHealthStatus(CreatureStats creature, HealthStatus status)
{
    if (creature == null) return;
    knownHealthStatuses[creature] = status;
}

public static HealthStatus GetKnownHealthStatus(CreatureStats creature)
{
    if (creature != null && knownHealthStatuses.TryGetValue(creature, out HealthStatus status))
    {
        return status;
    }
    return HealthStatus.Unknown;
}

public static void RecordAdaptiveResistanceState(CreatureStats creature, string damageType)
{
    if (creature == null) return;
    knownAdaptiveResistances[creature] = damageType;
    GD.Print($"[Combat Memory] Recorded that {creature.Name}'s adaptive resistance is now set to {damageType}.");
}

public static string GetKnownAdaptiveResistance(CreatureStats creature)
{
    if (creature != null && knownAdaptiveResistances.TryGetValue(creature, out string type))
    {
        return type;
    }
    return "";
}

public static void RecordPlayerAttackAction()
{
    PlayerAggressionIndex = Mathf.Lerp(PlayerAggressionIndex, 1.0f, 0.05f);
}

public static void RecordPlayerDebuffAction()
{
    PlayerDebuffReliance = Mathf.Lerp(PlayerDebuffReliance, 1.0f, 0.05f);
}

public static void DecayStyleMemory()
{
    PlayerAggressionIndex = Mathf.Lerp(PlayerAggressionIndex, 0.5f, 0.01f);
    PlayerDebuffReliance = Mathf.Lerp(PlayerDebuffReliance, 0.5f, 0.01f);
}

public static void RecordDisbelief(CreatureStats observer, CreatureStats targetOfIllusion)
{
    if (observer == null || targetOfIllusion == null) return;

    if (!disbelievedIllusions.ContainsKey(observer))
    {
        disbelievedIllusions[observer] = new HashSet<CreatureStats>();
    }

    disbelievedIllusions[observer].Add(targetOfIllusion);
    GD.Print($"[Combat Memory] Recorded that {observer.Name} has disbelieved the illusion on {targetOfIllusion.Name}.");
}

public static bool HasDisbelievedIllusion(CreatureStats observer, CreatureStats targetOfIllusion)
{
    return disbelievedIllusions.ContainsKey(observer) && disbelievedIllusions[observer].Contains(targetOfIllusion);
}

public static void RecordSoulBound(CreatureStats victim)
{
    if (!soulBoundVictims.Contains(victim)) soulBoundVictims.Add(victim);
}

public static bool WasSoulBound(CreatureStats victim)
{
    return soulBoundVictims.Contains(victim);
}


public static void RecordHeardSound(CreatureStats listener, CreatureStats source, Vector3 location, SoundEventType type, float threatEstimate, float confidence, bool isReliable)
{
    if (listener == null) return;
    if (!heardSoundMemory.ContainsKey(listener))
    {
        heardSoundMemory[listener] = new List<HeardMemoryEntry>();
    }

    heardSoundMemory[listener].Add(new HeardMemoryEntry(source, location, type, threatEstimate, confidence, isReliable));

    if (heardSoundMemory[listener].Count > 20)
    {
        heardSoundMemory[listener].RemoveAt(0);
    }
}

public static List<HeardMemoryEntry> GetHeardSounds(CreatureStats listener)
{
    if (listener == null || !heardSoundMemory.ContainsKey(listener)) return new List<HeardMemoryEntry>();
    return heardSoundMemory[listener];
}

public static void RecordScent(CreatureStats listener, ScentEvent scent, float confidence, float suspicion)
{
    if (listener == null) return;

    if (!smelledScentMemory.ContainsKey(listener))
    {
        smelledScentMemory[listener] = new List<ScentMemoryEntry>();
    }

    smelledScentMemory[listener].Add(new ScentMemoryEntry(scent.SourceCreature, scent.Position, scent.Type, confidence, suspicion, scent.IsTrail));
    if (smelledScentMemory[listener].Count > 25)
    {
        smelledScentMemory[listener].RemoveAt(0);
    }

    lastScentByObserver[listener] = scent;
    scentSuspicionByObserver[listener] = suspicion;
}

public static List<ScentMemoryEntry> GetScentMemories(CreatureStats listener)
{
    if (listener == null || !smelledScentMemory.ContainsKey(listener)) return new List<ScentMemoryEntry>();
    return smelledScentMemory[listener];
}

public static bool TryGetLastScent(CreatureStats listener, out ScentEvent scent)
{
    if (listener != null && lastScentByObserver.TryGetValue(listener, out scent)) return true;
    scent = default;
    return false;
}

public static float GetScentSuspicion(CreatureStats listener)
{
    if (listener != null && scentSuspicionByObserver.TryGetValue(listener, out float value)) return value;
    return 0f;
}

public static void ResetMemory()
{
    playerThreatLevels.Clear();
    PlayerAggressionIndex = 0.5f;
    PlayerDebuffReliance = 0.5f;
	intimidateRetryCounters.Clear();
	creaturesThatHaveActedOffensively.Clear();
	creaturesWithConfirmedSR.Clear();
	knownAdaptiveResistances.Clear();
	knownHealthStatuses.Clear();
	identifiedSpells.Clear();
	knownMagicalAuras.Clear();
	smelledEnemies.Clear();
	hasCheckedScentDirection.Clear();
	knownSentience.Clear();
	sanctuarySaveResults.Clear();
	heardSoundMemory.Clear();
	smelledScentMemory.Clear();
	lastScentByObserver.Clear();
	scentSuspicionByObserver.Clear();
}
}
