using System;
using System.Collections.Generic;

// Standard C# classes for JSON deserialization.
// Note: Property names must match JSON keys exactly if using standard JSON parsers, 
// or use [JsonProperty] attributes if using Newtonsoft.Json.

[Serializable]
public class AttackList
{
    public List<AttackData> entries;
}

[Serializable]
public class AttackData
{
    public string attack; 
    public int count; 
    public List<int> bonus; 
    public List<List<AttackEffect>> entries;
}

[Serializable]
public class AttackEffect
{
    public string damage; 
    public string type; 
    public string crit_range; 
    public int crit_multiplier; 
    public string effect;
}

[Serializable]
public class SlaList
{
    public List<SlaEntry> entries;
}

[Serializable]
public class SlaEntry
{
    public string name; 
    public string freq; 
    public int DC; 
    public string CL; 
    public string source; 
    public string other; 
    public string school; 
    public bool is_mythic_spell;
}

[Serializable]
public class SpecialAbilityList
{
    public List<SpecialAbilityEntry> entries;
}

[Serializable]
public class SpecialAbilityEntry
{
    public string name; 
    public string type; 
    public string text;
}

[Serializable]
public class PsychicMagicList
{
    public List<PsychicMagicEntry> entries;
}

[Serializable]
public class PsychicMagicEntry
{
    public string name; 
    public int PE; 
    public int DC;
}

// NOTE: This class conflicts with DurationData in Ability_SO.cs.
// Since this is for Import JSON parsing, let's rename it slightly to avoid ambiguity
// or use the Namespace feature. For now, I will rename it ImportDurationData.
[Serializable]
public class ImportDurationData
{
    public int baseRounds = 0;
    public int diceCount = 0;
    public int dieSides = 0;
}