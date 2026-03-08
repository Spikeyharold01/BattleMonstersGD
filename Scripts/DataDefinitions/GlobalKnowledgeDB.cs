// =================================================================================================
// FILE: GlobalKnowledgeDB.cs
// PURPOSE: Static database linking traits to damage outcomes.
// =================================================================================================
using System.Collections.Generic;
using System.Linq; 
using Godot;

public static class GlobalKnowledgeDB
{
    private static Dictionary<string, float> traitKnowledge = new Dictionary<string, float>();
    private static Dictionary<string, int> traitSpellResistance = new Dictionary<string, int>();

    public static void RecordSimulatedOutcome(CreatureTemplate_SO targetTemplate, string damageType, float finalMultiplier)
    {
        if (Mathf.IsEqualApprox(finalMultiplier, 1.0f)) return;

        // Assuming CreatureTemplate_SO uses PascalCase for properties
        List<string> traits = new List<string> { targetTemplate.Type.ToString() };
        if (targetTemplate.SubTypes != null) 
        {
            // Convert Godot Array to C# List for LINQ operations
            foreach(var sub in targetTemplate.SubTypes) traits.Add(sub);
        }

        foreach (string trait in traits.Distinct())
        {
            string key = $"{trait}_{damageType}";
            traitKnowledge[key] = finalMultiplier;
        }

        if (targetTemplate.SpellResistance > 0)
        {
            foreach (string trait in traits.Distinct())
            {
                traitSpellResistance[trait] = targetTemplate.SpellResistance;
            }
        }
    }

    public static float PredictMultiplierFromTraits(CreatureTemplate_SO targetTemplate, string damageType)
    {
        List<string> traits = new List<string> { targetTemplate.Type.ToString() };
        if (targetTemplate.SubTypes != null)
        {
            foreach(var sub in targetTemplate.SubTypes) traits.Add(sub);
        }
        
        float bestPrediction = -1f;

        foreach (string trait in traits.Distinct())
        {
            string key = $"{trait}_{damageType}";

            if (traitKnowledge.TryGetValue(key, out float prediction))
            {
                if (bestPrediction == -1f || 
                    Mathf.Abs(prediction - 1.0f) > Mathf.Abs(bestPrediction - 1.0f))
                {
                    bestPrediction = prediction;
                }
            }
        }
        
        return (bestPrediction == -1f) ? 1.0f : bestPrediction;
    }

    public static int PredictSRFromTraits(CreatureTemplate_SO targetTemplate)
    {
        List<string> traits = new List<string> { targetTemplate.Type.ToString() };
        if (targetTemplate.SubTypes != null)
        {
            foreach(var sub in targetTemplate.SubTypes) traits.Add(sub);
        }
        
        int highestPredictedSR = 0;

        foreach (string trait in traits.Distinct())
        {
            if (traitSpellResistance.TryGetValue(trait, out int predictedSR))
            {
                if (predictedSR > highestPredictedSR)
                {
                    highestPredictedSR = predictedSR;
                }
            }
        }
        return highestPredictedSR;
    }
}