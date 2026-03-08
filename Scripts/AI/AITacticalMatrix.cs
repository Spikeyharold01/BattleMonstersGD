using Godot;
using System.IO;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: AITacticalMatrix.cs (GODOT VERSION)
// PURPOSE: The global AI brain that learns and saves its knowledge.
// ATTACH TO: Do not attach (Static Class).
// =================================================================================================

/// <summary>
/// A serializable data container that holds all the "weights" or "preferences" for the AI.
/// Godot C# supports serialization via System.Text.Json or Newtonsoft.Json for complex types like Dictionary.
/// Since we need simple file I/O, we can use Godot's FileAccess or System.IO.
/// For Dictionary serialization, standard JsonUtility doesn't exist in Godot.
/// We will use a wrapper or manual serialization if sticking to Godot's JSON,
/// or use System.Text.Json (standard in .NET 6+ used by Godot 4).
/// </summary>
public class TacticalData
{
// --- Targeting Weights ---
public float W_TargetLowHealth = 10f;
public Dictionary<string, float> W_WeaponEffectiveness = new Dictionary<string, float>();
public float W_TargetHighestThreat = 15f;
public float W_TargetLowAC = 5f;
public float W_TargetLowWillSave = 8f;

// --- Positional Weights ---
public float W_AchieveFlank = 20f;          
public float W_MaintainMeleeRange = 5f;     
public float W_MaintainRangedDistance = 10f;
public float W_AvoidThreateningArea = -10f; 
public float W_SuccessfullyHid = 5f;        

// --- Strategic Weights ---
public float W_UseDebuffOnThreat = 25f;     
public float W_UseBuffWhenAvailable = 15f;  
public float W_Experimentation = 10f;       
public float W_HealWoundedAlly = 20f;       
public float W_GoDefensiveWhenLowHP = 30f;  
public float W_AttackHighACMiss = -5f;

// --- Auditory Heuristic Learning ---
// Key format: "{Tier}_{Loudness}" where Tier in [Animal,LowInt,Tactical,Strategic]
// and Loudness in [Quiet,Medium,Loud]. Positive values indicate more caution.
public Dictionary<string, float> W_SoundCautionByTier = new Dictionary<string, float>();
}

public static class AITacticalMatrix
{
private static TacticalData learnedData;
private static string savePath;


static AITacticalMatrix()
{
    // Godot User Data Path
    savePath = "user://ai_tactical_matrix.json";
    Load();
}

public static TacticalData GetPerfectTactics()
{
    // Deep copy via JSON serialization using Godot's JSON helper or System.Text.Json
    string json = System.Text.Json.JsonSerializer.Serialize(learnedData);
    return System.Text.Json.JsonSerializer.Deserialize<TacticalData>(json);
}

public static void RecordWeaponEffectivenessFeedback(CreatureStats target, Item_SO weapon, float damageDealt, int totalPotentialDamage)
{
    if (target == null || weapon == null) return;
    const float learningRate = 0.5f;

    float feedbackValue = 0;
    
    bool wasResisted = target.Template.Resistances.Any(res => weapon.DamageInfo.Any(d => res.DamageTypes.Contains(d.DamageType))) && damageDealt < totalPotentialDamage;

    if (damageDealt > totalPotentialDamage) feedbackValue = 10f; 
    else if (wasResisted) feedbackValue = -5f; 
    else if (damageDealt == 0) feedbackValue = -10f; 
    else if (damageDealt == totalPotentialDamage)
    {
        if (target.Template.DamageReductions != null && target.Template.DamageReductions.Any())
        {
            feedbackValue = 5f; 
        }
    }
    
    if (Mathf.IsEqualApprox(feedbackValue, 0)) return; 

    List<string> targetTraits = new List<string> { $"Type_{target.Template.Type}" };
    if (target.Template.SubTypes != null)
        targetTraits.AddRange(target.Template.SubTypes.Select(s => $"SubType_{s}"));

    List<string> weaponProperties = new List<string> { $"Material_{weapon.Material}" };
    weaponProperties.AddRange(weapon.DamageInfo.Select(d => $"DamageType_{d.DamageType}"));
    
    foreach (var trait in targetTraits)
    {
        foreach (var prop in weaponProperties)
        {
            string key = $"{trait}_{prop}";
            if (!learnedData.W_WeaponEffectiveness.ContainsKey(key))
            {
                learnedData.W_WeaponEffectiveness[key] = 0;
            }
            learnedData.W_WeaponEffectiveness[key] += feedbackValue * learningRate;
            learnedData.W_WeaponEffectiveness[key] = Mathf.Clamp(learnedData.W_WeaponEffectiveness[key], -50f, 50f);
            GD.Print($"[AI LEARNING] Updated {key} by {feedbackValue * learningRate}. New score: {learnedData.W_WeaponEffectiveness[key]}");
        }
    }
    Save();
}

public static void RecordFeedback(FeedbackType type, float value)
{
    const float learningRate = 0.25f;
    const float explorationBonus = 0.1f;
    
    float learningValue = value * learningRate;

    switch (type)
    {
        case FeedbackType.KilledTarget:
            learnedData.W_TargetLowHealth += learningValue; 
            GD.Print($"[AI LEARNING] Rewarded W_TargetLowHealth by {learningValue}. New value: {learnedData.W_TargetLowHealth}");
            break;
        case FeedbackType.DamagedHighestThreat:
            learnedData.W_TargetHighestThreat += learningValue + explorationBonus; 
            GD.Print($"[AI LEARNING] Rewarded W_TargetHighestThreat by {learningValue}. New value: {learnedData.W_TargetHighestThreat}");
            break;
		case FeedbackType.WeaponBypassedDR:
        case FeedbackType.WeaponHitVulnerability:
        case FeedbackType.WeaponWasResisted:
        case FeedbackType.WeaponWasImmune:
            break;
        case FeedbackType.MissedHighAC:
            learnedData.W_AttackHighACMiss += learningValue; 
            GD.Print($"[AI LEARNING] Penalized W_AttackHighACMiss by {learningValue}. New value: {learnedData.W_AttackHighACMiss}");
            break;
        case FeedbackType.AchievedFlankAndHit:
            learnedData.W_AchieveFlank += learningValue;
            GD.Print($"[AI LEARNING] Rewarded W_AchieveFlank by {learningValue}. New value: {learnedData.W_AchieveFlank}");
            break;
        case FeedbackType.AvoidedDamageByStayingAtRange:
            learnedData.W_MaintainRangedDistance += learningValue + explorationBonus;
            GD.Print($"[AI LEARNING] Rewarded W_MaintainRangedDistance by {learningValue}. New value: {learnedData.W_MaintainRangedDistance}");
            break;
        case FeedbackType.DebuffedThreatAndTheyMissed:
            learnedData.W_UseDebuffOnThreat += learningValue;
            GD.Print($"[AI LEARNING] Heavily Rewarded W_UseDebuffOnThreat by {learningValue}. New value: {learnedData.W_UseDebuffOnThreat}");
            break;
        case FeedbackType.HealedAllyWhoThenGotAKill:
            learnedData.W_HealWoundedAlly += learningValue;
            GD.Print($"[AI LEARNING] Rewarded W_HealWoundedAlly by {learningValue}. New value: {learnedData.W_HealWoundedAlly}");
            break;
        case FeedbackType.UsedBuffAndAllyHit:
            learnedData.W_UseBuffWhenAvailable += learningValue + explorationBonus;
            GD.Print($"[AI LEARNING] Rewarded W_UseBuffWhenAvailable by {learningValue}. New value: {learnedData.W_UseBuffWhenAvailable}");
            break;
        case FeedbackType.SurvivedTurnUsingDefense:
            learnedData.W_GoDefensiveWhenLowHP += learningValue;
            GD.Print($"[AI LEARNING] Rewarded W_GoDefensiveWhenLowHP by {learningValue}. New value: {learnedData.W_GoDefensiveWhenLowHP}");
            break;
        case FeedbackType.HidingSpotSucceeded:
            learnedData.W_SuccessfullyHid += learningValue; 
            GD.Print($"[AI LEARNING] Rewarded W_SuccessfullyHid by {learningValue}. New value: {learnedData.W_SuccessfullyHid}");
            break;
        case FeedbackType.HidingSpotFailed:
            learnedData.W_SuccessfullyHid += learningValue; 
            GD.Print($"[AI LEARNING] Penalized W_SuccessfullyHid by {learningValue}. New value: {learnedData.W_SuccessfullyHid}");
            break;
    }

    learnedData.W_TargetLowHealth = Mathf.Clamp(learnedData.W_TargetLowHealth, 1f, 50f);
    learnedData.W_TargetHighestThreat = Mathf.Clamp(learnedData.W_TargetHighestThreat, 1f, 50f);
    learnedData.W_TargetLowAC = Mathf.Clamp(learnedData.W_TargetLowAC, 1f, 30f);
    learnedData.W_TargetLowWillSave = Mathf.Clamp(learnedData.W_TargetLowWillSave, 1f, 40f);
    learnedData.W_AchieveFlank = Mathf.Clamp(learnedData.W_AchieveFlank, 5f, 60f);
    learnedData.W_MaintainMeleeRange = Mathf.Clamp(learnedData.W_MaintainMeleeRange, -5f, 20f);
    learnedData.W_MaintainRangedDistance = Mathf.Clamp(learnedData.W_MaintainRangedDistance, 1f, 40f);
    learnedData.W_AvoidThreateningArea = Mathf.Clamp(learnedData.W_AvoidThreateningArea, -50f, -1f);
    learnedData.W_UseDebuffOnThreat = Mathf.Clamp(learnedData.W_UseDebuffOnThreat, 5f, 70f);
    learnedData.W_UseBuffWhenAvailable = Mathf.Clamp(learnedData.W_UseBuffWhenAvailable, 0f, 40f);
    learnedData.W_HealWoundedAlly = Mathf.Clamp(learnedData.W_HealWoundedAlly, 5f, 60f);
    learnedData.W_GoDefensiveWhenLowHP = Mathf.Clamp(learnedData.W_GoDefensiveWhenLowHP, 10f, 80f);
    learnedData.W_AttackHighACMiss = Mathf.Clamp(learnedData.W_AttackHighACMiss, -40f, -1f);
    learnedData.W_SuccessfullyHid = Mathf.Clamp(learnedData.W_SuccessfullyHid, -20f, 40f);
    learnedData.W_Experimentation = Mathf.Clamp(learnedData.W_Experimentation, 0f, 30f);
    
    Save();
}

public static float GetSoundCautionBias(int intelligence, float threatEstimate)
{
    EnsureInitialized();
    string key = BuildSoundLearningKey(intelligence, threatEstimate);
    if (!learnedData.W_SoundCautionByTier.ContainsKey(key))
    {
        learnedData.W_SoundCautionByTier[key] = 0f;
    }
    return learnedData.W_SoundCautionByTier[key];
}

public static void RecordSoundOutcome(int intelligence, float threatEstimate, float cautionDelta)
{
    EnsureInitialized();

    string key = BuildSoundLearningKey(intelligence, threatEstimate);
    if (!learnedData.W_SoundCautionByTier.ContainsKey(key))
    {
        learnedData.W_SoundCautionByTier[key] = 0f;
    }

    // Smarter creatures adapt faster from less data.
    float intelligenceLearningMultiplier = Mathf.Clamp(0.5f + (intelligence / 10f), 0.5f, 2.2f);
    float adjustedDelta = cautionDelta * intelligenceLearningMultiplier;

    learnedData.W_SoundCautionByTier[key] = Mathf.Clamp(learnedData.W_SoundCautionByTier[key] + adjustedDelta, -6f, 10f);
    GD.Print($"[AI LEARNING] Sound caution '{key}' adjusted by {adjustedDelta:F2}. New value: {learnedData.W_SoundCautionByTier[key]:F2}");
    Save();
}

private static string BuildSoundLearningKey(int intelligence, float threatEstimate)
{
    string tier = intelligence <= 2 ? "Animal" :
                  intelligence <= 6 ? "LowInt" :
                  intelligence <= 12 ? "Tactical" : "Strategic";

    string loudness = threatEstimate >= 6f ? "Loud" :
                      threatEstimate >= 2.2f ? "Medium" : "Quiet";

    return $"{tier}_{loudness}";
}

private static void EnsureInitialized()
{
    if (learnedData == null)
    {
        learnedData = new TacticalData();
    }

    if (learnedData.W_WeaponEffectiveness == null)
    {
        learnedData.W_WeaponEffectiveness = new Dictionary<string, float>();
    }

    if (learnedData.W_SoundCautionByTier == null)
    {
        learnedData.W_SoundCautionByTier = new Dictionary<string, float>();
    }
}

private static void Save()
{
    string json = System.Text.Json.JsonSerializer.Serialize(learnedData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    
    // Godot File Access
    using var file = FileAccess.Open(savePath, FileAccess.ModeFlags.Write);
    file.StoreString(json);
}

private static void Load()
{
    if (FileAccess.FileExists(savePath))
    {
        using var file = FileAccess.Open(savePath, FileAccess.ModeFlags.Read);
        string json = file.GetAsText();
        learnedData = System.Text.Json.JsonSerializer.Deserialize<TacticalData>(json);
        EnsureInitialized();
        GD.Print("[AITacticalMatrix] Loaded learned data.");
    }
    else
    {
        learnedData = new TacticalData();
        EnsureInitialized();
        GD.Print("[AITacticalMatrix] No saved data found. Creating new tactical matrix.");
    }
}
}