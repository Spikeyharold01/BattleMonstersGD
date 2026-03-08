using Godot;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public static class ItemParser 
{
    public static Item_SO GetOrCreateComplexGearAsset(string name, string path, Dictionary<string, Item_SO> itemCache)
    {
        if (string.IsNullOrEmpty(name)) return null;

        string originalName = name.Trim();
        if (itemCache.TryGetValue(originalName, out var cachedItem)) return cachedItem;

        string safeName = string.Join("_", originalName.Split(Path.GetInvalidFileNameChars()));
        // Note: 'path' must be a valid Godot path (res://...)
        string assetPath = $"{path}/Gear_{safeName}.tres"; 
        
        Item_SO item = null;
        if (FileAccess.FileExists(assetPath))
        {
            item = GD.Load<Item_SO>(assetPath);
        }

        if (item != null)
        {
            itemCache[originalName] = item;
            return item; 
        }
        
        item = new Item_SO();
        item.ItemName = originalName;

        string lowerName = originalName.ToLower();

        // --- Parsing Logic (Regex adapted to set Properties instead of Fields) ---
        // P1: Multi-stat Belts
        var physicalBeltMatch = Regex.Match(lowerName, @"belt of physical might \+(\d+).*?\[(str|dex|con),\s*(str|dex|con)\]");
        if (physicalBeltMatch.Success)
        {
            int bonus = int.Parse(physicalBeltMatch.Groups[1].Value);
            item.ItemType = ItemType.Gear;
            item.EquipSlot = EquipmentSlot.Belt;
            item.IsEquippable = true;
            item.Modifications.Add(new StatModification { StatToModify = ImportUtils.ParseEnum(physicalBeltMatch.Groups[2].Value, StatToModify.None), ModifierValue = bonus, BonusType = BonusType.Enhancement });
            item.Modifications.Add(new StatModification { StatToModify = ImportUtils.ParseEnum(physicalBeltMatch.Groups[3].Value, StatToModify.None), ModifierValue = bonus, BonusType = BonusType.Enhancement });
        }
        // ... (Repeating similar logic with Property capitalization) ...
        // Simplification for brevity in this response, follow same pattern as Unity parser but Property Case.
        else
        {
             var match = Regex.Match(originalName, @"^(\+\d+\s*)?(mwk\s*)?(masterwork\s*)?(mithral\s*)?(adamantine\s*)?(cold iron\s*)?([a-zA-Z\s\']+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                int enhancementBonus = 0;
                if (match.Groups[1].Success) int.TryParse(match.Groups[1].Value.Trim().TrimStart('+'), out enhancementBonus);
                
                string materialStr = "";
                if (match.Groups[4].Success) materialStr = "mithral";
                else if (match.Groups[5].Success) materialStr = "adamantine";
                else if (match.Groups[6].Success) materialStr = "cold iron";
                
                string baseItemName = match.Groups[7].Value.Trim();
                string lowerBaseName = baseItemName.ToLower();

                item.IsEquippable = true;
                if (!string.IsNullOrEmpty(materialStr)) item.Material = ImportUtils.ParseEnum(materialStr, WeaponMaterial.Normal);

                if (lowerBaseName.Contains("plate") || lowerBaseName.Contains("mail") || lowerBaseName.Contains("leather") || lowerBaseName.Contains("hide") || lowerBaseName.Contains("armor"))
                {
                    item.ItemType = ItemType.Armor;
                    item.EquipSlot = EquipmentSlot.Armor;
                    // ... Armor Category logic ...
                    if (enhancementBonus > 0) item.Modifications.Add(new StatModification { StatToModify = StatToModify.ArmorClass, BonusType = BonusType.Enhancement, ModifierValue = enhancementBonus });
                }
                else if (ItemParser.IsManufacturedWeapon(lowerBaseName))
                {
                    item.ItemType = ItemType.Weapon;
                    item.EquipSlot = EquipmentSlot.MainHand;
                    item.DamageInfo.Add(new DamageInfo()); 

                    if (enhancementBonus > 0)
                    {
                        item.Modifications.Add(new StatModification { StatToModify = StatToModify.AttackRoll, BonusType = BonusType.Enhancement, ModifierValue = enhancementBonus });
                        item.Modifications.Add(new StatModification { StatToModify = StatToModify.MeleeDamage, BonusType = BonusType.Enhancement, ModifierValue = enhancementBonus });
                    }
                    // ... Handedness / Bow logic ...
                }
                // ... Rest of logic ...
            }
        }
        
        // Final Save
        if (item.IsEquippable || item.ItemType == ItemType.Potion)
        {
            // Godot Saving
            ResourceSaver.Save(item, assetPath);
            itemCache[originalName] = item;
            return item;
        }

        // Garbage collection handles the instance in C#, no DestroyImmediate needed.
        return null;
    }

    // Helper for Weapon Assets
    public static Item_SO GetOrCreateWeaponAsset(string rawAttackString, string path, bool isRanged, Dictionary<string, Item_SO> itemCache)
    {
        Match nameMatch = Regex.Match(rawAttackString, @"^(\+\d+\s+)?(mwk\s+)?([^\(]+)");
        string weaponName = nameMatch.Groups[3].Value.Trim();
        // ... Logic to clean weapon name ...
        if (weaponName.Contains(" or ")) weaponName = weaponName.Split(new[] { " or " }, StringSplitOptions.None)[0].Trim();
        if (string.IsNullOrEmpty(weaponName)) return null;

        if (itemCache.TryGetValue(weaponName, out var cachedItem)) return cachedItem;

        string safeName = string.Join("_", weaponName.Split(Path.GetInvalidFileNameChars()));
        string assetPath = $"{path}/Weapon_{safeName}.tres";

        Item_SO item = null;
        if (FileAccess.FileExists(assetPath)) item = GD.Load<Item_SO>(assetPath);

        if (item == null)
        {
            item = new Item_SO();
            item.ItemName = weaponName;
            item.ItemType = ItemType.Weapon;
            item.IsEquippable = true;
            item.EquipSlot = EquipmentSlot.MainHand;
            
            // ... Logic to extract dice/crit from raw string ...
            // Use ImportUtils.ParseComplexDamageString
            
            ResourceSaver.Save(item, assetPath);
        }
        itemCache[weaponName] = item;
        return item;
    }

    public static List<Item_SO> ParseTreasureString(string rawTreasure, string itemPath, CreatureTemplate_SO template, Dictionary<string, Item_SO> itemCache)
    {
        var gearList = new List<Item_SO>();
        if (string.IsNullOrEmpty(rawTreasure)) return gearList;

        string cleaned = rawTreasure.Trim('\'', '[', ']');
        cleaned = Regex.Replace(cleaned, @"(,\s*)?other (treasure|gear|treasures|items)", "", RegexOptions.IgnoreCase);
        // ... Cleaning logic ...

        string[] items = cleaned.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        // Important: In Godot C#, accessing arrays inside Resources checks for null automatically? 
        // Ensure StartingEquipment is initialized in Template_SO
        var equippedWeaponNames = new HashSet<string>(template.StartingEquipment.Select(i => i.ItemName.ToLower()));

        foreach (var itemStr in items)
        {
            string trimmedItem = itemStr.Trim();
            if (string.IsNullOrEmpty(trimmedItem) || trimmedItem == "—") continue;
            // ... Filtering logic ...
            
            Item_SO gearItem = GetOrCreateComplexGearAsset(trimmedItem, itemPath, itemCache);
            if (gearItem != null)
            {
                gearList.Add(gearItem);
            }
        }
        return gearList;
    }

    public static bool IsManufacturedWeapon(string name)
    {
        string[] weaponKeywords = { "sword", "mace", "axe", "dagger", "bow", "crossbow", "sling", "spear", "lance", "flail", "hammer", "scythe", "glaive", "guisarme", "ranseur", "halberd", "falchion", "scimitar", "rapier", "star", "whip", "rock" };
        string lowerName = name.ToLower();
        return weaponKeywords.Any(kw => lowerName.Contains(kw));
    }
}