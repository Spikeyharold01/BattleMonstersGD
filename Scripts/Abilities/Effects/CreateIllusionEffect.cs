using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: CreateIllusionEffect.cs (GODOT VERSION)
// PURPOSE: An effect component for creating an illusion (e.g. Wall of Stone) on the battlefield.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class CreateIllusionEffect : AbilityEffectComponent
{
[Export]
[Tooltip("The prefab to spawn, which must have an IllusionController component.")]
public PackedScene IllusionPrefab;

[Export]
[Tooltip("If true, this effect clones the caster's CharacterPrefab instead of using IllusionPrefab.")]
public bool UseCasterCharacterPrefab = false;

[Export]
[Tooltip("When cloning the caster prefab, remove runtime combat/controller nodes so the projection stays data-driven and non-combatant.")]
public bool StripRuntimeControllersOnClone = true;

[Export]
[Tooltip("The dimensions of the illusion (Width, Height, Depth) in feet.")]
public Vector3 IllusionDimensions = new Vector3(10f, 10f, 1f);

[ExportGroup("Ghost Sound")]
[Export]
[Tooltip("When this CreateIllusionEffect is used by Ghost Sound, use preview-scale X as requested human-volume.")]
public bool UsePreviewScaleAsGhostSoundVolume = true;

[Export]
[Tooltip("Default human-volume used by AI/non-preview casting. If <= 0, uses max allowed by caster level.")]
public float DefaultGhostSoundHumanVolume = -1f;

[ExportGroup("Sensory Projection")]
[Export]
[Tooltip("Emit periodic illusionary sound from the spawned illusion (for image spells with sound).")]
public bool EmitIllusorySound = false;

[Export]
[Tooltip("Illusionary sound intensity for periodic emissions.")]
public float IllusorySoundIntensity = 1.4f;

[Export]
[Tooltip("Seconds between periodic illusionary sound emissions.")]
public float IllusorySoundPulseIntervalSeconds = 1.5f;

[Export]
[Tooltip("Duration of each emitted illusionary sound event.")]
public float IllusorySoundEventDurationSeconds = 2.0f;

[Export]
[Tooltip("Emit periodic illusionary scent from the spawned illusion (for smell-capable perception).")]
public bool EmitIllusoryScent = false;

[Export]
public ScentType IllusoryScentType = ScentType.Creature;

[Export]
public float IllusoryScentIntensity = 0.8f;

[Export]
public float IllusoryScentDecayRate = 0.22f;

[Export]
public float IllusoryScentVerticalBias = 0f;

[Export]
public bool IllusoryScentIsTrail = false;

[Export]
public float IllusoryScentDurationSeconds = 4f;

[Export]
[Tooltip("Emit periodic thermal-signature proxy scent pulses for thermal/fire-scent perception models.")]
public bool EmitIllusoryThermalSignature = false;

[Export]
public ScentType IllusoryThermalScentType = ScentType.Fire;

[Export]
public float IllusoryThermalIntensity = 1.0f;

[Export]
public float IllusoryThermalDecayRate = 0.18f;

[Export]
public float IllusoryThermalDurationSeconds = 4f;

[Export]
[Tooltip("Seconds between periodic scent / thermal pulses.")]
public float IllusoryScentPulseIntervalSeconds = 1.5f;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
 bool isGhostSound = ability.AbilityName.ToLower().Contains("ghost sound");

    PackedScene prefabToSpawn = ResolveIllusionPrefab(context);

    if (!isGhostSound && prefabToSpawn == null)
    {
        GD.PrintErr($"CreateIllusionEffect on {ability.AbilityName} is missing its prefab!");
        return;
    }

    // The player/AI will provide the AimPoint and a rotation.
    Quaternion rotation = Quaternion.Identity; 
    
    // TargetObject (GameObject) is Node3D in Godot context
    Node3D targetObj = context.TargetObject as Node3D;
    
    if (targetObj != null)
    {
         // Get rotation from player input preview object
         // Godot Quaternion from Basis
         rotation = targetObj.GlobalTransform.Basis.GetRotationQuaternion();
    }
	
if (prefabToSpawn != null)
    {
 Node3D illusionGO = prefabToSpawn.Instantiate<Node3D>();

        // Add to Scene Root
        SceneTree tree = (SceneTree)Engine.GetMainLoop();
        tree.CurrentScene.AddChild(illusionGO);

        illusionGO.GlobalPosition = context.AimPoint;
        illusionGO.GlobalRotation = rotation.GetEuler(); // Set rotation

        if (UseCasterCharacterPrefab && StripRuntimeControllersOnClone)
        {
            StripRuntimeNodesFromClone(illusionGO);
        }

        var controller = EnsureIllusionController(illusionGO);
        if (controller == null)
        {
            GD.PrintErr($"Illusion prefab '{prefabToSpawn.ResourcePath}' is missing the IllusionController component!");
            illusionGO.QueueFree();
            return;
        }

        controller.Initialize(context.Caster, ability, IllusionDimensions);
		
        if (EmitIllusorySound || EmitIllusoryScent || EmitIllusoryThermalSignature)
        {
            var emitter = new IllusionPerceptionEmitter();
            emitter.Name = "IllusionPerceptionEmitter";
            illusionGO.AddChild(emitter);
            emitter.Configure(
                context.Caster,
                EmitIllusorySound,
                IllusorySoundIntensity,
                IllusorySoundPulseIntervalSeconds,
                IllusorySoundEventDurationSeconds,
                EmitIllusoryScent,
                IllusoryScentType,
                IllusoryScentIntensity,
                IllusoryScentDecayRate,
                IllusoryScentVerticalBias,
                IllusoryScentIsTrail,
                IllusoryScentDurationSeconds,
                EmitIllusoryThermalSignature,
                IllusoryThermalScentType,
                IllusoryThermalIntensity,
                IllusoryThermalDecayRate,
                IllusoryThermalDurationSeconds,
                IllusoryScentPulseIntervalSeconds);
        }
    }

     if (isGhostSound)
    {
        int casterLevel = context.Caster?.Template?.CasterLevel ?? 0;
        int maxHumanVolume = SoundSystem.GetGhostSoundMaxHumanVolume(casterLevel);
        float requestedHumanVolume = ResolveGhostSoundHumanVolume(context, maxHumanVolume);
        float durationSeconds = Mathf.Max(6f, casterLevel * 6f); // 1 round/level

        SoundSystem.EmitGhostSoundByHumanVolume(context.Caster, context.AimPoint, requestedHumanVolume, durationSeconds);
        GD.Print($"{context.Caster.Name} creates Ghost Sound at {context.AimPoint} with volume {requestedHumanVolume:0.#}/{maxHumanVolume} humans for {durationSeconds:0.#}s.");
    }

    GD.Print($"{context.Caster.Name} creates an {ability.AbilityName} at {context.AimPoint}.");
}

private PackedScene ResolveIllusionPrefab(EffectContext context)
{
    if (!UseCasterCharacterPrefab) return IllusionPrefab;
    return context?.Caster?.Template?.CharacterPrefab;
}

private void StripRuntimeNodesFromClone(Node3D illusionGO)
{
    var oldStats = illusionGO.GetNodeOrNull<CreatureStats>("CreatureStats") ?? illusionGO as CreatureStats;
    if (oldStats != null) oldStats.QueueFree();

    var oldAI = illusionGO.GetNodeOrNull<AIController>("AIController");
    if (oldAI != null) oldAI.QueueFree();

    var mover = illusionGO.GetNodeOrNull<CreatureMover>("CreatureMover");
    if (mover != null) mover.QueueFree();

    var actionController = illusionGO.GetNodeOrNull<PlayerActionController>("PlayerActionController");
    if (actionController != null) actionController.QueueFree();
}

private IllusionController EnsureIllusionController(Node3D illusionGO)
{
    var existingController = illusionGO as IllusionController ?? illusionGO.GetNodeOrNull<IllusionController>("IllusionController");
    if (existingController != null) return existingController;

    var generatedController = new IllusionController
    {
        Name = "IllusionController",
        Monitoring = true,
        Monitorable = true
    };
    illusionGO.AddChild(generatedController);
    return generatedController;
}

private float ResolveGhostSoundHumanVolume(EffectContext context, int maxHumanVolume)
{
    float fallback = DefaultGhostSoundHumanVolume > 0f ? DefaultGhostSoundHumanVolume : maxHumanVolume;
    if (!UsePreviewScaleAsGhostSoundVolume || context.TargetObject == null)
    {
        return Mathf.Clamp(fallback, 0.1f, Mathf.Max(0.1f, maxHumanVolume));
    }

    float requestedFromPreview = context.TargetObject.Scale.X;
    if (requestedFromPreview <= 0f)
    {
        requestedFromPreview = fallback;
    }

    return Mathf.Clamp(requestedFromPreview, 0.1f, Mathf.Max(0.1f, maxHumanVolume));
}

public override float GetAIEstimatedValue(EffectContext context)
{
    if (context?.Caster == null) return 0f;

    Ability_SO sourceAbility = context.Ability;
    if (IsSoundDecoyAbility(sourceAbility))
    {
        return ScoreSoundDecoyPlacement(context, sourceAbility);
    }

    if (IsGenericDecoyAbility(sourceAbility))
    {
        return ScoreGenericDecoyPlacement(context);
    }

    return ScoreBarrierPlacement(context);
}

private bool IsSoundDecoyAbility(Ability_SO ability)
{
    string name = ability?.AbilityName?.ToLower() ?? string.Empty;
    string description = ability?.DescriptionForTooltip?.ToLower() ?? string.Empty;
    return name.Contains("ghost sound") ||
           description.Contains("illusory sound") ||
           (description.Contains("illusion") && description.Contains("sound"));
}

private bool IsGenericDecoyAbility(Ability_SO ability)
{
    string name = ability?.AbilityName?.ToLower() ?? string.Empty;
    string description = ability?.DescriptionForTooltip?.ToLower() ?? string.Empty;
    if (IsSoundDecoyAbility(ability)) return true;

    return name.Contains("decoy") ||
           name.Contains("distraction") ||
           name.Contains("lure") ||
           description.Contains("decoy") ||
           description.Contains("distraction") ||
           description.Contains("draw attention") ||
           description.Contains("mislead");
}

private float ScoreSoundDecoyPlacement(EffectContext context, Ability_SO ability)
{
    var caster = context.Caster;
    var allEnemies = TurnManager.Instance.GetAllCombatants()
        .Where(c => c != null && c != caster && c.CurrentHP > 0 && c.IsInGroup("Player") != caster.IsInGroup("Player"))
        .ToList();
    if (!allEnemies.Any()) return 0f;

    int casterLevel = caster.Template?.CasterLevel ?? 0;
    int maxHumans = SoundSystem.GetGhostSoundMaxHumanVolume(casterLevel);
    float desiredHumans = DefaultGhostSoundHumanVolume > 0f ? DefaultGhostSoundHumanVolume : maxHumans;
    float clampedHumans = Mathf.Clamp(desiredHumans, 0.1f, Mathf.Max(0.1f, maxHumans));
    float intensity = SoundSystem.ConvertGhostSoundHumanVolumeToIntensity(clampedHumans);
    float duration = Mathf.Max(6f, casterLevel * 6f);

    var probe = new SoundEvent(caster, context.AimPoint, intensity, duration, SoundEventType.Illusion, isIllusion: true);

    var allies = AISpatialAnalysis.FindAllies(caster);
    float avgAllyDistanceToDecoy = allies.Any() ? allies.Average(a => a.GlobalPosition.DistanceTo(context.AimPoint)) : 0f;

    float score = 0f;
    int audibleEnemies = 0;

    foreach (var enemy in allEnemies)
    {
        if (!SoundSystem.CanHear(enemy, probe)) continue;
        audibleEnemies++;

        float enemyToDecoy = enemy.GlobalPosition.DistanceTo(context.AimPoint);
        float enemyToCaster = enemy.GlobalPosition.DistanceTo(caster.GlobalPosition);
        bool canSeeCaster = LineOfSightManager.GetVisibility(enemy, caster).HasLineOfSight;

        float enemyScore = 30f;
        if (!canSeeCaster) enemyScore += 18f;

        var enemyState = enemy.GetNodeOrNull<CombatStateController>("CombatStateController");
        if (enemyState != null)
        {
            LocationStatus status = enemyState.GetLocationStatus(caster);
            if (status <= LocationStatus.KnownDirection) enemyScore += 12f;
        }

        if (enemyToDecoy < enemyToCaster) enemyScore += 8f;
        if (avgAllyDistanceToDecoy >= 15f) enemyScore += 6f;

        score += enemyScore;
    }

    if (audibleEnemies == 0)
    {
        score -= 25f;
    }
    else
    {
        score += audibleEnemies * 4f;
    }

    return score;
}

private float ScoreGenericDecoyPlacement(EffectContext context)
{
    var caster = context.Caster;
    var enemies = AISpatialAnalysis.FindVisibleTargets(caster);
    if (!enemies.Any()) return 10f;

    var allies = AISpatialAnalysis.FindAllies(caster);
    float avgAllyDistanceToDecoy = allies.Any() ? allies.Average(a => a.GlobalPosition.DistanceTo(context.AimPoint)) : 0f;

    float score = 0f;
    foreach (var enemy in enemies)
    {
        float enemyToDecoy = enemy.GlobalPosition.DistanceTo(context.AimPoint);
        float enemyToCaster = enemy.GlobalPosition.DistanceTo(caster.GlobalPosition);

        float enemyScore = 15f;
        if (enemyToDecoy < enemyToCaster) enemyScore += 10f;
        if (avgAllyDistanceToDecoy >= 15f) enemyScore += 6f;

        score += enemyScore;
    }

    return score;
}

private float ScoreBarrierPlacement(EffectContext context)
{
    var aiController = context.Caster.GetParent().GetNodeOrNull<AIController>("AIController");
    if (aiController == null) return 0f;

    float bestScore = 0;
    var visibleEnemies = AISpatialAnalysis.FindVisibleTargets(context.Caster);
    var vulnerableAllies = AISpatialAnalysis.FindAllies(context.Caster)
        .Where(a => a.CurrentHP < a.Template.MaxHP * 0.5f).ToList();

 vulnerableAllies.Add(context.Caster);

    var rangedEnemies = visibleEnemies.Where(e => e.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand)?.WeaponType != WeaponType.Melee).ToList();

    foreach (var ally in vulnerableAllies)
    {
        foreach (var enemy in rangedEnemies)
        {
            Vector3 midpoint = ally.GlobalPosition.Lerp(enemy.GlobalPosition, 0.5f);

            float score = 100f;
            if (ally == context.Caster) score *= 1.5f;
            if (enemy == aiController.GetPerceivedHighestThreat()) score *= 2.0f;

            if (score > bestScore)
            {
                bestScore = score;
                context.AimPoint = midpoint;
            }
        }
    }
    
    return bestScore;
}
}