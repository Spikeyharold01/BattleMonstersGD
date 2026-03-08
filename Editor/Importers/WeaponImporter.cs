#if TOOLS
using Godot;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public static class WeaponImporter
{
    private struct WeaponCategoryInfo
    {
        public WeaponHandedness Handedness;
        public WeaponType WeaponType;
    }

    public static void ImportWeapons(string folderPath)
    {
        string[] csvFiles = Directory.GetFiles(folderPath, "*.csv");
        if (csvFiles.Length == 0)
        {
            GD.PrintErr("No CSV files found in the selected folder.");
            return;
        }

        string itemPath = "res://Data/Items/Weapons";
        ImportUtils.EnsureDirectory(itemPath);

        foreach (var filePath in csvFiles)
        {
            ProcessCsvFile(filePath, itemPath);
        }

        GD.Print($"Import Complete. Processed {csvFiles.Length} weapon CSV files.");
    }

    private static void ProcessCsvFile(string filePath, string itemPath)
    {
        string[] lines = File.ReadAllLines(filePath);
        if (lines.Length <= 1) return;

        WeaponCategoryInfo categoryInfo = GetWeaponCategoryFromFile(Path.GetFileNameWithoutExtension(filePath));

        // Auto-detect the delimiter by checking the first data row.
        char delimiter = '\t'; // Default to tab-separated
        if (lines.Length > 1)
        {
            int tabCount = lines[1].Count(c => c == '\t');
            int commaCount = lines[1].Count(c => c == ',');
            if (commaCount > tabCount) delimiter = ',';
        }

        GD.Print($"Processing {Path.GetFileName(filePath)} with delimiter '{delimiter}'");

        // Skip the header line
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] columns = line.Replace("\"", "").Split(delimiter);
            
            if (columns.Length < 9)
            {
                // GD.PrintErr($"Skipping malformed row in {Path.GetFileName(filePath)}: {line}");
                continue;
            }

            string weaponName = columns[0].Trim();
            string dmgM = columns[3].Trim();
            string crit = columns[4].Trim();
            string range = columns[5].Trim();
            string type = columns[7].Trim();
            string special = columns[8].Trim();

            // Skip entries that are not weapons (like ammunition placeholders without damage)
            if (dmgM == "—" || string.IsNullOrEmpty(dmgM)) continue;

            CreateOrUpdateWeaponAsset(weaponName, dmgM, crit, range, type, special, categoryInfo, itemPath);
        }
    }

    private static void CreateOrUpdateWeaponAsset(string name, string dmg, string crit, string range, string type, string special, WeaponCategoryInfo category, string path)
    {
        string safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        string assetPath = $"{path}/Weapon_{safeName}.tres";

        Item_SO item;
        if (FileAccess.FileExists(assetPath))
            item = GD.Load<Item_SO>(assetPath);
        else
            item = new Item_SO();

        item.ItemName = name;
        item.ItemType = ItemType.Weapon;
        item.IsEquippable = true;
        item.EquipSlot = (category.Handedness == WeaponHandedness.TwoHanded) ? EquipmentSlot.MainHand : category.WeaponType == WeaponType.Projectile ? EquipmentSlot.MainHand : EquipmentSlot.OffHand;
        
        item.Handedness = category.Handedness;
        item.WeaponType = category.WeaponType;

        // --- Parse Damage for Single and Double Weapons ---
        // Handles "1d6/1d6" formats found in Double weapons
        if (item.DamageInfo == null) item.DamageInfo = new Godot.Collections.Array<DamageInfo>();
        item.DamageInfo.Clear();

        string[] damageParts = dmg.Split('/');
        string[] typeParts = type.Split(new[] { " or " }, StringSplitOptions.None);
        
        for(int i = 0; i < damageParts.Length; i++)
        {
            string currentDmg = damageParts[i].Trim();
            var dmgInfo = new DamageInfo { MultipliesOnCrit = true };
            
            if (currentDmg.Contains("d"))
            {
                var parts = currentDmg.Split('d');
                int.TryParse(parts[0], out int diceCount);
                int.TryParse(parts[1], out int dieSides);
                dmgInfo.DiceCount = diceCount;
                dmgInfo.DieSides = dieSides;
            }
            else
            {
                int.TryParse(currentDmg, out int flat);
                dmgInfo.FlatBonus = flat;
            }

            // Map "P", "S", "B" to full names
            string currentType = (i < typeParts.Length) ? typeParts[i].Trim() : typeParts[0].Trim();
            if (currentType == "P") dmgInfo.DamageType = "Piercing";
            else if (currentType == "S") dmgInfo.DamageType = "Slashing";
            else if (currentType == "B") dmgInfo.DamageType = "Bludgeoning";
            else dmgInfo.DamageType = "Physical"; // Fallback

            item.DamageInfo.Add(dmgInfo);
        }

        // --- Parse Critical ---
        // For double weapons, we simplify and take the first critical profile listed.
        string critToParse = crit.Split('/')[0].Trim();
        ParseCritString(critToParse, item);

        // --- Parse Range ---
        // Clean " ft." and parse
        string rangeClean = range.Replace(" ft.", "").Trim();
        if (float.TryParse(rangeClean, out float rangeVal) && rangeVal > 0)
        {
            item.RangeIncrement = rangeVal;
            // If it has range but isn't ammo/projectile, it's thrown
            if (item.WeaponType == WeaponType.Melee) item.WeaponType = WeaponType.Thrown;
        }

        // --- Parse Special Qualities ---
        string lowerSpecial = special.ToLower();
        item.HasBraceFeature = lowerSpecial.Contains("brace");
        item.HasDisarmFeature = lowerSpecial.Contains("disarm");
        item.HasTripFeature = lowerSpecial.Contains("trip");
        item.IsDoubleWeapon = lowerSpecial.Contains("double");
        item.HasBlockingFeature = lowerSpecial.Contains("blocking");
        item.HasDeadlyFeature = lowerSpecial.Contains("deadly");
        item.HasDistractingFeature = lowerSpecial.Contains("distracting");
        item.HasFragileFeature = lowerSpecial.Contains("fragile");
        item.IsMonkWeapon = lowerSpecial.Contains("monk");
        item.HasGrappleFeature = lowerSpecial.Contains("grapple");

        // Specific filtering list from original script to remove keywords that are now bools
        var qualitiesList = special.Split(',')
            .Select(s => s.Trim())
            .Where(s => s != "—" && !string.IsNullOrEmpty(s) && 
                        !s.ToLower().Contains("brace") && 
                        !s.ToLower().Contains("disarm") && 
                        !s.ToLower().Contains("trip") && 
                        !s.ToLower().Contains("double") && 
                        !s.ToLower().Contains("blocking") && 
                        !s.ToLower().Contains("deadly") && 
                        !s.ToLower().Contains("distracting") && 
                        !s.ToLower().Contains("fragile") && 
                        !s.ToLower().Contains("monk") && 
                        !s.ToLower().Contains("grapple"))
            .ToList();

        if (item.SpecialQualities == null) item.SpecialQualities = new Godot.Collections.Array<string>();
        item.SpecialQualities.Clear();
        foreach(var q in qualitiesList) item.SpecialQualities.Add(q);
            
        ResourceSaver.Save(item, assetPath);
    }
    
    // Helper method to parse a single critical string segment
    private static void ParseCritString(string critString, Item_SO item)
    {
        item.CriticalThreatRange = 20; // Default
        item.CriticalMultiplier = 2;   // Default

        critString = critString.Trim();
        if (critString.Contains("/"))
        {
            var parts = critString.Split('/');
            if (parts[0].Contains("–") || parts[0].Contains("-"))
            {
                var rangeParts = parts[0].Split(new[]{'–', '-'});
                int.TryParse(rangeParts[0], out int tRange);
                item.CriticalThreatRange = tRange;
            }
            int.TryParse(parts[1].Replace("x", ""), out int mult);
            item.CriticalMultiplier = mult;
        }
        else if (critString.Contains("x"))
        {
            int.TryParse(critString.Replace("x", ""), out int mult);
            item.CriticalMultiplier = mult;
        }
        else if (critString.Contains("-") || critString.Contains("–"))
        {
             var rangeParts = critString.Split(new[]{'–', '-'});
             int.TryParse(rangeParts[0], out int tRange);
             item.CriticalThreatRange = tRange;
        }
    }

    private static WeaponCategoryInfo GetWeaponCategoryFromFile(string fileName)
    {
        var info = new WeaponCategoryInfo { Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Melee };
        string lowerFileName = fileName.ToLower();

        if (lowerFileName.Contains("light")) info.Handedness = WeaponHandedness.Light;
        if (lowerFileName.Contains("two-handed")) info.Handedness = WeaponHandedness.TwoHanded;
        
        if (lowerFileName.Contains("ranged") || lowerFileName.Contains("ammunition")) info.WeaponType = WeaponType.Projectile;
        if (lowerFileName.Contains("thrown")) info.WeaponType = WeaponType.Thrown;

        return info;
    }
}
#endif