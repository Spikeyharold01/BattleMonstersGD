using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: WeaponDamageConverter.cs (GODOT VERSION)
// PURPOSE: A static helper to scale weapon damage based on wielder and weapon size.
// ATTACH TO: Do not attach (Static Class).
// =================================================================================================
public static class WeaponDamageConverter
{
// Represents one row in the damage progression table.
private static readonly List<string> Progression = new List<string>
{
"1", "1d2", "1d3", "1d4", "1d6", "1d8", "1d10", "1d12"
};

// Special cases not in the simple progression
private static readonly Dictionary<string, string> SpecialNextStep = new Dictionary<string, string>
{
    { "1d8", "2d6" }, { "1d10", "2d8" }, { "1d12", "3d6" },
    { "2d6", "3d6" }, { "2d8", "3d8" }, { "3d6", "4d6" },
    { "3d8", "4d8" }, { "4d6", "6d6" }, { "4d8", "6d8" }
};

 public static (int diceCount, int dieSides) GetSizedDamage(ItemInstance weaponInstance, CreatureSize wielderSize)
{
    int sizeDifference = (int)wielderSize - (int)weaponInstance.DesignedForSize;

    string currentDamageString = $"{weaponInstance.ItemData.DamageInfo[0].DiceCount}d{weaponInstance.ItemData.DamageInfo[0].DieSides}";

    if (sizeDifference == 0)
    {
        return (weaponInstance.ItemData.DamageInfo[0].DiceCount, weaponInstance.ItemData.DamageInfo[0].DieSides);
    }

    // Apply steps up or down the progression table
    for (int i = 0; i < Mathf.Abs(sizeDifference); i++)
    {
        currentDamageString = (sizeDifference > 0) ? GetNextDamageStep(currentDamageString) : GetPreviousDamageStep(currentDamageString);
    }

    // Parse the final string back into dice and sides
    if (currentDamageString.Contains("d"))
    {
        var parts = currentDamageString.Split('d');
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }
    else
    {
        if(int.TryParse(currentDamageString, out int flatDmg) && flatDmg == 1) return (1,1);
        return (0, 0);
    }
}

private static string GetNextDamageStep(string damage)
{
    if (SpecialNextStep.ContainsKey(damage)) return SpecialNextStep[damage];
    int index = Progression.IndexOf(damage);
    if (index != -1 && index < Progression.Count - 1) return Progression[index + 1];
    return damage; 
}

private static string GetPreviousDamageStep(string damage)
{
    foreach (var pair in SpecialNextStep)
    {
        if (pair.Value == damage) return pair.Key;
    }
    int index = Progression.IndexOf(damage);
    if (index > 0) return Progression[index - 1];
    return damage; 
}
}