using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: CombatStateController.cs (GODOT VERSION)
// PURPOSE: Manages a creature's real-time combat state, such as tracking the location of enemies.
// ATTACH TO: ALL creature prefabs (Child Node).
// =================================================================================================
/// <summary>
/// Defines how well a creature knows the location of another creature.
/// </summary>
public enum LocationStatus
{
Unknown, // Location is not known.
KnownDirection, // The general direction is known (e.g., from a ranged attack).
DetectedPresence, // Presence is known from non-visual senses, but direction is uncertain.
KnownSquare, // The specific 5-foot square is known, but not the exact position (still has total concealment).
Pinpointed // The exact location is known, allowing attacks (though still subject to miss chance).
}
public partial class CombatStateController : GridNode
{
private CreatureStats myStats;

// Tracks creatures this combatant has visually confirmed during the current combat.
public HashSet<CreatureStats> SeenCreaturesThisCombat { get; private set; } = new HashSet<CreatureStats>();

// The core dictionary for tracking the location of other combatants.
public Dictionary<CreatureStats, LocationStatus> EnemyLocationStates { get; private set; }
private readonly Dictionary<CreatureStats, double> locationStatusTimestamps = new Dictionary<CreatureStats, double>();
private readonly Dictionary<CreatureStats, double> soundUncertaintyBoostExpiry = new Dictionary<CreatureStats, double>();
private readonly Dictionary<CreatureStats, float> soundUncertaintyDecayMultipliers = new Dictionary<CreatureStats, float>();

private const double KnownSquareBaseDecaySeconds = 4.5;
private const double KnownDirectionBaseDecaySeconds = 4.0;
private const double DetectedPresenceBaseDecaySeconds = 3.0;

public override void _Ready()
{
    myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
    EnemyLocationStates = new Dictionary<CreatureStats, LocationStatus>();
}

/// <summary>
/// Called by the TurnManager at the start of combat to initialize the tracking dictionary.
/// </summary>
public void OnCombatStart(List<CreatureStats> allCombatants)
{
    EnemyLocationStates.Clear();
	   SeenCreaturesThisCombat.Clear();
    locationStatusTimestamps.Clear();
    soundUncertaintyBoostExpiry.Clear();
    soundUncertaintyDecayMultipliers.Clear();
    foreach (var combatant in allCombatants)
    {
        if (combatant != myStats)
        {
            // Initially, all other creatures are considered 'Unknown' until detected.
            EnemyLocationStates.Add(combatant, LocationStatus.Unknown);
        }
    }
}

/// <summary>
/// Marks a creature as visually seen during this match.
/// </summary>
public void MarkCreatureAsSeen(CreatureStats target)
{
    if (target != null && target != myStats)
    {
        SeenCreaturesThisCombat.Add(target);
    }
}

public bool HasSeenCreature(CreatureStats target)
{
    return target != null && SeenCreaturesThisCombat.Contains(target);
}

/// <summary>
/// Updates the location status of a specific target.
/// </summary>

public override void _Process(double delta)
{
    if (EnemyLocationStates == null || EnemyLocationStates.Count == 0) return;

    double nowSeconds = Time.GetTicksMsec() / 1000.0;
    var trackedTargets = EnemyLocationStates.Keys.ToList();

    foreach (var target in trackedTargets)
    {
        if (target == null || !EnemyLocationStates.ContainsKey(target)) continue;

        LocationStatus status = EnemyLocationStates[target];
        if (status == LocationStatus.Pinpointed || status == LocationStatus.Unknown) continue;

        if (!locationStatusTimestamps.TryGetValue(target, out double lastTimestamp))
        {
            locationStatusTimestamps[target] = nowSeconds;
            continue;
        }

        float decayMultiplier = GetActiveSoundUncertaintyMultiplier(target, nowSeconds);
        double decayWindow = GetDecayWindow(status, decayMultiplier);
        if ((nowSeconds - lastTimestamp) < decayWindow) continue;

        EnemyLocationStates[target] = DowngradeStatus(status);
        locationStatusTimestamps[target] = nowSeconds;
    }
}

public void UpdateEnemyLocation(CreatureStats target, LocationStatus newStatus)
{
    if (target != null && EnemyLocationStates.ContainsKey(target))
    {
        // We never downgrade a location status (e.g., from Pinpointed back to Unknown) unless an enemy moves.
        if (newStatus > EnemyLocationStates[target])
        {
            EnemyLocationStates[target] = newStatus;
            locationStatusTimestamps[target] = Time.GetTicksMsec() / 1000.0;
            GD.Print($"{myStats.Name} now perceives {target.Name}'s location as: {newStatus}");
        }
        else if (newStatus == EnemyLocationStates[target])
        {
            locationStatusTimestamps[target] = Time.GetTicksMsec() / 1000.0;
        }
    }
}

/// <summary>
/// Gets the current known location status for a specific target.
/// </summary>

public void RegisterSoundUncertainty(CreatureStats target, float decayMultiplier, float lingerSeconds)
{
    if (target == null || !EnemyLocationStates.ContainsKey(target)) return;

    double nowSeconds = Time.GetTicksMsec() / 1000.0;
    float clampedMultiplier = Mathf.Clamp(decayMultiplier, 1.0f, 3.0f);
    float duration = Mathf.Clamp(lingerSeconds * 1.25f, 1.0f, 8.0f);

    soundUncertaintyDecayMultipliers[target] = clampedMultiplier;
    soundUncertaintyBoostExpiry[target] = nowSeconds + duration;
}

private float GetActiveSoundUncertaintyMultiplier(CreatureStats target, double nowSeconds)
{
    if (!soundUncertaintyBoostExpiry.TryGetValue(target, out double expiry)) return 1.0f;
    if (nowSeconds > expiry)
    {
        soundUncertaintyBoostExpiry.Remove(target);
        soundUncertaintyDecayMultipliers.Remove(target);
        return 1.0f;
    }

    if (!soundUncertaintyDecayMultipliers.TryGetValue(target, out float multiplier)) return 1.0f;
    return Mathf.Max(1.0f, multiplier);
}

private static double GetDecayWindow(LocationStatus status, float multiplier)
{
    double baseSeconds = status switch
    {
        LocationStatus.KnownSquare => KnownSquareBaseDecaySeconds,
        LocationStatus.KnownDirection => KnownDirectionBaseDecaySeconds,
        LocationStatus.DetectedPresence => DetectedPresenceBaseDecaySeconds,
        _ => 9999.0
    };

    return baseSeconds / Mathf.Max(1.0f, multiplier);
}

private static LocationStatus DowngradeStatus(LocationStatus status)
{
    return status switch
    {
        LocationStatus.KnownSquare => LocationStatus.KnownDirection,
        LocationStatus.KnownDirection => LocationStatus.DetectedPresence,
        LocationStatus.DetectedPresence => LocationStatus.Unknown,
        _ => status
    };
}

public LocationStatus GetLocationStatus(CreatureStats target)
{
    if (target != null && EnemyLocationStates.TryGetValue(target, out LocationStatus status))
    {
        return status;
    }
    return LocationStatus.Unknown;
}

/// <summary>
/// Called when an enemy moves. This resets their location status to Unknown for blinded creatures.
/// </summary>
public void OnCreatureMoved(CreatureStats movedCreature)
{
    if (myStats.MyEffects.HasCondition(Condition.Blinded) && EnemyLocationStates.ContainsKey(movedCreature))
    {
        // If I am blind and an enemy moves, I lose track of them.
        if(EnemyLocationStates[movedCreature] != LocationStatus.Unknown)
        {
            GD.Print($"{movedCreature.Name} moved. {myStats.Name} (blinded) no longer knows their location.");
            EnemyLocationStates[movedCreature] = LocationStatus.Unknown;
            locationStatusTimestamps[movedCreature] = Time.GetTicksMsec() / 1000.0;
            soundUncertaintyBoostExpiry.Remove(movedCreature);
            soundUncertaintyDecayMultipliers.Remove(movedCreature);
        }
    }
}
}