using Godot;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

[GlobalClass]
public partial class AbilityMechanicMapping : Resource
{
    [Export] public string AbilityName;
    [Export] public Godot.Collections.Array<AbilityEffectComponent> EffectComponents = new();
}

[GlobalClass]
public partial class AbilityMechanicsDatabase : Resource
{
    [Export] public Godot.Collections.Array<AbilityMechanicMapping> Mappings = new();

    private Dictionary<string, Godot.Collections.Array<AbilityEffectComponent>> _lookup;

    public void Initialize()
    {
        _lookup = new Dictionary<string, Godot.Collections.Array<AbilityEffectComponent>>(StringComparer.OrdinalIgnoreCase);
        if (Mappings == null) return;

        foreach (var mapping in Mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping?.AbilityName)) continue;
            if (!_lookup.ContainsKey(mapping.AbilityName))
            {
                _lookup.Add(mapping.AbilityName, mapping.EffectComponents);
            }
			
            string normalized = NormalizeAbilityLookupKey(mapping.AbilityName);
            if (!_lookup.ContainsKey(normalized))
            {
                _lookup.Add(normalized, mapping.EffectComponents);
            }
        }
    }

    public bool TryGetMechanics(string abilityName, out Godot.Collections.Array<AbilityEffectComponent> components)
    {
         if (_lookup == null) Initialize();

        components = null;
        if (string.IsNullOrWhiteSpace(abilityName)) return false;

        if (_lookup.TryGetValue(abilityName, out components)) return true;

        string normalized = NormalizeAbilityLookupKey(abilityName);
        if (_lookup.TryGetValue(normalized, out components)) return true;

        if (TryBuildGenericCureWoundsMechanics(abilityName, out components)) return true;
		if (TryBuildGenericSummonMonsterMechanics(abilityName, out components)) return true;
        return TryBuildGenericHealMechanics(abilityName, out components);
    }


    private static bool TryBuildGenericHealMechanics(string abilityName, out Godot.Collections.Array<AbilityEffectComponent> components)
    {
        components = null;

        string normalized = NormalizeAbilityLookupKey(abilityName);
        string coreName = Regex.Replace(normalized, @"\s*\(mythic\)\s*$", "", RegexOptions.IgnoreCase).Trim();
        coreName = Regex.Replace(coreName, @"^mythic\s+", "", RegexOptions.IgnoreCase).Trim();

        bool isHeal = Regex.IsMatch(coreName, @"^heal(?:\s*,\s*mass)?$", RegexOptions.IgnoreCase);
        if (!isHeal) return false;

        bool isMass = coreName.Contains(",");
        int maxHealing = isMass ? 250 : 150;

        components = new Godot.Collections.Array<AbilityEffectComponent>
        {
            new HealEffect
            {
                UseFlatAmountPerCasterLevel = true,
                FlatAmountPerCasterLevel = 10,
                FlatAmountMaximum = maxHealing,
                DamageUndeadInstead = true,
                UndeadDamageType = "Positive"
            },
            new HealAbilityDamageEffect
            {
                AmountToHeal = 999,
                TargetChoosesStat = false,
                SpecificScore = AbilityScore.None
            },
            new Effect_RemoveAffliction { TagToRemove = EffectTag.Poison, AutoSucceed = true },
            new Effect_RemoveAffliction { TagToRemove = EffectTag.Disease, AutoSucceed = true }
        };

        var removableConditions = new[]
        {
            Condition.Blinded,
            Condition.Confused,
            Condition.Dazed,
            Condition.Dazzled,
            Condition.Deafened,
            Condition.Exhausted,
            Condition.Fatigued,
            Condition.Nauseated,
            Condition.Sickened,
            Condition.Stunned
        };

        foreach (var condition in removableConditions)
        {
            components.Add(new Effect_RemoveStatus { ConditionToRemove = condition });
        }

        components.Add(new Effect_RemoveStatus { CheckSpecificName = true, EffectName = "Feeblemind" });
        components.Add(new Effect_RemoveStatus { CheckSpecificName = true, EffectName = "Insanity" });

        return true;
    }
    private static string NormalizeAbilityLookupKey(string abilityName)
    {
        string normalized = Regex.Replace(abilityName.Trim(), @"\s+", " ");

        // Normalize mass naming variants to a single form.
        var massPrefixMatch = Regex.Match(normalized, @"^mass\s+(cure\s+.+)$", RegexOptions.IgnoreCase);
        if (massPrefixMatch.Success)
        {
            normalized = $"{massPrefixMatch.Groups[1].Value}, Mass";
        }

        // Strip non-mythic source suffixes, e.g. "(Core Rulebook)".
        var trailingTag = Regex.Match(normalized, @"^(?<core>.+?)\s*\((?<tag>[^)]+)\)$");
        if (trailingTag.Success && !trailingTag.Groups["tag"].Value.Equals("Mythic", StringComparison.OrdinalIgnoreCase))
        {
            normalized = trailingTag.Groups["core"].Value.Trim();
        }

        return normalized;
    }


    private static bool TryBuildGenericSummonMonsterMechanics(string abilityName, out Godot.Collections.Array<AbilityEffectComponent> components)
    {
        components = null;

        string normalized = Regex.Replace(abilityName.Trim(), @"\s+", " ");
        string coreName = Regex.Replace(normalized, @"\s*\(mythic\)\s*$", "", RegexOptions.IgnoreCase).Trim();
        coreName = Regex.Replace(coreName, @"^mythic\s+", "", RegexOptions.IgnoreCase).Trim();

        Match match = Regex.Match(coreName, @"^summon\s+monster\s+(?<rank>ix|iv|v?i{0,3}|[1-9])(?:\s*\((?<restriction>[^)]*)\))?$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;

        int spellTier = ParseSummonMonsterTier(match.Groups["rank"].Value);
        if (spellTier < 1 || spellTier > 9) return false;

        string restriction = match.Groups["restriction"].Success ? match.Groups["restriction"].Value.Trim() : string.Empty;

        var summonList = BuildSummonMonsterListForTier(spellTier);

        var effect = new Effect_Summon
        {
            SummonList = summonList,
            DurationRoundsPerLevel = 1,
            RestrictionText = restriction
        };

        ApplySummonMonsterRestriction(effect, restriction);

        components = new Godot.Collections.Array<AbilityEffectComponent> { effect };
        return true;
    }

    private static readonly string[] DefaultElementalRestrictionTags =
    {
        "air",
        "earth",
        "fire",
        "water"
    };

    private static void ApplySummonMonsterRestriction(Effect_Summon effect, string restriction)
    {
        if (effect == null || string.IsNullOrWhiteSpace(restriction)) return;

        string normalized = restriction.Trim().ToLowerInvariant();
        if (normalized.EndsWith(" only")) normalized = normalized[..^5].Trim();

        if (normalized.Contains("elemental"))
        {
            effect.RequiredNameContains = "Elemental";

            var subtypeTokens = new List<string> { "Elemental" };

            // Explicit support for core elemental qualifiers while still allowing extension.
            foreach (var elementalTag in DefaultElementalRestrictionTags)
            {
                if (normalized.Contains(elementalTag))
                {
                    subtypeTokens.Add(CapitalizeToken(elementalTag));
                }
            }

            // Extensible fallback: capture "<tag> elemental(s)" patterns.
            // Example future cases: "void elementals only", "wood elemental only".
            foreach (Match m in Regex.Matches(normalized, @"\b(?<tag>[a-z]+)\s+elementals?\b", RegexOptions.IgnoreCase))
            {
                string tag = m.Groups["tag"].Value.Trim();
                if (string.IsNullOrWhiteSpace(tag)) continue;
                string normalizedTag = CapitalizeToken(tag);
                if (!subtypeTokens.Contains(normalizedTag)) subtypeTokens.Add(normalizedTag);
            }

            effect.RequiredSubtypeContains = string.Join(",", subtypeTokens);
            return;
        }

        // Generic fallback for parenthetical restrictions such as
        // "wolves only" or "fiendish creatures only".
        effect.RequiredNameContains = restriction.Trim();
    }

    private static string CapitalizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string token = value.Trim().ToLowerInvariant();
        return char.ToUpperInvariant(token[0]) + token[1..];
    }



    private static SummonList_SO BuildSummonMonsterListForTier(int spellTier)
    {
        var list = new SummonList_SO { SpellLevel = spellTier };

        foreach (var option in BuildSummonMonsterCatalog())
        {
            if (option.ListLevel > spellTier) continue;

            int countDice = 1;
            int countBonus = 0;
            if (option.ListLevel == spellTier)
            {
                countDice = 1;
                countBonus = 0;
            }
            else if (option.ListLevel == spellTier - 1)
            {
                countDice = 3;
                countBonus = 0;
            }
            else
            {
                countDice = 4;
                countBonus = 1;
            }

            list.Entries.Add(new SummonEntry
            {
                CreatureNameFallback = option.CreatureName,
                SourceListLevel = option.ListLevel,
                CountDice = countDice,
                CountBonus = countBonus,
                ApplyCelestialIfGood = option.UsesAlignmentTemplate,
                ApplyFiendishIfEvil = option.UsesAlignmentTemplate,
                SubtypesCsv = option.SubtypesCsv,
                IsAlternativeOption = option.IsAlternativeOption
            });
        }

        return list;
    }
    private static int ParseSummonMonsterTier(string rankToken)
    {
        string r = rankToken.Trim().ToUpperInvariant();
        return r switch
        {
            "I" => 1,
            "II" => 2,
            "III" => 3,
            "IV" => 4,
            "V" => 5,
            "VI" => 6,
            "VII" => 7,
            "VIII" => 8,
            "IX" => 9,
            _ when int.TryParse(r, out int numeric) => numeric,
            _ => 0
        };
    }

    private sealed class SummonCatalogOption
    {
        public int ListLevel;
        public string CreatureName;
        public string SubtypesCsv;
        public bool IsAlternativeOption;
        public bool UsesAlignmentTemplate;
    }

    private static List<SummonCatalogOption> BuildSummonMonsterCatalog()
    {
        var options = new List<SummonCatalogOption>();

        static void Add(List<SummonCatalogOption> list, int level, string name, string subtypes = "", bool alt = false, bool template = false)
        {
            list.Add(new SummonCatalogOption
            {
                ListLevel = level,
                CreatureName = name,
                SubtypesCsv = subtypes,
                IsAlternativeOption = alt,
                UsesAlignmentTemplate = template
            });
        }

        Add(options, 1, "Dire rat", template: true); Add(options, 1, "Dog", template: true); Add(options, 1, "Dolphin", template: true); Add(options, 1, "Eagle", template: true); Add(options, 1, "Fire beetle", template: true); Add(options, 1, "Poisonous frog", template: true); Add(options, 1, "Pony (horse)", template: true); Add(options, 1, "Viper (snake)", template: true); Add(options, 1, "Bloody human skeleton", alt: true);

        Add(options, 2, "Ant, giant (worker)", template: true); Add(options, 2, "Elemental (Small)", "Elemental"); Add(options, 2, "Giant centipede", template: true); Add(options, 2, "Giant frog", template: true); Add(options, 2, "Giant spider", template: true); Add(options, 2, "Goblin dog", template: true); Add(options, 2, "Horse", template: true); Add(options, 2, "Hyena", template: true); Add(options, 2, "Lemure (devil)", "Evil,Lawful"); Add(options, 2, "Octopus", template: true); Add(options, 2, "Squid", template: true); Add(options, 2, "Wolf", template: true);
        Add(options, 2, "Akata", alt: true); Add(options, 2, "Elk", alt: true, template: true); Add(options, 2, "Grig", alt: true); Add(options, 2, "Hell hound", "Evil,Lawful", alt: true); Add(options, 2, "Merfolk", alt: true, template: true); Add(options, 2, "Reefclaw", alt: true); Add(options, 2, "Venomous snake", alt: true, template: true);

        Add(options, 3, "Ant, giant (soldier)", template: true); Add(options, 3, "Ape", template: true); Add(options, 3, "Aurochs (herd animal)", template: true); Add(options, 3, "Boar", template: true); Add(options, 3, "Cheetah", template: true); Add(options, 3, "Constrictor snake", template: true); Add(options, 3, "Crocodile", template: true); Add(options, 3, "Dire bat", template: true); Add(options, 3, "Dretch (demon)", "Chaotic,Evil"); Add(options, 3, "Electric eel", template: true); Add(options, 3, "Lantern archon", "Good,Lawful"); Add(options, 3, "Leopard (cat)", template: true); Add(options, 3, "Monitor lizard", template: true); Add(options, 3, "Shark", template: true); Add(options, 3, "Wolverine", template: true);
        Add(options, 3, "Blink dog", alt: true, template: true); Add(options, 3, "Choker", alt: true); Add(options, 3, "Dire boar", alt: true, template: true); Add(options, 3, "Human natural wererat rogue 2", alt: true); Add(options, 3, "Iron cobra (no poison)", alt: true); Add(options, 3, "Nosoi psychopomp", alt: true); Add(options, 3, "Silvanshee agathion", "Good", alt: true);

        Add(options, 4, "Ant, giant (drone)", template: true); Add(options, 4, "Bison (herd animal)", template: true); Add(options, 4, "Deinonychus (dinosaur)", template: true); Add(options, 4, "Dire ape", template: true); Add(options, 4, "Dire boar", template: true); Add(options, 4, "Dire wolf", template: true); Add(options, 4, "Elemental (Medium)", "Elemental"); Add(options, 4, "Giant scorpion", template: true); Add(options, 4, "Giant wasp", template: true); Add(options, 4, "Grizzly bear", template: true); Add(options, 4, "Hell hound", "Evil,Lawful"); Add(options, 4, "Hound archon", "Good,Lawful"); Add(options, 4, "Lion", template: true); Add(options, 4, "Mephit (any)", "Elemental"); Add(options, 4, "Pteranodon (dinosaur)", template: true); Add(options, 4, "Rhinoceros", template: true);
        Add(options, 4, "Amphisbaena", alt: true); Add(options, 4, "Cerberi", "Evil,Lawful", alt: true); Add(options, 4, "Choker", alt: true); Add(options, 4, "Giant mantis", alt: true, template: true); Add(options, 4, "Gibbering mouther", alt: true, template: true); Add(options, 4, "Grick", alt: true, template: true); Add(options, 4, "Tiger", alt: true, template: true);

        Add(options, 5, "Ankylosaurus (dinosaur)", template: true); Add(options, 5, "Babau (demon)", "Chaotic,Evil"); Add(options, 5, "Bearded devil", "Evil,Lawful"); Add(options, 5, "Bralani azata", "Chaotic,Good"); Add(options, 5, "Dire lion", template: true); Add(options, 5, "Elemental (Large)", "Elemental"); Add(options, 5, "Giant moray eel", template: true); Add(options, 5, "Kyton", "Evil,Lawful"); Add(options, 5, "Orca (dolphin)", template: true); Add(options, 5, "Salamander", "Evil"); Add(options, 5, "Woolly rhinoceros", template: true); Add(options, 5, "Xill", "Evil");
        Add(options, 5, "Emperor cobra", alt: true, template: true); Add(options, 5, "Cloaker", alt: true, template: true); Add(options, 5, "Merrow, saltwater", alt: true); Add(options, 5, "Shadow mastiff", "Evil", alt: true); Add(options, 5, "Vulpinal agathion", "Good", alt: true);

        Add(options, 6, "Dire bear", template: true); Add(options, 6, "Dire tiger", template: true); Add(options, 6, "Elasmosaurus (dinosaur)", template: true); Add(options, 6, "Elemental (Huge)", "Elemental"); Add(options, 6, "Elephant", template: true); Add(options, 6, "Erinyes (devil)", "Evil,Lawful"); Add(options, 6, "Giant octopus", template: true); Add(options, 6, "Invisible stalker", "Air"); Add(options, 6, "Lillend azata", "Chaotic,Good"); Add(options, 6, "Shadow demon", "Chaotic,Evil"); Add(options, 6, "Shadow mastiff", "Evil"); Add(options, 6, "Succubus (demon)", "Chaotic,Evil"); Add(options, 6, "Triceratops (dinosaur)", template: true);
        Add(options, 6, "Bulette", alt: true); Add(options, 6, "Chaos Beast", "Chaotic", alt: true); Add(options, 6, "Griffon", alt: true, template: true); Add(options, 6, "Mothman", alt: true); Add(options, 6, "Tylosaurus (dinosaur)", alt: true); Add(options, 6, "Vanth psychopomp", alt: true);

        Add(options, 7, "Bebelith", "Chaotic,Evil"); Add(options, 7, "Bone devil", "Evil,Lawful"); Add(options, 7, "Brachiosaurus (dinosaur)", template: true); Add(options, 7, "Dire crocodile", template: true); Add(options, 7, "Dire shark", template: true); Add(options, 7, "Elemental (greater)", "Elemental"); Add(options, 7, "Giant squid", template: true); Add(options, 7, "Mastodon (elephant)", template: true); Add(options, 7, "Roc", template: true); Add(options, 7, "Tyrannosaurus (dinosaur)", template: true); Add(options, 7, "Vrock (demon)", "Chaotic,Evil");
        Add(options, 7, "Behir", alt: true); Add(options, 7, "Daughter of the Dead", alt: true); Add(options, 7, "Emkrah", alt: true); Add(options, 7, "Giant anaconda", alt: true, template: true); Add(options, 7, "Young frost giant", alt: true, template: true);

        Add(options, 8, "Barbed devil", "Evil,Lawful"); Add(options, 8, "Elemental (elder)", "Elemental"); Add(options, 8, "Hezrou (demon)", "Chaotic,Evil");
        Add(options, 8, "Frost giant", alt: true, template: true); Add(options, 8, "Gorgon", alt: true); Add(options, 8, "Young cloud giant", alt: true, template: true);

        Add(options, 9, "Astral deva (angel)", "Good"); Add(options, 9, "Ghaele azata", "Chaotic,Good"); Add(options, 9, "Glabrezu (demon)", "Chaotic,Evil"); Add(options, 9, "Ice devil", "Evil,Lawful"); Add(options, 9, "Nalfeshnee (demon)", "Chaotic,Evil"); Add(options, 9, "Trumpet archon", "Good,Lawful");
        Add(options, 9, "Cloud giant", alt: true, template: true); Add(options, 9, "Young storm giant", alt: true, template: true);

        return options;
    }
    private static bool TryBuildGenericCureWoundsMechanics(string abilityName, out Godot.Collections.Array<AbilityEffectComponent> components)
    {
        components = null;

        string normalized = NormalizeAbilityLookupKey(abilityName);
        bool isMythic = Regex.IsMatch(normalized, @"\bmythic\b", RegexOptions.IgnoreCase);

        string coreName = Regex.Replace(normalized, @"\s*\(mythic\)\s*$", "", RegexOptions.IgnoreCase).Trim();
        coreName = Regex.Replace(coreName, @"^mythic\s+", "", RegexOptions.IgnoreCase).Trim();

        var match = Regex.Match(coreName, @"^cure\s+(light|moderate|serious|critical)\s+wounds(?:\s*,\s*mass)?$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;

        string severity = match.Groups[1].Value.ToLowerInvariant();
        bool isMass = coreName.Contains(",");

        int baseDice = severity switch
        {
			"light" => 1,
            "moderate" => 2,
            "serious" => 3,
            "critical" => 4,
            _ => 0
        };
        if (baseDice <= 0) return false;

        int baseMax = baseDice * 5;
        int standardMax = isMass ? baseMax + 20 : baseMax;

        var heal = new HealEffect
        {
            DiceCount = isMythic ? baseDice * 2 : baseDice,
            DieSides = 8,
            ScalingFlatBonusPerCasterLevel = isMythic ? 2 : 1,
            MaxScalingBonus = isMythic && !isMass ? baseMax * 2 : standardMax
        };

        components = new Godot.Collections.Array<AbilityEffectComponent> { heal };

        // Specific mythic rider requested for Cure Critical Wounds.
        if (isMythic && severity == "critical")
        {
            components.Add(new HealAbilityDamageEffect
            {
                AmountToHeal = 4,
                TargetChoosesStat = true
            });
        }
        
        return true;
    }
}
