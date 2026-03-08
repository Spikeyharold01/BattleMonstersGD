#if TOOLS
using Godot;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public static class WeaponRangeUpdater
{
    public static void UpdateWeaponRanges(string csvPath)
    {
        string weaponAssetPath = "res://Data/Items/Weapons";
        
        // 1. Load All Existing Weapons
        Dictionary<string, Item_SO> weaponLookup = new Dictionary<string, Item_SO>();
        using var dir = DirAccess.Open(weaponAssetPath);
        if (dir != null)
        {
            dir.ListDirBegin();
            string fileName = dir.GetNext();
            while (fileName != "")
            {
                if (!dir.CurrentIsDir() && (fileName.EndsWith(".tres") || fileName.EndsWith(".res")))
                {
                    string fullPath = $"{weaponAssetPath}/{fileName}";
                    var item = GD.Load<Item_SO>(fullPath);
                    if (item != null && item.ItemType == ItemType.Weapon)
                    {
                        string cleanedName = CleanNameForMatching(item.ItemName);
                        if (!weaponLookup.ContainsKey(cleanedName))
                            weaponLookup.Add(cleanedName, item);
                    }
                }
                fileName = dir.GetNext();
            }
        }
        else
        {
            GD.PrintErr($"Could not open directory: {weaponAssetPath}");
            return;
        }

        // 2. Process CSV
        string[] lines = File.ReadAllLines(csvPath);
        int updatedCount = 0;
        List<string> notFound = new List<string>();

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] columns = line.Split(',');
            if (columns.Length < 2) continue;

            string csvWeaponName = CleanNameForMatching(columns[0]);
            string rangeString = columns[1].Trim();

            if (weaponLookup.TryGetValue(csvWeaponName, out Item_SO weaponToUpdate))
            {
                string firstRangeValue = rangeString.Split('/')[0];
                var match = Regex.Match(firstRangeValue, @"\d+");
                if (match.Success && float.TryParse(match.Value, out float range))
                {
                    weaponToUpdate.RangeIncrement = range;
                    // In Godot, ResourceSaver.Save is necessary to persist changes
                    ResourceSaver.Save(weaponToUpdate, weaponToUpdate.ResourcePath);
                    updatedCount++;
                }
            }
            else
            {
                notFound.Add(columns[0].Trim());
            }
        }

        GD.Print($"Update complete! Updated {updatedCount} weapons.");
        if (notFound.Count > 0)
        {
            GD.Print("Weapons not found: " + string.Join(", ", notFound.Take(5)) + (notFound.Count > 5 ? "..." : ""));
        }
    }

    private static string CleanNameForMatching(string name)
    {
        string cleaned = name.ToLower().Replace("\"", "");
        cleaned = Regex.Replace(cleaned, @"\s?\(.*\)", "");
        cleaned = cleaned.Replace(", (standard)", "").Replace(", (throwing)", "")
                         .Replace(", (heavy)", " heavy").Replace(", (light)", " light")
                         .Replace(",", "");
        return cleaned.Trim();
    }
}
#endif