using Godot;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public static class ImportUtils 
{
    // Helper to get value from Dictionary safely
    public static T GetValue<T>(Dictionary<string, string> data, string key, T defaultValue = default(T))
    {
        if (data.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value))
        {
            try
            {
                var converter = System.ComponentModel.TypeDescriptor.GetConverter(typeof(T));
                if (converter != null)
                {
                    return (T)converter.ConvertFromInvariantString(value);
                }
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }
    
    public static T ParseEnum<T>(string value, T defaultValue) where T : struct
    {
        if (string.IsNullOrEmpty(value))
            return defaultValue;
        return Enum.TryParse<T>(value.Replace(' ', '_').Replace("-", "_"), true, out T result) ? result : defaultValue;
    }

    public static List<string> ParseStringList(string rawData)
    {
        if (string.IsNullOrEmpty(rawData))
            return new List<string>();
        return rawData.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim(' ', '"'))
                      .Where(s => !string.IsNullOrEmpty(s))
                      .ToList();
    }

    public static List<Language> ParseLanguages(string rawData)
    {
        var langs = new List<Language>();
        if (string.IsNullOrEmpty(rawData))
            return langs;
        var names = rawData.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var name in names)
        {
            langs.Add(ImportUtils.ParseEnum(name.Trim(), Language.Common));
        }
        return langs.Distinct().ToList();
    }

    public static int GetCrAsInt(string cr)
    {
        if (string.IsNullOrEmpty(cr)) return 0;
        if (cr.Contains("/")) return 0; // Simplified handling of fractionals
        int.TryParse(cr, out int crInt);
        return crInt;
    }

    public static int CalculateModifier(int score)
    {
        // Godot.Mathf.FloorToInt works like Unity's
        return Mathf.FloorToInt((score - 10) / 2f);
    }

    public static int GetSaveBase(int totalSave, int abilityScore)
    {
        return totalSave - CalculateModifier(abilityScore);
    }

    public static int GetAbilityModForSkill(SkillType skill, CreatureTemplate_SO template)
    {
        switch (skill)
        {
            case SkillType.Climb:
            case SkillType.Swim:
                return CalculateModifier(template.Strength);
            case SkillType.Acrobatics:
            case SkillType.DisableDevice:
            case SkillType.EscapeArtist:
            case SkillType.Fly:
            case SkillType.Ride:
            case SkillType.SleightOfHand:
            case SkillType.Stealth:
                return CalculateModifier(template.Dexterity);
            case SkillType.Appraise:
            case SkillType.Craft:
            case SkillType.Linguistics:
            case SkillType.Spellcraft:
            case SkillType.Knowledge:
            case SkillType.KnowledgeArcana:
            case SkillType.KnowledgeDungeoneering:
            case SkillType.KnowledgeEngineering:
            case SkillType.KnowledgeGeography:
            case SkillType.KnowledgeHistory:
            case SkillType.KnowledgeLocal:
            case SkillType.KnowledgeNature:
            case SkillType.KnowledgeNobility:
            case SkillType.KnowledgePlanes:
            case SkillType.KnowledgeReligion:
                return CalculateModifier(template.Intelligence);
            case SkillType.Heal:
            case SkillType.Perception:
            case SkillType.Profession:
            case SkillType.SenseMotive:
            case SkillType.Survival:
            case SkillType.Spot:
            case SkillType.Listen:
                return CalculateModifier(template.Wisdom);
            case SkillType.Bluff:
            case SkillType.Diplomacy:
            case SkillType.Disguise:
            case SkillType.HandleAnimal:
            case SkillType.Intimidate:
            case SkillType.Perform:
            case SkillType.UseMagicDevice:
                return CalculateModifier(template.Charisma);
            default:
                return 0;
        }
    }

    public static SkillType GetKnowledgeSkillForCreature(CreatureType type)
    {
        switch (type)
        {
            case CreatureType.Construct:
            case CreatureType.Dragon:
            case CreatureType.MagicalBeast:
                return SkillType.KnowledgeArcana;
            case CreatureType.Aberration:
            case CreatureType.Ooze:
                return SkillType.KnowledgeDungeoneering;
            case CreatureType.Humanoid:
                return SkillType.KnowledgeLocal;
            case CreatureType.Animal:
            case CreatureType.Fey:
            case CreatureType.MonstrousHumanoid:
            case CreatureType.Plant:
            case CreatureType.Vermin:
                return SkillType.KnowledgeNature;
            case CreatureType.Outsider:
                return SkillType.KnowledgePlanes;
            case CreatureType.Undead:
                return SkillType.KnowledgeReligion;
            default:
                return SkillType.Knowledge;
        }
    }

    public static float GetVerticalReachFromSize(CreatureSize size)
    {
        switch (size)
        {
            case CreatureSize.Fine: return 0.5f;
            case CreatureSize.Diminituve: return 1f; // Typo fix from Enum definition check
            case CreatureSize.Tiny: return 2f;
            case CreatureSize.Small: return 4f;
            case CreatureSize.Medium: return 8f;
            case CreatureSize.Large: return 16f;
            case CreatureSize.Huge: return 32f;
            case CreatureSize.Gargantuan: return 64f;
            case CreatureSize.Colossal: return 128f;
            default: return 8f;
        }
    }

    public static List<EnvironmentProperty> TranslateEnvironmentToProperties(string environmentString)
    {
        if (string.IsNullOrEmpty(environmentString) || environmentString.ToLower().Contains("any"))
            return new List<EnvironmentProperty> { EnvironmentProperty.Any };
        
        string lowerEnv = environmentString.ToLower();
        var properties = new HashSet<EnvironmentProperty>();
        
        if (lowerEnv.Contains("aquatic") || lowerEnv.Contains("ocean") || lowerEnv.Contains("sea"))
            properties.Add(EnvironmentProperty.Aquatic);
        else
            properties.Add(EnvironmentProperty.Terrestrial);
            
        if (lowerEnv.Contains("underground") || lowerEnv.Contains("dungeon"))
            properties.Add(EnvironmentProperty.Subterranean);
        if (lowerEnv.Contains("urban") || lowerEnv.Contains("ruin") || lowerEnv.Contains("castle"))
            properties.Add(EnvironmentProperty.Urban);
        if (lowerEnv.Contains("arctic") || lowerEnv.Contains("tundra") || lowerEnv.Contains("glacier"))
            properties.Add(EnvironmentProperty.Arctic);
        else if (lowerEnv.Contains("cold"))
            properties.Add(EnvironmentProperty.Cold);
            
        if (lowerEnv.Contains("jungle"))
        {
            properties.Add(EnvironmentProperty.Warm);
            properties.Add(EnvironmentProperty.Humid);
        }
        else if (lowerEnv.Contains("warm") || lowerEnv.Contains("desert"))
            properties.Add(EnvironmentProperty.Warm);
            
        if (lowerEnv.Contains("temperate")) properties.Add(EnvironmentProperty.Temperate);
        if (lowerEnv.Contains("desert")) properties.Add(EnvironmentProperty.Arid);
        if (lowerEnv.Contains("swamp") || lowerEnv.Contains("marsh")) properties.Add(EnvironmentProperty.Humid);
        if (lowerEnv.Contains("mountain") || lowerEnv.Contains("hills")) properties.Add(EnvironmentProperty.Mountainous);
        if (lowerEnv.Contains("forest") || lowerEnv.Contains("jungle")) properties.Add(EnvironmentProperty.Forested);
        if (lowerEnv.Contains("plains") || lowerEnv.Contains("desert") || lowerEnv.Contains("savanna")) properties.Add(EnvironmentProperty.OpenTerrain);
        if (lowerEnv.Contains("beach") || lowerEnv.Contains("coast")) properties.Add(EnvironmentProperty.Coastal);
        
        if (lowerEnv.Contains("aballon"))
        {
            properties.Add(EnvironmentProperty.Arid);
            properties.Add(EnvironmentProperty.Planar);
        }

        if (properties.Count == 0 || (properties.Count == 1 && properties.Contains(EnvironmentProperty.Terrestrial)))
            return new List<EnvironmentProperty> { EnvironmentProperty.Any };
            
        return properties.ToList();
    }

    public static int ParseOrganization(string organizationText)
    {
        if (string.IsNullOrEmpty(organizationText)) return 1;

        float maxMean = 1f;
        string[] parts = organizationText.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            string cleanPart = part.ToLower().Trim();
            float currentMean = 0f;
            if (cleanPart.Contains("solitary")) currentMean = 1f;
            else if (cleanPart.Contains("pair")) currentMean = 2f;
            
            Match rangeMatch = Regex.Match(cleanPart, @"\((\d+)-(\d+)\)");
            if (rangeMatch.Success)
            {
                int min = int.Parse(rangeMatch.Groups[1].Value);
                int max = int.Parse(rangeMatch.Groups[2].Value);
                currentMean = (min + max) / 2.0f;
            }
            else
            {
                Match singleMatch = Regex.Match(cleanPart, @"\((\d+)\)");
                if (singleMatch.Success)
                {
                    currentMean = int.Parse(singleMatch.Groups[1].Value);
                }
            }
            if (currentMean > maxMean) maxMean = currentMean;
        }
        return Mathf.RoundToInt(maxMean);
    }

    // Godot typically uses "res://" paths. System.IO works, but ensures directories exist.
    public static void EnsureDirectory(string path)
    {
        // Convert to global path for System.IO if needed, but DirAccess is safer for Godot Virtual FS
        // For Editor tools, using absolute paths via ProjectSettings.GlobalizePath is common.
        if (!DirAccess.DirExistsAbsolute(path))
        {
            DirAccess.MakeDirRecursiveAbsolute(path);
        }
    }

    public static List<DamageInfo> ParseComplexDamageString(string fullDamageString)
    {
        var damageInfos = new List<DamageInfo>();
        if (string.IsNullOrEmpty(fullDamageString)) return damageInfos;

        string[] components = fullDamageString.Split(new[] { " plus ", " and " }, StringSplitOptions.RemoveEmptyEntries);
        if (components.Length > 0)
        {
            damageInfos.Add(ParseSingleDamageComponent(components[0], "Physical"));
        }
        for (int i = 1; i < components.Length; i++)
        {
            string component = components[i];
            string damageType = "Untyped";
            if (component.Contains("cold")) damageType = "Cold";
            else if (component.Contains("electricity")) damageType = "Electricity";
            else if (component.Contains("fire")) damageType = "Fire";
            else if (component.Contains("acid")) damageType = "Acid";
            else if (component.Contains("sonic")) damageType = "Sonic";
            
            var dmgInfo = ParseSingleDamageComponent(component, damageType);
            dmgInfo.MultipliesOnCrit = false;
            damageInfos.Add(dmgInfo);
        }
        return damageInfos;
    }

    public static DamageInfo ParseSingleDamageComponent(string damageSegment, string damageType)
    {
        // IMPORTANT: In Godot, DamageInfo is a Resource, must use 'new'
        var info = new DamageInfo { DamageType = damageType, MultipliesOnCrit = true };
        
        string processedSegment = damageSegment.Trim();
        processedSegment = Regex.Replace(processedSegment, @"\s*\/\s*[×x]\d+.*", "");
        processedSegment = Regex.Replace(processedSegment, @"\s+\w+$", "");
        
        int plusIndex = processedSegment.IndexOf('+');
        int minusIndex = processedSegment.IndexOf('-');
        
        if (plusIndex > 0)
        {
            int.TryParse(processedSegment.Substring(plusIndex + 1), out int bonus);
            info.FlatBonus = bonus;
            processedSegment = processedSegment.Substring(0, plusIndex).Trim();
        }
        else if (minusIndex > 0)
        {
            int.TryParse(processedSegment.Substring(minusIndex), out int bonus);
            info.FlatBonus = bonus; // Will be negative due to parse? or minus symbol?
            // Usually pathfinder is 1d6-1, so Parse "-1" results in -1. Correct.
            processedSegment = processedSegment.Substring(0, minusIndex).Trim();
        }

        if (processedSegment.Contains("d"))
        {
            var parts = processedSegment.Split('d');
            int.TryParse(parts[0], out int count);
            int.TryParse(parts[1], out int sides);
            info.DiceCount = count;
            info.DieSides = sides;
        }
        else
        {
            int.TryParse(processedSegment, out int flatDamage);
            info.FlatBonus += flatDamage;
        }
        return info;
    }

    public static DurationData ParseAuraDuration(string durationStr)
    {
        var data = new DurationData();
        if (string.IsNullOrEmpty(durationStr)) return data;

        string processed = durationStr.ToLower().Trim();

        // Handle units first, convert everything to rounds
        if (processed.Contains("minute"))
        {
            processed = processed.Replace("minutes", "").Replace("minute", "").Trim();
            if (int.TryParse(processed, out int minutes)) data.BaseRounds = minutes * 10;
        }
        else if (processed.Contains("hour"))
        {
            processed = processed.Replace("hours", "").Replace("hour", "").Trim();
            if (int.TryParse(processed, out int hours)) data.BaseRounds = hours * 600;
        }
        else if (processed.Contains("day"))
        {
            processed = processed.Replace("days", "").Replace("day", "").Trim();
            if (int.TryParse(processed, out int days)) data.BaseRounds = days * 14400;
        }
        else // It's in rounds
        {
            processed = processed.Replace("rounds", "").Replace("round", "").Trim();
            if (processed.Contains('d'))
            {
                var parts = processed.Split('d');
                if (parts.Length == 2)
                {
                    int.TryParse(parts[0], out int count);
                    int.TryParse(parts[1], out int sides);
                    data.DiceCount = count;
                    data.DieSides = sides;
                }
            }
            else
            {
                int.TryParse(processed, out int rounds);
                data.BaseRounds = rounds;
            }
        }
        return data;
    }

    public static string ParseHitDiceFromLongString(string rawData)
    {
        if (string.IsNullOrEmpty(rawData)) return "";

        string cleanData = rawData.Split(new[] { " plus ", " see " }, StringSplitOptions.None)[0].Trim();
        var matches = Regex.Matches(cleanData, @"(\d+d\d+)");

        if (matches.Count > 0)
        {
            var diceStrings = new List<string>();
            foreach (Match match in matches)
            {
                diceStrings.Add(match.Value);
            }
            return string.Join("+", diceStrings);
        }
        return "";
    }
}