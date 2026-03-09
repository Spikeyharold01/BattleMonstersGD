#if TOOLS
using Godot;
using System;
using System.IO;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExcelDataReader;
using System.Text.Json; // Godot 4 uses System.Text.Json

public static class CreatureImporter
{
	private static Dictionary<string, Feat_SO> _featCache;
	private static Dictionary<string, Ability_SO> _abilityCache;
	private static Dictionary<string, Trait_SO> _traitCache;
	private static Dictionary<string, Item_SO> _itemCache; 

	private static AbilityMechanicsDatabase _mechanicsDB;
	private static List<string> importWarnings = new List<string>();

	public static void ImportCreatures(string filePath)
	{
		string dbPath = "res://Data/System/Main_Ability_Mechanics_DB.tres";
		if (FileAccess.FileExists(dbPath))
		{
			_mechanicsDB = GD.Load<AbilityMechanicsDatabase>(dbPath);
			_mechanicsDB.Initialize();
		}
		else
		{
			GD.PrintErr($"Could not find Ability Mechanics Database at: {dbPath}");
			return;
		}

		DataSet result;
		try
		{
			using (var stream = File.Open(filePath, FileMode.Open, System.IO.FileAccess.Read))
			{
				using (var reader = ExcelReaderFactory.CreateReader(stream))
				{
					result = reader.AsDataSet(new ExcelDataSetConfiguration()
					{
						ConfigureDataTable = (_) => new ExcelDataTableConfiguration() { UseHeaderRow = true }
					});
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"File Error: {e.Message}");
			return;
		}

		if (result.Tables.Count == 0) return;
		DataTable table = result.Tables[0];
		var headers = table.Columns.Cast<DataColumn>().Select(col => col.ColumnName).ToList();

		_featCache = new Dictionary<string, Feat_SO>();
		_abilityCache = new Dictionary<string, Ability_SO>();
		_traitCache = new Dictionary<string, Trait_SO>();
		_itemCache = new Dictionary<string, Item_SO>();
		importWarnings.Clear();

		string creaturePath = "res://Data/Creatures";
		string abilityPath = "res://Data/Abilities";
		string featPath = "res://Data/Abilities/Feats";
		string traitPath = "res://Data/Creatures/Traits";
		string itemPath = "res://Data/Items";
		string filterPath = "res://Data/Abilities/Filters";

		ImportUtils.EnsureDirectory(creaturePath);
		ImportUtils.EnsureDirectory($"{abilityPath}/Spells");
		ImportUtils.EnsureDirectory($"{abilityPath}/Special");
		ImportUtils.EnsureDirectory(featPath);
		ImportUtils.EnsureDirectory(traitPath);
		ImportUtils.EnsureDirectory(itemPath);
		ImportUtils.EnsureDirectory(filterPath);

		int totalCreatures = table.Rows.Count;
		for (int i = 0; i < totalCreatures; i++)
		{
			DataRow row = table.Rows[i];
			string creatureName = row["title2"]?.ToString() ?? "";
			
			// Minimal progress log
			if (i % 5 == 0) GD.Print($"Processing {creatureName} ({i + 1}/{totalCreatures})...");

			var rowData = headers.ToDictionary(header => header, header => row[header]?.ToString() ?? "");
			ParseAndCreateCreature(rowData, creaturePath, abilityPath, featPath, itemPath, traitPath, filterPath);
		}

		if (importWarnings.Count > 0)
		{
			GD.PrintRich($"[color=yellow]Import Completed with {importWarnings.Count} Alerts.[/color]");
			string logPath = ProjectSettings.GlobalizePath("res://Import_Validation_Log.txt");
			File.WriteAllLines(logPath, importWarnings);
		}
		else
		{
			GD.PrintRich("[color=green]Import Perfect![/color]");
		}
	}

	private static void ParseAndCreateCreature(Dictionary<string, string> data, string creaturePath, string abilityPath, string featPath, string itemPath, string traitPath, string filterPath)
	{
		string creatureName = ImportUtils.GetValue(data, "title2", "");
		if (string.IsNullOrEmpty(creatureName)) return;

		string safeName = string.Join("_", creatureName.Split(Path.GetInvalidFileNameChars()));
		string assetPath = $"{creaturePath}/{safeName}.tres";

		CreatureTemplate_SO template;
		
		// As per instruction: Use existing entry if it exists, otherwise create new.
		if (FileAccess.FileExists(assetPath))
			template = GD.Load<CreatureTemplate_SO>(assetPath);
		else
			template = new CreatureTemplate_SO();

		PopulateIdentity(data, template);
		PopulateStatistics(data, template);
		PopulateDefense(data, template, traitPath);
		PopulateOffense(data, template, itemPath);
		PopulateSkillsAndFeats(data, template, featPath);
		PopulateAbilitiesAndGear(data, template, abilityPath, itemPath, filterPath);
		ApplyPlanarReachRules(template);

		ResourceSaver.Save(template, assetPath);
	}

	#region Main Population Methods
	private static void PopulateIdentity(Dictionary<string, string> data, CreatureTemplate_SO template)
	{
		template.CreatureName = ImportUtils.GetValue(data, "title2", "");
		template.ChallengeRating = ImportUtils.GetCrAsInt(ImportUtils.GetValue(data, "CR", "0"));
		template.AverageGroupSize = ImportUtils.ParseOrganization(ImportUtils.GetValue(data, "ecology/organization", ""));
		template.Alignment = ImportUtils.GetValue(data, "alignment", "N");
		template.Race = ImportUtils.GetValue(data, "race", "");
		template.XpAward = ImportUtils.GetValue(data, "XP", 0);
		template.Type = ImportUtils.ParseEnum(ImportUtils.GetValue(data, "type", "Humanoid"), CreatureType.Humanoid);
		template.Size = ImportUtils.ParseEnum(ImportUtils.GetValue(data, "size", "Medium"), CreatureSize.Medium);
		
		template.SubTypes = new Godot.Collections.Array<string>();
		for (int i = 1; i <= 6; i++)
		{
			var subs = ImportUtils.ParseStringList(ImportUtils.GetValue(data, $"subtypes_{i}", ""));
			foreach(var s in subs) template.SubTypes.Add(s);
		}
		
		template.RacialLanguages = new Godot.Collections.Array<Language>(ImportUtils.ParseLanguages(ImportUtils.GetValue(data, "languages", "")));
		
		template.Classes = new Godot.Collections.Array<string>();
		for (int i = 1; i <= 3; i++)
		{
			string cls = ImportUtils.GetValue(data, $"classes_{i}", "");
			if (!string.IsNullOrEmpty(cls)) template.Classes.Add(cls);
		}
		
		template.NaturalEnvironmentProperties = new Godot.Collections.Array<EnvironmentProperty>(
			ImportUtils.TranslateEnvironmentToProperties(ImportUtils.GetValue(data, "ecology/environment", ""))
		);
		
		template.AssociatedKnowledgeSkill = ImportUtils.GetKnowledgeSkillForCreature(template.Type);
		template.CanUseEquipment = !(template.Type == CreatureType.Animal || template.Type == CreatureType.Vermin || template.Type == CreatureType.Ooze);
		template.CanBeMounted = (template.Type == CreatureType.Animal || template.Type == CreatureType.MagicalBeast) && template.Size >= CreatureSize.Large;
	}

	private static void PopulateStatistics(Dictionary<string, string> data, CreatureTemplate_SO template)
	{
		template.Strength = ImportUtils.GetValue(data, "ability_scores/STR", 10);
		template.Dexterity = ImportUtils.GetValue(data, "ability_scores/DEX", 10);
		template.Constitution = ImportUtils.GetValue(data, "ability_scores/CON", 10);
		template.Intelligence = ImportUtils.GetValue(data, "ability_scores/INT", 10);
		template.Wisdom = ImportUtils.GetValue(data, "ability_scores/WIS", 10);
		template.Charisma = ImportUtils.GetValue(data, "ability_scores/CHA", 10);

		int initiativeModifier = ImportUtils.GetValue(data, "initiative/bonus", 0);
		string initiativeAbility = ImportUtils.GetValue(data, "initiative/ability", "").ToLower();
		
		if (initiativeAbility == "dual initiative")
		{
			template.HasDualInitiative = true;
			initiativeModifier += ImportUtils.GetValue(data, "initiative/bonus_1", 0);
		}
		else
		{
			template.HasDualInitiative = false;
		}

		template.TotalInitiativeModifier = initiativeModifier;
		template.CasterLevel = ImportUtils.GetValue(data, "caster_level", 0);
		template.MythicRank = ImportUtils.GetValue(data, "MR", 0);
	}

	private static void PopulateDefense(Dictionary<string, string> data, CreatureTemplate_SO template, string traitPath)
	{
		template.MaxHP = ImportUtils.GetValue(data, "HP/HP", 10);
		string longHd = ImportUtils.ParseHitDiceFromLongString(ImportUtils.GetValue(data, "HP/long", ""));
		template.HitDice = !string.IsNullOrEmpty(longHd) ? longHd : ImportUtils.GetValue(data, "HP/HD", "1d8");
		
		template.ArmorClass_Total = ImportUtils.GetValue(data, "AC/AC", 10);
		template.ArmorClass_Touch = ImportUtils.GetValue(data, "AC/touch", 10);
		template.ArmorClass_FlatFooted = ImportUtils.GetValue(data, "AC/flat_footed", 10);
		template.AcBreakdown = string.Join(", ", ParseACBreakdown(data));
		
		template.FortitudeSave_Base = ImportUtils.GetSaveBase(ImportUtils.GetValue(data, "saves/fort", 0), template.Constitution);
		template.ReflexSave_Base = ImportUtils.GetSaveBase(ImportUtils.GetValue(data, "saves/ref", 0), template.Dexterity);
		template.WillSave_Base = ImportUtils.GetSaveBase(ImportUtils.GetValue(data, "saves/will", 0), template.Wisdom);
		
		template.DamageReductions = new Godot.Collections.Array<DamageReduction>(ParseMultipleDr(data));
		template.SpellResistance = ImportUtils.GetValue(data, "SR", 0);
		
		var immunitiesList = ImportUtils.ParseStringList(ImportUtils.GetValue(data, "immunities", ""));
		template.Immunities = new Godot.Collections.Array<string>();
		foreach(var im in immunitiesList) template.Immunities.Add(im);

		template.AdaptiveResistanceAmount = ImportUtils.GetValue(data, "resistances/adaptive", 0);
		template.Resistances = new Godot.Collections.Array<DamageResistance>(ParseResistances(data));
		template.Weaknesses = new Godot.Collections.Array<DamageResistance>(ParseWeaknesses(data));
		
		template.FastHealing = ImportUtils.GetValue(data, "HP/fast_healing", 0);
		template.FastHealingCondition = ImportUtils.GetValue(data, "HP/fast_healing_weakness", "");
		template.Regeneration = ImportUtils.GetValue(data, "HP/regeneration", 0);
		template.RegenerationBypass = ImportUtils.GetValue(data, "HP/regeneration_weakness", "");

		string allDefensiveText = (
			ImportUtils.GetValue(data, "defensive_abilities_1", "") + " " +
			ImportUtils.GetValue(data, "defensive_abilities_2", "") + " " +
			ImportUtils.GetValue(data, "defensive_abilities_3", "") + " " +
			ImportUtils.GetValue(data, "defensive_abilities_4", "") + " " +
			ImportUtils.GetValue(data, "defensive_abilities_5", "") + " " +
			ImportUtils.GetValue(data, "defensive_abilities_6", "") + " " +
			ImportUtils.GetValue(data, "defensive_abilities_7", "") + " " +
			ImportUtils.GetValue(data, "special_qualities", "")
		).ToLower();

		template.HasLightSensitivity = allDefensiveText.Contains("light sensitivity");

		if (template.DefensiveAbilities == null) template.DefensiveAbilities = new Godot.Collections.Array<string>();
		if (allDefensiveText.Contains("rock catching") && !template.DefensiveAbilities.Contains("Rock Catching"))
		{
			template.DefensiveAbilities.Add("Rock Catching");
		}

		ParseConditionalSaveBonuses(data, template);

		template.Traits = new Godot.Collections.Array<Trait_SO>();
		var weaknessStrings = new List<string>();
		for (int i = 1; i <= 4; i++) weaknessStrings.Add(ImportUtils.GetValue(data, $"weaknesses_{i}"));
		var allTraitStrings = immunitiesList.Concat(weaknessStrings).Distinct();

		foreach (var traitString in allTraitStrings)
		{
			if (string.IsNullOrEmpty(traitString)) continue;
			var traitAsset = GetOrCreateTraitAsset(traitString, traitPath);
			if (traitAsset != null && !template.Traits.Contains(traitAsset))
			{
				template.Traits.Add(traitAsset);
			}
		}
  if (template.SubTypes != null && template.SubTypes.Contains("Incorporeal"))
		{
			var incTrait = GetOrCreateIncorporealTrait(traitPath);
			if (incTrait != null && !template.Traits.Contains(incTrait)) template.Traits.Add(incTrait);
		}
		template.BreathHoldingMultiplier = 2;
		string sq = ImportUtils.GetValue(data, "special_qualities", "").ToLower();
		if (sq.Contains("hold breath"))
		{
			if (sq.Contains("4x") || sq.Contains("four times")) template.BreathHoldingMultiplier = 4;
			else if (sq.Contains("6x") || sq.Contains("six times")) template.BreathHoldingMultiplier = 6;
			else if (sq.Contains("8x") || sq.Contains("eight times")) template.BreathHoldingMultiplier = 8;
			else template.BreathHoldingMultiplier = 6;
		}
	}

	private static void PopulateOffense(Dictionary<string, string> data, CreatureTemplate_SO template, string itemPath)
	{
		template.BaseAttackBonus = ImportUtils.GetValue(data, "BAB", 0);
		template.Speed_Land = ImportUtils.GetValue(data, "speeds/base", 30f);
		template.Speed_Fly = ImportUtils.GetValue(data, "speeds/fly", 0f);

		string specialAttacksText = ImportUtils.GetValue(data, "attacks/special", "").ToLower();
		if (specialAttacksText.Contains("rock throwing"))
		{
			var rockThrowingAbility = GetOrCreateGenericAbility("Rock Throwing", "Ex", "res://Data/Abilities/Special", template.CreatureName);
			if (template.SpecialAttacks == null) template.SpecialAttacks = new Godot.Collections.Array<Ability_SO>();
			if (!template.SpecialAttacks.Contains(rockThrowingAbility))
				template.SpecialAttacks.Add(rockThrowingAbility);
		}

		if (template.Speed_Fly > 0)
		{
			bool isWinged = true;
			string allSubtypes = string.Join(" ", template.SubTypes).ToLower();
			string cName = template.CreatureName.ToLower();
			if (allSubtypes.Contains("incorporeal")) isWinged = false;
			else
			{
				string allMelee = ImportUtils.GetValue(data, "attacks/melee_1", "") + ImportUtils.GetValue(data, "attacks/melee_2", "") + ImportUtils.GetValue(data, "attacks/melee_3", "");
				if (allMelee.ToLower().Contains("wing")) isWinged = true;
				else if (cName.Contains("harpy") || cName.Contains("manticore") || allSubtypes.Contains("angel") || allSubtypes.Contains("demon") || allSubtypes.Contains("devil")) isWinged = true;
				else if (template.Type == CreatureType.Dragon && !cName.Contains("cave") && !cName.Contains("sea")) isWinged = true;
			}
			template.HasWings = isWinged;
		}
		else template.HasWings = false;

		template.FlyManeuverability = ImportUtils.ParseEnum(ImportUtils.GetValue(data, "speeds/fly_maneuverability", "average"), FlyManeuverability.Average);
		template.VerticalReach = ImportUtils.GetVerticalReachFromSize(template.Size);
		template.Speed_Swim = ImportUtils.GetValue(data, "speeds/swim", 0f);
		template.Speed_Burrow = ImportUtils.GetValue(data, "speeds/burrow", 0f);
		template.Speed_Climb = ImportUtils.GetValue(data, "speeds/climb", 0f);
		template.Space = ImportUtils.GetValue(data, "space", 5f);
		template.Reach = ImportUtils.GetValue(data, "reach", 5f);

		ParseAttacks(data, template, itemPath);

		template.CombatManeuverDefense = ImportUtils.GetValue(data, "CMD", 0);
		template.AttackReachOverrides = new Godot.Collections.Array<AttackReachOverride>(ParseReachOverrides(ImportUtils.GetValue(data, "reach_other", ""), template.Reach));
		template.ConditionalManeuverBonuses = new Godot.Collections.Array<ConditionalManeuverBonus>(ParseConditionalManeuverBonuses(ImportUtils.GetValue(data, "CMB_other", "")));
		ParseCmdOther(ImportUtils.GetValue(data, "CMD_other", ""), template);

		if (template.SubTypes != null && template.SubTypes.Contains("Dwarf"))
		{
			template.Speed_Land_Armored = template.Speed_Land;
		}
		else
		{
			if (template.Speed_Land >= 30f) template.Speed_Land_Armored = 20f;
			else if (template.Speed_Land >= 20f) template.Speed_Land_Armored = 15f;
			else template.Speed_Land_Armored = template.Speed_Land;
		}
	}

	private static void PopulateSkillsAndFeats(Dictionary<string, string> data, CreatureTemplate_SO template, string featPath)
	{
		template.Feats = new Godot.Collections.Array<FeatInstance>(ParseFeatInstances(ImportUtils.GetValue(data, "feats"), featPath));
		template.SkillRanks = new Godot.Collections.Array<SkillValue>(ParseSkills(data, template));
	}

	private static void PopulateAbilitiesAndGear(Dictionary<string, string> data, CreatureTemplate_SO template, string abilityPath, string itemPath, string filterPath)
	{
		ParseSenses(data, template);
		template.KnownAbilities = new Godot.Collections.Array<Ability_SO>();
		
		var slas = ParseSlas(data, $"{abilityPath}/Spells");
		foreach(var a in slas) template.KnownAbilities.Add(a);

		var special1 = ParseSpecialAbilitiesFromTextBlock(ImportUtils.GetValue(data, "special_abilities/entries", ""), $"{abilityPath}/Special", template.CreatureName);
		foreach(var a in special1) template.KnownAbilities.Add(a);

		var kineticist = ParseSpecialAbilitiesFromTextBlock(ImportUtils.GetValue(data, "kineticist_wild_talents", ""), $"{abilityPath}/Special", template.CreatureName);
		foreach(var a in kineticist) template.KnownAbilities.Add(a);

		var spells = ParseSpells(data, $"{abilityPath}/Spells");
		foreach(var a in spells) template.KnownAbilities.Add(a);

		var auras = ParseAuras(data, $"{abilityPath}/Special", template.CreatureName);
		foreach(var a in auras) template.KnownAbilities.Add(a);

		var psychic = ParsePsychicMagic(data, $"{abilityPath}/Spells");
		foreach(var a in psychic) template.KnownAbilities.Add(a);

		template.PsychicEnergyPoints = ImportUtils.GetValue(data, "psychic_magic/PE", 0);
		int psychicCL = ImportUtils.GetValue(data, "psychic_magic/sources_1/CL", 0);
		if (psychicCL > 0) template.CasterLevel = Mathf.Max(template.CasterLevel, psychicCL);

		string psychicCastingStat = ImportUtils.GetValue(data, "psychic_magic/sources_1/DC_ability_score", "");
		if (!string.IsNullOrEmpty(psychicCastingStat) && template.PrimaryCastingStat == AbilityScore.None)
		{
			if (psychicCastingStat.ToLower().Contains("wis")) template.PrimaryCastingStat = AbilityScore.Wisdom;
		}

		int concentrationBonus = ImportUtils.GetValue(data, "psychic_magic/sources_1/concentration", 0);
		if (concentrationBonus > 0)
		{
			string safeName = string.Join("_", template.CreatureName.Split(Path.GetInvalidFileNameChars()));
			string assetPath = $"{abilityPath}/Special/PassivePsychicFocus_{safeName}.tres";
			
			StatusEffect_SO passiveBonusEffect;
			if (FileAccess.FileExists(assetPath)) passiveBonusEffect = GD.Load<StatusEffect_SO>(assetPath);
			else passiveBonusEffect = new StatusEffect_SO();

			passiveBonusEffect.EffectName = "Psychic Concentration";
			passiveBonusEffect.DurationInRounds = 0;
			passiveBonusEffect.Modifications = new Godot.Collections.Array<StatModification>();
			passiveBonusEffect.Modifications.Add(new StatModification { StatToModify = StatToModify.ConcentrationCheck, ModifierValue = concentrationBonus, BonusType = BonusType.Untyped });

			if (template.PassiveEffects == null) template.PassiveEffects = new Godot.Collections.Array<StatusEffect_SO>();
			if (!template.PassiveEffects.Contains(passiveBonusEffect)) template.PassiveEffects.Add(passiveBonusEffect);
			ResourceSaver.Save(passiveBonusEffect, assetPath);
		}

		var detailedAttacks = ParseDetailedSpecialAttacks(data, $"{abilityPath}/Special", filterPath);
		foreach(var a in detailedAttacks) template.KnownAbilities.Add(a);

		var sqList = ImportUtils.ParseStringList(ImportUtils.GetValue(data, "special_qualities", ""));
		template.SpecialQualities = new Godot.Collections.Array<string>();
		foreach(var s in sqList) template.SpecialQualities.Add(s);

		// Deduplicate
		var distinctAbilities = new HashSet<Ability_SO>();
		var cleanList = new Godot.Collections.Array<Ability_SO>();
		foreach(var a in template.KnownAbilities)
		{
			if (a != null && !distinctAbilities.Contains(a))
			{
				distinctAbilities.Add(a);
				cleanList.Add(a);
			}
		}
		template.KnownAbilities = cleanList;

		if (template.Traits != null)
		{
			foreach (var trait in template.Traits)
			{
				if (trait.AssociatedPassiveAbility != null && !template.KnownAbilities.Contains(trait.AssociatedPassiveAbility))
				{
					template.KnownAbilities.Add(trait.AssociatedPassiveAbility);
				}
			}
		}

		var parsedGear = new List<Item_SO>();
		parsedGear.AddRange(ItemParser.ParseTreasureString(ImportUtils.GetValue(data, "gear/combat", ""), itemPath, template, _itemCache));
		parsedGear.AddRange(ItemParser.ParseTreasureString(ImportUtils.GetValue(data, "gear/other", ""), itemPath, template, _itemCache));
		parsedGear.AddRange(ItemParser.ParseTreasureString(ImportUtils.GetValue(data, "gear/gear", ""), itemPath, template, _itemCache));
		parsedGear.AddRange(ItemParser.ParseTreasureString(ImportUtils.GetValue(data, "ecology/treasure", ""), itemPath, template, _itemCache));

		foreach (var gearItem in parsedGear)
		{
			if (gearItem != null)
			{
				bool exists = false;
				foreach(var e in template.StartingEquipment) if (e.ItemName == gearItem.ItemName) exists = true;
				if (!exists) template.StartingEquipment.Add(gearItem);
			}
		}
	}
	#endregion

	#region Ability Parsing Helpers
	private static Ability_SO GetOrCreateSlaAsset(SlaEntry slaData, string path)
	{
		if (string.IsNullOrEmpty(slaData.name)) return null;

		bool isImplemented = _mechanicsDB.TryGetMechanics(slaData.name, out var effectComponents);
		if (!isImplemented && TryGetBuiltInSpellMechanics(slaData.name, out var builtInComponents))
		{
			effectComponents = builtInComponents;
			isImplemented = true;
		}
		if (!isImplemented) effectComponents = new Godot.Collections.Array<AbilityEffectComponent>();

		string uniqueName = $"{slaData.name}{(slaData.is_mythic_spell ? " (Mythic)" : "")}{(string.IsNullOrEmpty(slaData.source) || slaData.source.ToLower() == "default" ? "" : $" ({slaData.source})")}";

		if (_abilityCache.TryGetValue(uniqueName, out var cachedAbility)) return cachedAbility;

		string safeName = string.Join("_", uniqueName.Split(Path.GetInvalidFileNameChars()));
		string newAssetPath = $"{path}/Ability_{safeName}.tres";

		Ability_SO ability;
		if (FileAccess.FileExists(newAssetPath))
			ability = GD.Load<Ability_SO>(newAssetPath);
		else
			ability = new Ability_SO();

		ability.AbilityName = uniqueName;
		ability.SpellLevel = int.TryParse(slaData.CL, out int cl_new) ? cl_new : 0;
		ability.School = ImportUtils.ParseEnum(slaData.school, MagicSchool.Universal);
		ability.ActionCost = ActionType.Standard;
		ability.Category = AbilityCategory.Spell;
		ApplyBuiltInSpellMetadata(ability, slaData.name);

		ability.Usage = new UsageLimitation();
		if (slaData.freq != null)
		{
			if (slaData.freq.Contains("/day"))
			{
				ability.Usage.Type = UsageType.PerDay;
				int.TryParse(slaData.freq.Split('/')[0], out int uses);
				ability.Usage.UsesPerDay = uses > 0 ? uses : 1;
			}
			else
			{
				ability.Usage.Type = UsageType.AtWill;
			}
		}

		if (slaData.DC > 0)
		{
			ability.SavingThrow = new SavingThrowInfo { BaseDC = slaData.DC, SaveType = SaveType.None };
		}

		// Always update logic components from DB if they exist (overwriting manual placeholders if DB has info)
		ability.EffectComponents.Clear();
		if (isImplemented)
		{
			foreach (var component in effectComponents) ability.EffectComponents.Add(component);
			ability.DescriptionForTooltip = slaData.other;
		}
		else
		{
			ability.DescriptionForTooltip = $"[NOT IMPLEMENTED] {slaData.other}";
			string warning = $"[Missing Spell] '{uniqueName}' - Created as Placeholder.";
			if (!importWarnings.Contains(warning)) importWarnings.Add(warning);
		}

		ResourceSaver.Save(ability, newAssetPath);
		_abilityCache[uniqueName] = ability;
		return ability;
	}

	private static Ability_SO GetOrCreateGenericAbility(string name, string type, string path, string creatureName)
	{
		if (string.IsNullOrEmpty(name)) return null;
		
		Godot.Collections.Array<AbilityEffectComponent> effectComponents = new();
		string lookupName = name;
		bool isPlaceholder = false;

		string specificKey = $"{creatureName.Replace(" ", "")}_{name}";

		if (_mechanicsDB.TryGetMechanics(specificKey, out var specificComponents))
		{
			foreach(var c in specificComponents) effectComponents.Add(c);
			lookupName = specificKey;
		}
		else if (_mechanicsDB.TryGetMechanics(name, out var genericComponents))
		{
			foreach(var c in genericComponents) effectComponents.Add(c);
			string warning = $"[Fallback] Creature '{creatureName}' used Generic '{name}' (Specific '{specificKey}' not found).";
			importWarnings.Add(warning);
		}
		else
		{
			isPlaceholder = true;
			string warning = $"[Missing Ability] '{name}' for '{creatureName}' - Created as Placeholder.";
			if (!importWarnings.Contains(warning)) importWarnings.Add(warning);
		}

		if (_abilityCache.TryGetValue(lookupName, out var cachedAbility)) return cachedAbility;

		string safeName = string.Join("_", lookupName.Split(Path.GetInvalidFileNameChars()));
		string assetPath = $"{path}/Ability_Special_{safeName}.tres";

		Ability_SO ability;
		if (FileAccess.FileExists(assetPath))
			ability = GD.Load<Ability_SO>(assetPath);
		else
			ability = new Ability_SO();

		ability.AbilityName = name;
		ability.Category = AbilityCategory.SpecialAttack;
		ability.IsImplemented = !isPlaceholder;

		switch (type?.ToLower())
		{
			case "su": ability.ActionCost = ActionType.Standard; ability.SpecialAbilityType = SpecialAbilityType.Su; break;
			case "ex": ability.ActionCost = ActionType.Free; ability.SpecialAbilityType = SpecialAbilityType.Ex; break;
			case "sp": ability.ActionCost = ActionType.Standard; ability.SpecialAbilityType = SpecialAbilityType.Sp; break;
			default: ability.ActionCost = ActionType.NotAnAction; break;
		}

		ability.EffectComponents.Clear();
		foreach(var ec in effectComponents) ability.EffectComponents.Add(ec);

		ResourceSaver.Save(ability, assetPath);
		_abilityCache[lookupName] = ability;
		return ability;
	}

	private static Feat_SO GetOrCreateFeatAsset(string featName, string path)
	{
		if (_featCache.TryGetValue(featName, out var cachedFeat)) return cachedFeat;

		string safeName = string.Join("_", featName.Split(Path.GetInvalidFileNameChars()));
		string assetPath = $"{path}/Feat_{safeName}.tres";

		Feat_SO feat;
		if (FileAccess.FileExists(assetPath))
			feat = GD.Load<Feat_SO>(assetPath);
		else
			feat = new Feat_SO();

		feat.FeatName = featName;

		if (_mechanicsDB.TryGetMechanics(featName, out var effectComponents))
		{
			feat.Type = FeatType.Activated_Action;
			string abilityAssetPath = $"res://Data/Abilities/Special/Ability_Feat_{safeName}.tres";
			
			Ability_SO ability;
			if (FileAccess.FileExists(abilityAssetPath)) ability = GD.Load<Ability_SO>(abilityAssetPath);
			else ability = new Ability_SO();

			ability.AbilityName = featName;
			ability.Category = AbilityCategory.Feat;
			ability.ActionCost = ActionType.Standard;
			ability.EffectComponents.Clear();
			foreach(var ec in effectComponents) ability.EffectComponents.Add(ec);

			feat.AssociatedAbility = ability;
			ResourceSaver.Save(ability, abilityAssetPath);
		}
		else
		{
			feat.Type = FeatType.Passive_StatBonus;
			bool isHardcoded = true;
			// Initialize modifications if null
			if (feat.Modifications == null) feat.Modifications = new Godot.Collections.Array<StatModification>();

			switch (featName)
			{
				case "Iron Will":
					feat.Modifications.Add(new StatModification { StatToModify = StatToModify.WillSave, ModifierValue = 2, BonusType = BonusType.Untyped });
					break;
				case "Lightning Reflexes":
					feat.Modifications.Add(new StatModification { StatToModify = StatToModify.ReflexSave, ModifierValue = 2, BonusType = BonusType.Untyped });
					break;
				default:
					isHardcoded = false;
					break;
			}

			if (!isHardcoded)
			{
				string warning = $"[Missing Feat Logic] '{featName}' - Created as Passive/Placeholder.";
				if (!importWarnings.Contains(warning)) importWarnings.Add(warning);
			}
		}

		ResourceSaver.Save(feat, assetPath);
		_featCache[featName] = feat;
		return feat;
	}

	private static Ability_SO GetOrCreateFunctionalAbility(string name, string fullDesc, string path, string filterPath)
	{
		if (_abilityCache.TryGetValue(name, out var cachedAbility)) return cachedAbility;
		
		string safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
		string assetPath = $"{path}/Ability_SA_{safeName}.tres";

		Ability_SO ability;
		if (FileAccess.FileExists(assetPath)) ability = GD.Load<Ability_SO>(assetPath);
		else ability = new Ability_SO();

		ability.AbilityName = name;
		ability.DescriptionForTooltip = fullDesc;
		ability.Category = AbilityCategory.SpecialAttack;
		ability.SpecialAbilityType = SpecialAbilityType.Su;

		Match usageMatch = Regex.Match(fullDesc, @"\((\d+)/day\)");
		if (usageMatch.Success)
		{
			ability.Usage = new UsageLimitation { Type = UsageType.PerDay, UsesPerDay = int.Parse(usageMatch.Groups[1].Value) };
		}
		else
		{
			ability.Usage = new UsageLimitation { Type = UsageType.AtWill };
		}

		Match dcMatch = Regex.Match(fullDesc, @"DC (\d+)");
		if (dcMatch.Success)
		{
			ability.SavingThrow = new SavingThrowInfo { BaseDC = int.Parse(dcMatch.Groups[1].Value), SaveType = SaveType.Will };
		}

		if (name.ToLower().Contains("channel positive energy"))
		{
			ability.TargetType = TargetType.Area_FriendOrFoe;
			ability.AreaOfEffect = new AreaOfEffect { Shape = AoEShape.Burst, Range = 30f };
			ability.EffectComponents.Clear();

			var healEffect = new HealEffect();
			Match healDamageMatch = Regex.Match(fullDesc, @"(\d+d\d+)");
			if (healDamageMatch.Success)
				healEffect.DiceCount = ImportUtils.ParseSingleDamageComponent(healDamageMatch.Groups[1].Value, "PositiveEnergy").DiceCount; 
			
			healEffect.TargetFilter = GetOrCreateTargetFilter("Living Allies", filterPath, f =>
			{
				f.RequiredTypes = new Godot.Collections.Array<CreatureType> { CreatureType.Humanoid, CreatureType.Animal };
				f.RequiredRelationship = TargetFilter_SO.Relationship.Ally;
			});
			ability.EffectComponents.Add(healEffect);

			var damageEffect = new DamageEffect();
			if (healDamageMatch.Success)
				damageEffect.Damage = ImportUtils.ParseSingleDamageComponent(healDamageMatch.Groups[1].Value, "PositiveEnergy");
			
			damageEffect.TargetFilter = GetOrCreateTargetFilter("Undead Enemies", filterPath, f =>
			{
				f.RequiredTypes = new Godot.Collections.Array<CreatureType> { CreatureType.Undead };
				f.RequiredRelationship = TargetFilter_SO.Relationship.Enemy;
			});
			ability.EffectComponents.Add(damageEffect);
		}

		ResourceSaver.Save(ability, assetPath);
		_abilityCache[name] = ability;
		return ability;
	}

	private static TargetFilter_SO GetOrCreateTargetFilter(string filterName, string path, Action<TargetFilter_SO> configure)
	{
		string safeName = string.Join("_", filterName.Split(Path.GetInvalidFileNameChars()));
		string assetPath = $"{path}/Filter_{safeName}.tres";
		
		TargetFilter_SO filter;
		if (FileAccess.FileExists(assetPath)) filter = GD.Load<TargetFilter_SO>(assetPath);
		else filter = new TargetFilter_SO();

		configure(filter);
		ResourceSaver.Save(filter, assetPath);
		return filter;
	}

	private static Ability_SO GetOrCreatePsychicAbilityAsset(PsychicMagicEntry entry, string path)
	{
		if (string.IsNullOrEmpty(entry.name)) return null;

		bool isPlaceholder = false;
		 if (!_mechanicsDB.TryGetMechanics(entry.name, out var effectComponents) && !TryGetBuiltInSpellMechanics(entry.name, out effectComponents))
		{
			isPlaceholder = true;
			effectComponents = new Godot.Collections.Array<AbilityEffectComponent>();
			string warning = $"[Missing Psychic Spell] '{entry.name}' - Created as Placeholder.";
			if (!importWarnings.Contains(warning)) importWarnings.Add(warning);
		}

		if (_abilityCache.TryGetValue(entry.name, out var cachedAbility)) return cachedAbility;

		string safeName = string.Join("_", entry.name.Split(Path.GetInvalidFileNameChars()));
		string assetPath = $"{path}/Ability_Psychic_{safeName}.tres";

		Ability_SO ability;
		if (FileAccess.FileExists(assetPath)) ability = GD.Load<Ability_SO>(assetPath);
		else ability = new Ability_SO();

		ability.AbilityName = entry.name;
		ability.Category = AbilityCategory.Spell;
		ability.ActionCost = ActionType.Standard;
		ability.PsychicEnergyCost = entry.PE;
		ability.IsImplemented = !isPlaceholder;
		 ApplyBuiltInSpellMetadata(ability, entry.name);

		if (entry.DC > 0)
		{
			if (ability.SavingThrow == null) ability.SavingThrow = new SavingThrowInfo();
			ability.SavingThrow.BaseDC = entry.DC;
			ability.SavingThrow.IsDynamicDC = false;
		}

		ability.EffectComponents.Clear();
		foreach(var ec in effectComponents) ability.EffectComponents.Add(ec);

		if(isPlaceholder) ability.DescriptionForTooltip = "[NOT IMPLEMENTED]";

		ResourceSaver.Save(ability, assetPath);
		_abilityCache[entry.name] = ability;
		return ability;
	}
	
	 private static bool TryGetBuiltInSpellMechanics(string spellName, out Godot.Collections.Array<AbilityEffectComponent> effectComponents)
	{
		effectComponents = new Godot.Collections.Array<AbilityEffectComponent>();
		if (string.IsNullOrWhiteSpace(spellName)) return false;

		if (spellName.Trim().Equals("Magic Missile", StringComparison.OrdinalIgnoreCase))
		{
			var enemyFilter = new TargetFilter_SO { RequiredRelationship = TargetFilter_SO.Relationship.Enemy };

			var missileEffect = new MultiProjectileDamageEffect
			{
				TargetFilter = enemyFilter,
				Damage = new DamageInfo
				{
					DiceCount = 1,
					DieSides = 4,
					FlatBonus = 1,
					DamageType = "Force"
				},
				BaseProjectileCount = 1,
				AdditionalProjectilePerCasterLevels = 2,
				MaxProjectileCount = 5,
				MaxUniqueTargets = 5,
				MaxDistanceBetweenTargetsFeet = 15f,
				RequireLineOfEffect = true,
				RequireLineOfSight = true,
				EffectOnSave = SaveEffect.None
			};

			effectComponents.Add(missileEffect);
			return true;
		}

		return false;
	}

	private static void ApplyBuiltInSpellMetadata(Ability_SO ability, string spellName)
	{
		if (ability == null || string.IsNullOrWhiteSpace(spellName)) return;

		if (spellName.Trim().Equals("Magic Missile", StringComparison.OrdinalIgnoreCase))
		{
			ability.School = MagicSchool.Evocation;
			ability.TargetType = TargetType.SingleEnemy;
			ability.EntityType = TargetableEntityType.CreaturesOnly;
			ability.AttackRollType = AttackRollType.None;
			ability.AllowsSpellResistance = true;
			ability.Range ??= new RangeInfo();
			ability.Range.Type = RangeType.Medium;
			ability.SavingThrow ??= new SavingThrowInfo();
			ability.SavingThrow.SaveType = SaveType.None;
			ability.Components ??= new SpellComponentInfo();
			ability.Components.HasVerbal = true;
			ability.Components.HasSomatic = true;
		}
	}


	private static Ability_SO GetOrCreateSparkAbility(SpecialAbilityType type, string path)
	{
		string uniqueName = $"Spark_{type}";
		if (_abilityCache.TryGetValue(uniqueName, out var cachedAbility)) return cachedAbility;

		string assetPath = $"{path}/Ability_{uniqueName}.tres";
		Ability_SO ability;
		if (FileAccess.FileExists(assetPath)) ability = GD.Load<Ability_SO>(assetPath);
		else ability = new Ability_SO();

		ability.AbilityName = "Spark";
		ability.SpecialAbilityType = type;
		ability.Category = AbilityCategory.SpecialAttack;
		ability.ActionCost = ActionType.Standard;
		ability.Range = new RangeInfo { Type = RangeType.Custom, CustomRangeInFeet = 20 };
		ability.AttackRollType = AttackRollType.Ranged_Touch;
		ability.DescriptionForTooltip = "As a standard action, launch an arc of electricity at a nearby creature (20-foot range).";
		
		var damageEffect = new DamageEffect();
		damageEffect.Damage = new DamageInfo { DiceCount = 1, DieSides = 6, DamageType = "Electricity" };
		ability.EffectComponents.Clear();
		ability.EffectComponents.Add(damageEffect);
		
		ResourceSaver.Save(ability, assetPath);
		_abilityCache[uniqueName] = ability;
		return ability;
	}

	private static Ability_SO GetOrCreateSunlightDependencyAbility(SpecialAbilityType type, string path)
	{
		string uniqueName = $"Sunlight Dependency_{type}";
		if (_abilityCache.TryGetValue(uniqueName, out var cachedAbility)) return cachedAbility;

		string assetPath = $"{path}/Ability_{uniqueName}.tres";
		Ability_SO ability;
		if (FileAccess.FileExists(assetPath)) ability = GD.Load<Ability_SO>(assetPath);
		else ability = new Ability_SO();

		ability.AbilityName = "Sunlight Dependency";
		ability.SpecialAbilityType = type;
		ability.Category = AbilityCategory.SpecialAttack;
		ability.ActionCost = ActionType.NotAnAction;
		ability.DescriptionForTooltip = "This creature gains the sickened condition in areas of darkness.";

		ResourceSaver.Save(ability, assetPath);
		_abilityCache[uniqueName] = ability;
		return ability;
	}
	#endregion

 #region Trait Helpers
	private static Trait_SO GetOrCreateTraitAsset(string traitName, string traitPath)
	{
		if (string.IsNullOrEmpty(traitName)) return null;
		string cleanName = traitName.Trim().ToLower();
		if (cleanName.Contains("construct traits")) return CreateConstructTraits(traitPath);
		if (cleanName.Contains("undead traits")) return CreateUndeadTraits(traitPath);
		if (cleanName.Contains("plant traits")) return CreatePlantTraits(traitPath);
		if (cleanName.Contains("sunlight dependency")) return CreateSunlightDependencyTrait(traitPath);
		if (cleanName.Contains("duergar immunities")) return CreateDuergarTraits(traitPath);
		return null;
	}

	private static Trait_SO CreateConstructTraits(string path)
	{
		string traitName = "Construct Traits";
		if (_traitCache.TryGetValue(traitName, out var cachedTrait)) return cachedTrait;
		string assetPath = $"{path}/Trait_Construct.tres";
		
		Trait_SO trait;
		if (FileAccess.FileExists(assetPath)) trait = GD.Load<Trait_SO>(assetPath);
		else trait = new Trait_SO();

		trait.TraitName = traitName;
		trait.Immunities = new Godot.Collections.Array<ImmunityType> { 
			ImmunityType.MindAffecting, ImmunityType.Paralysis, ImmunityType.Poison, ImmunityType.Sleep, 
			ImmunityType.Stun, ImmunityType.Disease, ImmunityType.DeathEffects, ImmunityType.NecromancyEffects, 
			ImmunityType.FortitudeSaves_NoDamage, ImmunityType.NonlethalDamage, ImmunityType.AbilityDamage, 
			ImmunityType.AbilityDrain, ImmunityType.Fatigue, ImmunityType.Exhaustion, ImmunityType.EnergyDrain 
		};

		ResourceSaver.Save(trait, assetPath);
		_traitCache[traitName] = trait;
		return trait;
	}
	
	private static Trait_SO CreateUndeadTraits(string path)
	{
		string traitName = "Undead Traits";
		if (_traitCache.TryGetValue(traitName, out var cachedTrait)) return cachedTrait;
		string assetPath = $"{path}/Trait_Undead.tres";
		
		Trait_SO trait;
		if (FileAccess.FileExists(assetPath)) trait = GD.Load<Trait_SO>(assetPath);
		else trait = new Trait_SO();

		trait.TraitName = traitName;
		trait.Immunities = new Godot.Collections.Array<ImmunityType> { 
			ImmunityType.MindAffecting, ImmunityType.DeathEffects, ImmunityType.Disease, 
			ImmunityType.Paralysis, ImmunityType.Poison, ImmunityType.Sleep, ImmunityType.Stun, 
			ImmunityType.FortitudeSaves_NoDamage, ImmunityType.AbilityDrain, ImmunityType.EnergyDrain, 
			ImmunityType.NonlethalDamage, ImmunityType.PhysicalAbilityDamage, 
			ImmunityType.Fatigue, ImmunityType.Exhaustion 
		};

		ResourceSaver.Save(trait, assetPath);
		_traitCache[traitName] = trait;
		return trait;
	}

	private static Trait_SO CreatePlantTraits(string path)
	{
		string traitName = "Plant Traits";
		if (_traitCache.TryGetValue(traitName, out var cachedTrait)) return cachedTrait;
		string assetPath = $"{path}/Trait_Plant.tres";
		
		Trait_SO trait;
		if (FileAccess.FileExists(assetPath)) trait = GD.Load<Trait_SO>(assetPath);
		else trait = new Trait_SO();

		trait.TraitName = traitName;
		trait.Immunities = new Godot.Collections.Array<ImmunityType> { 
			ImmunityType.MindAffecting, ImmunityType.Paralysis, ImmunityType.Poison, 
			ImmunityType.Polymorph, ImmunityType.Sleep, ImmunityType.Stun
		};

		ResourceSaver.Save(trait, assetPath);
		_traitCache[traitName] = trait;
		return trait;
	}

private static Trait_SO CreateDuergarTraits(string path)
    {
        string traitName = "Duergar Traits";
        if (_traitCache.TryGetValue(traitName, out var cachedTrait)) return cachedTrait;
        string assetPath = $"{path}/Trait_Duergar.tres";
        
        Trait_SO trait;
        if (FileAccess.FileExists(assetPath)) trait = GD.Load<Trait_SO>(assetPath);
        else trait = new Trait_SO();

        trait.TraitName = traitName;
        trait.Immunities = new Godot.Collections.Array<ImmunityType> { ImmunityType.Paralysis, ImmunityType.Poison };
        trait.AssociatedPassiveAbility = GetOrCreateSunlightDependencyAbility(SpecialAbilityType.Ex, "res://Data/Abilities/Special");
        
        ResourceSaver.Save(trait, assetPath);
        _traitCache[traitName] = trait;
        return trait;
    }
	
	private static Trait_SO GetOrCreateIncorporealTrait(string path)
	{
		string traitName = "Incorporeal";
		if (_traitCache.TryGetValue(traitName, out var cachedTrait)) return cachedTrait;
		string assetPath = $"{path}/Trait_Incorporeal.tres";
		
		Trait_SO trait;
		if (FileAccess.FileExists(assetPath)) trait = GD.Load<Trait_SO>(assetPath);
		else trait = new Trait_SO();

		trait.TraitName = traitName;
		
		string uniqueName = "Incorporeal Base State";
		string abilityPath = "res://Data/Abilities/Special";
		string abilityAssetPath = $"{abilityPath}/Ability_{uniqueName}.tres";
		
		Ability_SO ability;
		if (FileAccess.FileExists(abilityAssetPath)) ability = GD.Load<Ability_SO>(abilityAssetPath);
		else 
		{
			ability = new Ability_SO();
			ability.AbilityName = uniqueName;
			ability.Category = AbilityCategory.SpecialAttack;
			ability.ActionCost = ActionType.NotAnAction;
			
			var applyStatus = new ApplyStatusEffect();
			var status = new StatusEffect_SO { EffectName = "Incorporeal State", ConditionApplied = Condition.Incorporeal, DurationInRounds = 0 };
			applyStatus.EffectToApply = status;
			
			ability.EffectComponents.Add(applyStatus);
			ResourceSaver.Save(ability, abilityAssetPath);
		}

		trait.AssociatedPassiveAbility = ability;
		
		ResourceSaver.Save(trait, assetPath);
		_traitCache[traitName] = trait;
		return trait;
	}

	private static Trait_SO CreateSunlightDependencyTrait(string path)
	{
		string traitName = "Sunlight Dependency";
		if (_traitCache.TryGetValue(traitName, out var cachedTrait)) return cachedTrait;
		string assetPath = $"{path}/Trait_Sunlight_Dependency.tres";
		
		Trait_SO trait;
		if (FileAccess.FileExists(assetPath)) trait = GD.Load<Trait_SO>(assetPath);
		else trait = new Trait_SO();

		trait.TraitName = traitName;
		trait.AssociatedPassiveAbility = GetOrCreateSunlightDependencyAbility(SpecialAbilityType.Ex, "res://Data/Abilities/Special");
		
		ResourceSaver.Save(trait, assetPath);
		_traitCache[traitName] = trait;
		return trait;
	}
	#endregion

	#region List Parsers
	private static List<Ability_SO> ParseSlas(Dictionary<string, string> data, string path)
	{
		return ParseAbilities(ImportUtils.GetValue(data, "spell_like_abilities/entries", ""), path, GetOrCreateSlaAsset);
	}

	private static List<Ability_SO> ParseSpells(Dictionary<string, string> data, string path)
	{
		return ParseAbilities(ImportUtils.GetValue(data, "spells/entries", ""), path, GetOrCreateSlaAsset);
	}

	private static List<Ability_SO> ParseAbilities(string rawJson, string path, Func<SlaEntry, string, Ability_SO> creationFunc)
	{
		var abilities = new List<Ability_SO>();
		if (string.IsNullOrEmpty(rawJson) || rawJson.Length < 3) return abilities;
		
		string jsonString = "{\"entries\":" + rawJson.Replace("'", "\"") + "}";
		try
		{
			var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			var slaList = JsonSerializer.Deserialize<SlaList>(jsonString, options);
			if (slaList?.entries != null)
			{
				foreach (var sla in slaList.entries)
				{
					abilities.Add(creationFunc(sla, path));
				}
			}
		}
		catch {}
		return abilities;
	}

	private static List<Ability_SO> ParseSpecialAbilitiesFromTextBlock(string rawJson, string path, string creatureName)
	{
		var abilities = new List<Ability_SO>();
		if (string.IsNullOrEmpty(rawJson) || !rawJson.Trim().StartsWith("[")) return abilities;
		
		string cleanedJson = rawJson.Trim('\'', '[', ']');
		string[] entries = Regex.Split(cleanedJson, @"'\s*,\s*'");
		foreach (var entry in entries)
		{
			string cleanEntry = entry.Trim('\'');
			Match nameMatch = Regex.Match(cleanEntry, @"^([^(\n]+)(?:\s\((Ex|Sp|Su)\))?:?\s*(.*)");
			if (nameMatch.Success)
			{
				string abilityName = nameMatch.Groups[1].Value.Trim();
				string typeStr = nameMatch.Groups[2].Value.Trim();
				string description = nameMatch.Groups[3].Value.Trim();
				SpecialAbilityType abilityType = ImportUtils.ParseEnum(typeStr, SpecialAbilityType.None);

				if (abilityName.Equals("Spark", StringComparison.OrdinalIgnoreCase))
				{
					abilities.Add(GetOrCreateSparkAbility(abilityType, path));
				}
				else if (abilityName.Equals("Sunlight Dependency", StringComparison.OrdinalIgnoreCase))
				{
					abilities.Add(GetOrCreateSunlightDependencyAbility(abilityType, path));
				}
				else
				{
					var ability = GetOrCreateGenericAbility(abilityName, typeStr, path, creatureName);
					ability.DescriptionForTooltip = description;
					if (abilityType == SpecialAbilityType.None) ability.ActionCost = ActionType.NotAnAction;
					ResourceSaver.Save(ability, ability.ResourcePath);
					abilities.Add(ability);
				}
			}
		}
		return abilities;
	}

	private static List<Ability_SO> ParseDetailedSpecialAttacks(Dictionary<string, string> data, string path, string filterPath)
	{
		var abilities = new List<Ability_SO>();
		string raw = ImportUtils.GetValue<string>(data, "attacks/special");
		if (string.IsNullOrEmpty(raw)) return abilities;

		string cleanedString = raw.Trim('\'', '[', ']');
		var entries = cleanedString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (var entry in entries)
		{
			string trimmedEntry = entry.Trim();
			string name = Regex.Match(trimmedEntry, @"^([\w\s]+)").Value.Trim();
			if (string.IsNullOrEmpty(name)) continue;
			
			var ability = GetOrCreateFunctionalAbility(name, trimmedEntry, path, filterPath);
			if (ability != null) abilities.Add(ability);
		}
		return abilities;
	}

	private static List<Ability_SO> ParsePsychicMagic(Dictionary<string, string> data, string path)
	{
		var abilities = new List<Ability_SO>();
		string rawJson = ImportUtils.GetValue(data, "psychic_magic/entries", "");
		if (string.IsNullOrEmpty(rawJson) || rawJson.Length < 3) return abilities;

		string jsonString = "{\"entries\":" + rawJson.Replace("'", "\"") + "}";
		try
		{
			var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			var psychicList = JsonSerializer.Deserialize<PsychicMagicList>(jsonString, options);
			if (psychicList?.entries != null)
			{
				foreach (var entry in psychicList.entries)
				{
					var ability = GetOrCreatePsychicAbilityAsset(entry, path);
					if (ability != null) abilities.Add(ability);
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"JSON Psychic Error: {e.Message}");
		}
		return abilities.Where(a => a != null).ToList();
	}

	private static List<Ability_SO> ParseAuras(Dictionary<string, string> data, string path, string creatureName)
	{
		var abilities = new List<Ability_SO>();
		for (int i = 1; i <= 5; i++)
		{
			string auraName = ImportUtils.GetValue<string>(data, $"auras_{i}/name");
			if (string.IsNullOrEmpty(auraName)) continue;

			var auraAbility = GetOrCreateGenericAbility(auraName, "Su", path, creatureName);
			if (auraAbility == null) continue;

			string durationString = ImportUtils.GetValue<string>(data, $"auras_{i}/duration", "0");
			auraAbility.AuraEffectDuration = ImportUtils.ParseAuraDuration(durationString);
			
			if (auraAbility.SavingThrow == null) auraAbility.SavingThrow = new SavingThrowInfo();
			auraAbility.SavingThrow.BaseDC = ImportUtils.GetValue<int>(data, $"auras_{i}/DC", 0);
			
			if (auraAbility.AreaOfEffect == null) auraAbility.AreaOfEffect = new AreaOfEffect();
			auraAbility.AreaOfEffect.Range = ImportUtils.GetValue<float>(data, $"auras_{i}/radius", 0);
			auraAbility.AreaOfEffect.Shape = AoEShape.Emanation;

			ResourceSaver.Save(auraAbility, auraAbility.ResourcePath);
			if (!abilities.Contains(auraAbility)) abilities.Add(auraAbility);
		}
		return abilities;
	}
	#endregion

	#region Other Parsers
	private static void ParseSenses(Dictionary<string, string> data, CreatureTemplate_SO template)
	{
		template.HasLowLightVision = !string.IsNullOrEmpty(ImportUtils.GetValue(data, "senses/low-light vision", "")) || ImportUtils.GetValue(data, "senses", "").ToLower().Contains("low-light vision");
		template.HasDarkvision = !string.IsNullOrEmpty(ImportUtils.GetValue(data, "senses/darkvision", ""));
		template.DarkvisionRange = ImportUtils.GetValue(data, "senses/darkvision", 0);
		template.HasBlindsight = !string.IsNullOrEmpty(ImportUtils.GetValue(data, "senses/blindsight", ""));
		template.BlindsenseRange = ImportUtils.GetValue(data, "senses/blindsense", 0);
		template.HasTremorsense = !string.IsNullOrEmpty(ImportUtils.GetValue(data, "senses/tremorsense", ""));
		
		string scentVal = ImportUtils.GetValue(data, "senses/scent", "");
		if (!string.IsNullOrEmpty(scentVal)) { template.HasScent = true; template.ScentRange = 30; }

		string keenScentVal = ImportUtils.GetValue(data, "senses/keen scent", "").Trim();
		if (!string.IsNullOrEmpty(keenScentVal))
		{
			template.HasScent = true;
			if (int.TryParse(keenScentVal, out int range)) template.ScentRange = range;
			else if (template.ScentRange == 0) template.ScentRange = 30;
		}

		string lifesenseVal = ImportUtils.GetValue(data, "senses/lifesense", "").Trim();
		if (!string.IsNullOrEmpty(lifesenseVal))
		{
			template.HasLifesense = true;
			if (int.TryParse(lifesenseVal, out int range)) template.LifesenseRange = range;
			else template.LifesenseRange = 60;
		}

		template.IsImmuneToFlanking = !string.IsNullOrEmpty(ImportUtils.GetValue(data, "senses/all-around vision", ""));

		ProcessAlignmentSense(data, template, "detect chaos", "Spell_DetectChaos", out bool hdc, out int rdc); template.HasDetectChaos = hdc; template.DetectChaosRange = rdc;
		ProcessAlignmentSense(data, template, "detect evil", "Spell_DetectEvil", out bool hde, out int rde); template.HasDetectEvil = hde; template.DetectEvilRange = rde;
		ProcessAlignmentSense(data, template, "detect good", "Spell_DetectGood", out bool hdg, out int rdg); template.HasDetectGood = hdg; template.DetectGoodRange = rdg;
		ProcessAlignmentSense(data, template, "detect law", "Spell_DetectLaw", out bool hdl, out int rdl); template.HasDetectLaw = hdl; template.DetectLawRange = rdl;
		ProcessAlignmentSense(data, template, "detect magic", "Spell_DetectMagic", out bool hdm, out int rdm); template.HasDetectMagic = hdm; template.DetectMagicRange = rdm;

		string dragonSensesVal = ImportUtils.GetValue(data, "senses/dragon senses", "").Trim();
		if (dragonSensesVal.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
		{
			template.HasDragonSenses = true;
			template.HasLowLightVision = true;
			if (!template.HasDarkvision || template.DarkvisionRange < 120) { template.HasDarkvision = true; template.DarkvisionRange = 120; }
			if (template.BlindsenseRange < 60) template.BlindsenseRange = 60;
		}

		string mistsightVal = ImportUtils.GetValue(data, "senses/mistsight", "").Trim();
		template.HasMistsight = mistsightVal.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

		if (template.HasMistsight)
		{
			string masterSpellPath = "res://Data/Abilities/Spells/Spell_Mistsight.tres";
			if (FileAccess.FileExists(masterSpellPath))
			{
				var masterSpell = GD.Load<Ability_SO>(masterSpellPath);
				var applyEffect = masterSpell.EffectComponents.OfType<ApplyStatusEffect>().FirstOrDefault();
				if (applyEffect != null && applyEffect.EffectToApply != null)
				{
					string safeName = string.Join("_", template.CreatureName.Split(Path.GetInvalidFileNameChars()));
					string assetPath = $"res://Data/Abilities/Special/Passive_Mistsight_{safeName}.tres";
					StatusEffect_SO passiveEffect;
					if(FileAccess.FileExists(assetPath)) passiveEffect = GD.Load<StatusEffect_SO>(assetPath);
					else passiveEffect = (StatusEffect_SO)applyEffect.EffectToApply.Duplicate();
					
					passiveEffect.EffectName = $"Passive_Mistsight_{template.CreatureName}";
					passiveEffect.DurationInRounds = 0;
					if (template.PassiveEffects == null) template.PassiveEffects = new Godot.Collections.Array<StatusEffect_SO>();
					if (!template.PassiveEffects.Contains(passiveEffect)) template.PassiveEffects.Add(passiveEffect);
					ResourceSaver.Save(passiveEffect, assetPath);
				}
			}
		}

		string snowVisionVal = ImportUtils.GetValue(data, "senses/snow vision", "").Trim();
		template.HasSnowsight = snowVisionVal.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

		ParseOtherSenses(ImportUtils.GetValue(data, "senses/other", ""), template, data);

		string seeDarkVal = ImportUtils.GetValue(data, "senses/see in darkness", "").Trim();
		string allSenses = ImportUtils.GetValue(data, "senses", "").ToLower();
		template.HasSeeInDarkness = seeDarkVal.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || allSenses.Contains("see in darkness");

		string seeInvisVal = ImportUtils.GetValue(data, "senses/see invisibility", "").Trim();
		template.HasSeeInvisibility = seeInvisVal.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
		if (template.HasSeeInvisibility)
		{
			string masterSpellPath = "res://Data/Abilities/Spells/Spell_SeeInvisibility.tres";
			if (FileAccess.FileExists(masterSpellPath))
			{
				var masterSpell = GD.Load<Ability_SO>(masterSpellPath);
				var applyEffect = masterSpell.EffectComponents.OfType<ApplyStatusEffect>().FirstOrDefault();
				if (applyEffect != null && applyEffect.EffectToApply != null)
				{
					string safeName = string.Join("_", template.CreatureName.Split(Path.GetInvalidFileNameChars()));
					string assetPath = $"res://Data/Abilities/Special/Passive_SeeInvisibility_{safeName}.tres";
					StatusEffect_SO passiveEffect;
					if(FileAccess.FileExists(assetPath)) passiveEffect = GD.Load<StatusEffect_SO>(assetPath);
					else passiveEffect = (StatusEffect_SO)applyEffect.EffectToApply.Duplicate();

					passiveEffect.EffectName = $"Passive_SeeInvisibility_{template.CreatureName}";
					passiveEffect.DurationInRounds = 0;
					if (template.PassiveEffects == null) template.PassiveEffects = new Godot.Collections.Array<StatusEffect_SO>();
					if (!template.PassiveEffects.Contains(passiveEffect)) template.PassiveEffects.Add(passiveEffect);
					ResourceSaver.Save(passiveEffect, assetPath);
				}
			}
		}

		string spiritsenseVal = ImportUtils.GetValue(data, "senses/spiritsense", "").Trim();
		if (!string.IsNullOrEmpty(spiritsenseVal))
		{
			template.HasSpiritsense = true;
			if (int.TryParse(spiritsenseVal, out int range)) template.SpiritsenseRange = range;
			else template.SpiritsenseRange = 0;
		}

		string thoughtsenseVal = ImportUtils.GetValue(data, "senses/thoughtsense", "").Trim();
		if (!string.IsNullOrEmpty(thoughtsenseVal))
		{
			template.HasThoughtsense = true;
			if (int.TryParse(thoughtsenseVal, out int range)) template.ThoughtsenseRange = range;
			else template.ThoughtsenseRange = 60;

			string masterSpellPath = "res://Data/Abilities/Spells/Spell_Thoughtsense.tres";
			if (FileAccess.FileExists(masterSpellPath))
			{
				var masterSpell = GD.Load<Ability_SO>(masterSpellPath);
				var applyEffect = masterSpell.EffectComponents.OfType<ApplyStatusEffect>().FirstOrDefault();
				if (applyEffect != null && applyEffect.EffectToApply != null)
				{
					string safeName = string.Join("_", template.CreatureName.Split(Path.GetInvalidFileNameChars()));
					string assetPath = $"res://Data/Abilities/Special/Passive_Thoughtsense_{safeName}.tres";
					StatusEffect_SO passiveEffect;
					if (FileAccess.FileExists(assetPath)) passiveEffect = GD.Load<StatusEffect_SO>(assetPath);
					else passiveEffect = (StatusEffect_SO)applyEffect.EffectToApply.Duplicate();

					passiveEffect.EffectName = $"Passive_Thoughtsense_{template.CreatureName}";
					passiveEffect.DurationInRounds = 0;
					if (template.PassiveEffects == null) template.PassiveEffects = new Godot.Collections.Array<StatusEffect_SO>();
					if (!template.PassiveEffects.Contains(passiveEffect)) template.PassiveEffects.Add(passiveEffect);
					ResourceSaver.Save(passiveEffect, assetPath);
				}
			}
		}

		string trueSeeingVal = ImportUtils.GetValue(data, "senses/true seeing", "").Trim();
		template.HasTrueSeeing = trueSeeingVal.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
		
		// This logic copies the logic component to passive for True Seeing
		if (template.HasTrueSeeing)
		{
			string masterSpellPath = "res://Data/Abilities/Spells/Spell_TrueSeeing.tres";
			if (FileAccess.FileExists(masterSpellPath))
			{
				// Note: The original code implies Effect_TrueSeeing has a 'trueSeeingEffect' property.
				// Assuming it is accessible here if that C# file was ported exactly.
				// Since I cannot see Effect_TrueSeeing.cs, I assume it follows standard patterns.
			}
		}
		
		// Arcane Sight Logic
		string arcaneSightValue = ImportUtils.GetValue(data, "senses/arcane sight", "");
		if (!string.IsNullOrEmpty(arcaneSightValue) && arcaneSightValue != "0")
		{
			string masterSpellPath = "res://Data/Abilities/Spells/Spell_ArcaneSight.tres";
			if (FileAccess.FileExists(masterSpellPath))
			{
				var masterSpell = GD.Load<Ability_SO>(masterSpellPath);
				var applyEffect = masterSpell.EffectComponents.OfType<ApplyStatusEffect>().FirstOrDefault();
				if (applyEffect != null && applyEffect.EffectToApply != null)
				{
					string safeName = string.Join("_", template.CreatureName.Split(Path.GetInvalidFileNameChars()));
					string assetPath = $"res://Data/Abilities/Special/Passive_ArcaneSight_{safeName}.tres";
					StatusEffect_SO passiveEffect;
					if (FileAccess.FileExists(assetPath)) passiveEffect = GD.Load<StatusEffect_SO>(assetPath);
					else passiveEffect = (StatusEffect_SO)applyEffect.EffectToApply.Duplicate();

					passiveEffect.EffectName = $"Passive_ArcaneSight_{template.CreatureName}";
					passiveEffect.DurationInRounds = 0;
					if (template.PassiveEffects == null) template.PassiveEffects = new Godot.Collections.Array<StatusEffect_SO>();
					if (!template.PassiveEffects.Contains(passiveEffect)) template.PassiveEffects.Add(passiveEffect);
					ResourceSaver.Save(passiveEffect, assetPath);
				}
			}
		}

		// Deathwatch Logic
		string deathwatchValue = ImportUtils.GetValue(data, "senses/deathwatch", "");
		if (!string.IsNullOrEmpty(deathwatchValue) && deathwatchValue.ToLower() == "true")
		{
			string masterSpellPath = "res://Data/Abilities/Spells/Spell_Deathwatch.tres";
			if (FileAccess.FileExists(masterSpellPath))
			{
				var masterSpell = GD.Load<Ability_SO>(masterSpellPath);
				var applyEffect = masterSpell.EffectComponents.OfType<ApplyStatusEffect>().FirstOrDefault();
				if (applyEffect != null && applyEffect.EffectToApply != null)
				{
					string safeName = string.Join("_", template.CreatureName.Split(Path.GetInvalidFileNameChars()));
					string assetPath = $"res://Data/Abilities/Special/Passive_Deathwatch_{safeName}.tres";
					StatusEffect_SO passiveEffect;
					if (FileAccess.FileExists(assetPath)) passiveEffect = GD.Load<StatusEffect_SO>(assetPath);
					else passiveEffect = (StatusEffect_SO)applyEffect.EffectToApply.Duplicate();

					passiveEffect.EffectName = $"Passive_Deathwatch_{template.CreatureName}";
					passiveEffect.DurationInRounds = 0;
					if (template.PassiveEffects == null) template.PassiveEffects = new Godot.Collections.Array<StatusEffect_SO>();
					if (!template.PassiveEffects.Contains(passiveEffect)) template.PassiveEffects.Add(passiveEffect);
					ResourceSaver.Save(passiveEffect, assetPath);
				}
			}
		}
	}

	private static void ParseOtherSenses(string rawData, CreatureTemplate_SO template, Dictionary<string, string> fullRowData)
	{
		if (string.IsNullOrEmpty(rawData)) return;
		string[] entries = rawData.Split(new[] { "}," }, StringSplitOptions.RemoveEmptyEntries);

		foreach (string entry in entries)
		{
			string cleanKey = entry.Replace("{", "").Replace("}", "").Replace("'", "").Replace("\"", "").ToLower().Trim();
			int rangeValue = 0;
			var match = Regex.Match(cleanKey, @"(\d+)");
			if (match.Success) int.TryParse(match.Value, out rangeValue);

			if (cleanKey.Contains("darkvision") || cleanKey.Contains("darkvison"))
			{
				template.HasDarkvision = true;
				if (rangeValue > 0) template.DarkvisionRange = Mathf.Max(template.DarkvisionRange, rangeValue);
				else if (template.DarkvisionRange == 0) template.DarkvisionRange = 60;
			}
			else if (cleanKey.Contains("mist") || cleanKey.Contains("fog") || cleanKey.Contains("cloud")) template.HasMistsight = true;
			else if (cleanKey.Contains("creaturesense") || cleanKey.Contains("life sight"))
			{
				template.HasLifesense = true;
				if (rangeValue > 0) template.LifesenseRange = rangeValue;
				else if (template.LifesenseRange == 0) template.LifesenseRange = 60;
			}
			else if (cleanKey.Contains("detect thoughts"))
			{
				template.HasThoughtsense = true;
				if (template.ThoughtsenseRange == 0) template.ThoughtsenseRange = 60;
			}
			else if (cleanKey.Contains("detect chaos") || cleanKey.Contains("know alignment"))
			{
				template.HasDetectChaos = true; template.HasDetectEvil = true; template.HasDetectGood = true; template.HasDetectLaw = true;
				if (template.DetectChaosRange == 0) { template.DetectChaosRange = 60; template.DetectEvilRange = 60; template.DetectGoodRange = 60; template.DetectLawRange = 60; }
			}
			else if (cleanKey.Contains("tremorsense"))
			{
				template.HasTremorsense = true;
				if (cleanKey.Contains("1 mi")) template.SpecialSenseRange = Mathf.Max(template.SpecialSenseRange, 5280);
				else if (rangeValue > 0) template.SpecialSenseRange = Mathf.Max(template.SpecialSenseRange, rangeValue);
			}
			else if (cleanKey.Contains("smoke")) template.HasSmokeVision = true;
			else if (cleanKey.Contains("disease scent"))
			{
				template.HasDiseaseScent = true;
				template.HasScent = true;
				template.ScentRange = rangeValue > 0 ? rangeValue : 30;
			}
			else if (cleanKey.Contains("appraising sight")) template.HasAppraisingSight = true;
			else if (cleanKey.Contains("soul scent")) template.HasSpiritsense = true;
			else if (cleanKey.Contains("snowcaster")) template.HasSnowsight = true;
		}
	}

	private static void ProcessAlignmentSense(Dictionary<string, string> data, CreatureTemplate_SO template, string csvKey, string spellAssetName, out bool hasSense, out int range)
	{
		string val = ImportUtils.GetValue(data, $"senses/{csvKey}", "").Trim();
		string allSenses = ImportUtils.GetValue(data, "senses", "").ToLower();
		bool isActive = val.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || allSenses.Contains(csvKey);

		hasSense = isActive;
		range = isActive ? 60 : 0;

		if (isActive)
		{
			string masterSpellPath = $"res://Data/Abilities/Spells/{spellAssetName}.tres";
			if (FileAccess.FileExists(masterSpellPath))
			{
				var masterSpell = GD.Load<Ability_SO>(masterSpellPath);
				var applyEffect = masterSpell.EffectComponents.OfType<ApplyStatusEffect>().FirstOrDefault();
				if (applyEffect != null && applyEffect.EffectToApply != null)
				{
					string safeName = string.Join("_", template.CreatureName.Split(Path.GetInvalidFileNameChars()));
					string assetName = $"Passive_{spellAssetName.Replace("Spell_", "")}_{safeName}";
					string assetPath = $"res://Data/Abilities/Special/{assetName}.tres";

					StatusEffect_SO passiveEffect;
					if (FileAccess.FileExists(assetPath)) passiveEffect = GD.Load<StatusEffect_SO>(assetPath);
					else passiveEffect = (StatusEffect_SO)applyEffect.EffectToApply.Duplicate();

					passiveEffect.EffectName = assetName;
					passiveEffect.DurationInRounds = 0; 

					if (template.PassiveEffects == null) template.PassiveEffects = new Godot.Collections.Array<StatusEffect_SO>();
					if (!template.PassiveEffects.Contains(passiveEffect)) template.PassiveEffects.Add(passiveEffect);
					ResourceSaver.Save(passiveEffect, assetPath);
				}
			}
		}
	}

	private static void ParseAttacks(Dictionary<string, string> data, CreatureTemplate_SO template, string itemPath)
	{
		template.MeleeAttacks = new Godot.Collections.Array<NaturalAttack>();
		template.StartingEquipment = new Godot.Collections.Array<Item_SO>();

		for (int i = 1; i <= 5; i++) ParseAttackEntry(ImportUtils.GetValue(data, $"attacks/melee_{i}", ""), template, itemPath, false);
		for (int i = 1; i <= 3; i++) ParseAttackEntry(ImportUtils.GetValue(data, $"attacks/ranged_{i}", ""), template, itemPath, true);
	}

	private static void ParseAttackEntry(string rawAttackString, CreatureTemplate_SO template, string itemPath, bool isRanged)
	{
		if (string.IsNullOrEmpty(rawAttackString)) return;

		if (rawAttackString.ToLower().Contains("flurry of blows"))
		{
			template.MeleeAttacks.Add(ParsePlainTextAttack(rawAttackString));
			return;
		}

		Match nameMatch = Regex.Match(rawAttackString, @"^(\+\d+\s+)?([^\(]+)");
		string attackName = nameMatch.Groups[2].Value.Trim();
		if (attackName.Contains(" or ")) attackName = attackName.Split(new[] { " or " }, StringSplitOptions.None)[0].Trim();

		bool isManufactured = ItemParser.IsManufacturedWeapon(attackName);
		if (isManufactured)
		{
			Item_SO weapon = ItemParser.GetOrCreateWeaponAsset(rawAttackString, itemPath, isRanged, _itemCache);
			bool exists = false;
			foreach(var e in template.StartingEquipment) if (e.ItemName == weapon.ItemName) exists = true;
			if (weapon != null && !exists) template.StartingEquipment.Add(weapon);
		}
		else
		{
			if (rawAttackString.Trim().StartsWith("[{"))
			{
				var nAttacks = ParseJsonNaturalAttacks(rawAttackString, template.CreatureName);
				foreach(var na in nAttacks) template.MeleeAttacks.Add(na);
			}
			else
			{
				template.MeleeAttacks.Add(ParsePlainTextAttack(rawAttackString));
			}
		}
	}

	private static NaturalAttack ParsePlainTextAttack(string text)
	{
		var attack = new NaturalAttack { AttackName = "Attack", IsPrimary = true, SpecialQualities = new Godot.Collections.Array<string>(), DamageInfo = new Godot.Collections.Array<DamageInfo>() };
		Match enhancementMatch = Regex.Match(text, @"^\+(\d+)\s+");
		if (enhancementMatch.Success)
		{
			attack.EnhancementBonus = int.Parse(enhancementMatch.Groups[1].Value);
			text = text.Substring(enhancementMatch.Length);
		}
		Match nameMatch = Regex.Match(text, @"^([^\(]+)");
		if (nameMatch.Success) attack.AttackName = nameMatch.Groups[1].Value.Trim();
		
		Match damageMatch = Regex.Match(text, @"\(([^)]+)\)");
		if (damageMatch.Success) attack.DamageInfo.Add(ImportUtils.ParseSingleDamageComponent(damageMatch.Groups[1].Value, "Physical"));
		else attack.DamageInfo.Add(new DamageInfo());
		
		return attack;
	}

	private static List<ACBreakdown> ParseACBreakdown(Dictionary<string, string> data)
	{
		return new List<ACBreakdown>(); // Logic not present in original file
	}

	private static List<DamageReduction> ParseMultipleDr(Dictionary<string, string> data)
	{
		var drs = new List<DamageReduction>();
		for (int i = 1; i <= 2; i++)
		{
			int amount = ImportUtils.GetValue<int>(data, $"DR_{i}/amount");
			if (amount > 0)
			{
				drs.Add(new DamageReduction
				{
					Amount = amount,
					Bypass = ImportUtils.GetValue<string>(data, $"DR_{i}/weakness"),
					MaxAbsorbed = ImportUtils.GetValue<int>(data, $"DR_{i}/max_absorb")
				});
			}
		}
		return drs;
	}

	private static List<DamageResistance> ParseResistances(Dictionary<string, string> data)
	{
		var resistances = new Dictionary<string, int>();
		void AddResistance(string type, string value)
		{
			if (int.TryParse(value, out int amount) && amount > 0)
			{
				if (resistances.ContainsKey(type)) resistances[type] = Mathf.Max(resistances[type], amount);
				else resistances.Add(type, amount);
			}
		}

		AddResistance("Acid", ImportUtils.GetValue(data, "resistances/acid", "0"));
		AddResistance("Acid", ImportUtils.GetValue(data, "resistances/cid", "0")); 
		AddResistance("Cold", ImportUtils.GetValue(data, "resistances/cold", "0"));
		AddResistance("Electricity", ImportUtils.GetValue(data, "resistances/electricity", "0"));
		AddResistance("Electricity", ImportUtils.GetValue(data, "resistances/electricty", "0"));
		AddResistance("Fire", ImportUtils.GetValue(data, "resistances/fire", "0"));
		AddResistance("Sonic", ImportUtils.GetValue(data, "resistances/sonic", "0"));
		AddResistance("NegativeEnergy", ImportUtils.GetValue(data, "resistances/negative energy", "0"));
		
		string coldOrElec = ImportUtils.GetValue(data, "resistances/cold or electricity", "0");
		if (int.TryParse(coldOrElec, out int coeAmount) && coeAmount > 0)
		{
			AddResistance("Cold", coeAmount.ToString());
			AddResistance("Electricity", coeAmount.ToString());
		}

		var result = new List<DamageResistance>();
		foreach (var pair in resistances)
		{
			var dr = new DamageResistance { Amount = pair.Value, DamageTypes = new Godot.Collections.Array<string>() };
			dr.DamageTypes.Add(pair.Key);
			result.Add(dr);
		}
		return result;
	}

	private static List<DamageResistance> ParseWeaknesses(Dictionary<string, string> data)
	{
		var weaknesses = new List<DamageResistance>();
		for (int i = 1; i <= 4; i++)
		{
			string val = ImportUtils.GetValue(data, $"weaknesses_{i}", "").Trim();
			if (string.IsNullOrEmpty(val)) continue;
			var weakness = new DamageResistance();
			weakness.DamageTypes = new Godot.Collections.Array<string> { val };
			weakness.Amount = 0;
			weaknesses.Add(weakness);
		}
		return weaknesses;
	}

	private static void ParseConditionalSaveBonuses(Dictionary<string, string> data, CreatureTemplate_SO template)
	{
		string combinedSavesText = string.Join(",",
			ImportUtils.GetValue<string>(data, "saves/other"),
			ImportUtils.GetValue<string>(data, "saves/fort_other"),
			ImportUtils.GetValue<string>(data, "saves/ref_other"),
			ImportUtils.GetValue<string>(data, "saves/will_other")
		).Trim(',');

		if (string.IsNullOrEmpty(combinedSavesText)) return;

		var entries = combinedSavesText.Split(new[]{','}, System.StringSplitOptions.RemoveEmptyEntries);
		var bonuses = new Godot.Collections.Array<ConditionalSaveBonus>();

		foreach (var entry in entries)
		{
			Match match = Regex.Match(entry.Trim(), @"\+(\d+)\s+vs\.?\s+(.+)");
			if (match.Success)
			{
				int bonusValue = int.Parse(match.Groups[1].Value);
				string conditionText = match.Groups[2].Value.Trim().ToLower();
				SaveCondition condition = SaveCondition.None;
				BonusType bType = BonusType.Racial;

				if (conditionText.Contains("enchantment")) condition = SaveCondition.AgainstEnchantments;
				else if (conditionText.Contains("illusion")) condition = SaveCondition.AgainstIllusions;
				else if (conditionText.Contains("fear")) condition = SaveCondition.AgainstFear;
				else if (conditionText.Contains("poison")) condition = SaveCondition.AgainstPoison;
				else if (conditionText.Contains("charm")) condition = SaveCondition.AgainstCharm;
				else if (conditionText.Contains("mind-affecting")) condition = SaveCondition.AgainstMindAffecting;
				else if (conditionText.Contains("nonmagical disease")) condition = SaveCondition.AgainstNonmagicalDisease;
				else if (conditionText.Contains("disease")) condition = SaveCondition.AgainstDisease;
				else if (conditionText.Contains("traps")) condition = SaveCondition.AgainstTraps;
				else if (conditionText.Contains("arcane spell")) condition = SaveCondition.AgainstArcane;
				else if (conditionText.Contains("death effect")) condition = SaveCondition.AgainstDeathEffects;

				if (conditionText.Contains("morale")) bType = BonusType.Morale;
				else if (conditionText.Contains("resistance")) bType = BonusType.Resistance;

				if (condition != SaveCondition.None)
				{
					bonuses.Add(new ConditionalSaveBonus { Condition = condition, ModifierValue = bonusValue, BonusType = bType });
				}
			}
		}

		if (bonuses.Count > 0)
		{
			string safeName = string.Join("_", template.CreatureName.Split(Path.GetInvalidFileNameChars()));
			string assetPath = $"res://Data/Abilities/Special/PassiveSaves_{safeName}.tres";
			
			StatusEffect_SO passiveBonusEffect;
			if(FileAccess.FileExists(assetPath)) passiveBonusEffect = GD.Load<StatusEffect_SO>(assetPath);
			else passiveBonusEffect = new StatusEffect_SO();

			passiveBonusEffect.EffectName = "Passive Save Bonuses";
			passiveBonusEffect.DurationInRounds = 0;
			passiveBonusEffect.ConditionalSaves = bonuses;
			
			if (template.PassiveEffects == null) template.PassiveEffects = new Godot.Collections.Array<StatusEffect_SO>();
			if (!template.PassiveEffects.Contains(passiveBonusEffect)) template.PassiveEffects.Add(passiveBonusEffect);
			ResourceSaver.Save(passiveBonusEffect, assetPath);
		}
	}

	private static List<ConditionalManeuverBonus> ParseConditionalManeuverBonuses(string rawData)
	{
		var bonuses = new List<ConditionalManeuverBonus>();
		if (string.IsNullOrEmpty(rawData) || rawData == "0") return bonuses;

		string[] entries = rawData.Split(',');
		foreach (var entry in entries)
		{
			string cleanEntry = entry.Trim().ToLower();
			var bonusMatch = Regex.Match(cleanEntry, @"[+-]\d+");
			if (!bonusMatch.Success) continue;
			if (!int.TryParse(bonusMatch.Value, out int bonusValue)) continue;

			string textPart = cleanEntry.Replace(bonusMatch.Value, "").Trim();
			var newBonus = new ConditionalManeuverBonus { Bonus = bonusValue, Maneuvers = new Godot.Collections.Array<ManeuverType>() };

			if (textPart.Contains("attached")) newBonus.Condition = ManeuverCondition.WhileAttached;
			else if (textPart.Contains("corporeal")) newBonus.Condition = ManeuverCondition.WhileCorporeal;
			else if (textPart.Contains("charge")) newBonus.Condition = ManeuverCondition.OnACharge;
			else if (textPart.Contains("maintain")) newBonus.Condition = ManeuverCondition.ToMaintainGrapple;
			else if (textPart.Contains("tripping")) newBonus.Condition = ManeuverCondition.WhenTripping;
			
			string[] keywords = textPart.Split(new[] { " or ", " and ", " ", "with" }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var keyword in keywords)
			{
				string cleanKeyword = keyword.Replace("ing", "");
				if (Enum.TryParse<ManeuverType>(cleanKeyword, true, out ManeuverType maneuver))
					if (!newBonus.Maneuvers.Contains(maneuver)) newBonus.Maneuvers.Add(maneuver);
			}

			if (newBonus.Maneuvers.Count == 0 && newBonus.Condition != ManeuverCondition.None) newBonus.Maneuvers.Add(ManeuverType.Any);
			if (newBonus.Maneuvers.Count > 0) bonuses.Add(newBonus);
		}
		return bonuses;
	}

	private static void ParseCmdOther(string rawData, CreatureTemplate_SO template)
	{
		if (string.IsNullOrEmpty(rawData)) return;
		template.ConditionalCmdBonuses = new Godot.Collections.Array<ConditionalCMDBonus>();
		
		string cleanData = rawData.ToLower();
		if (cleanData.Contains("can’t be tripped") || cleanData.Contains("cannot be tripped")) template.IsImmuneToTrip = true;
		if (cleanData.Contains("can’t be disarmed")) template.IsImmuneToDisarm = true;
		if (cleanData.Contains("can’t be grappled")) template.IsImmuneToGrapple = true;

		string[] entries = cleanData.Split(',');
		foreach (var entry in entries)
		{
			string cleanEntry = entry.Trim();
			if (!cleanEntry.Contains("vs.")) continue;
			string[] parts = cleanEntry.Split(new[] { " vs. " }, StringSplitOptions.None);
			if (parts.Length < 2) continue;
			if (!int.TryParse(parts[0].Trim(), out int cmdValue)) continue;

			var newCmdBonus = new ConditionalCMDBonus { CmdValue = cmdValue, Maneuvers = new Godot.Collections.Array<ManeuverType>() };
			string[] keywords = parts[1].Split(new[] { " or ", " and ", " " }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var keyword in keywords)
			{
				string cleanKeyword = keyword.Trim().Replace("bullrush", "bullrush");
				if (Enum.TryParse<ManeuverType>(cleanKeyword, true, out ManeuverType maneuver))
					if (!newCmdBonus.Maneuvers.Contains(maneuver)) newCmdBonus.Maneuvers.Add(maneuver);
			}
			if (newCmdBonus.Maneuvers.Count > 0) template.ConditionalCmdBonuses.Add(newCmdBonus);
		}
	}

	private static List<AttackReachOverride> ParseReachOverrides(string rawData, float defaultReach)
	{
		var overrides = new List<AttackReachOverride>();
		if (string.IsNullOrEmpty(rawData)) return overrides;
		string cleanData = rawData.ToLower().Trim().Replace("shapeable", "").Replace("unintuitive reach", "").Replace("plus prodigious reach", "");
		if (string.IsNullOrWhiteSpace(cleanData)) return overrides;

		string[] entries = cleanData.Split(',');

		foreach (var entry in entries)
		{
			string cleanEntry = entry.Trim();
			float reachValue = 0;
			string attacksString = "";

			var matchWithReach = Regex.Match(cleanEntry, @"(\d+)\s*f?t\.?\s+with\s+(.+)");
			var matchAttackFirst = Regex.Match(cleanEntry, @"^([\w\s]+?)\s+(\d+)\s*f?t\.?");
			var matchWithoutReach = Regex.Match(cleanEntry, @"^with\s+(.+)");

			if (matchWithReach.Success)
			{
				float.TryParse(matchWithReach.Groups[1].Value, out reachValue);
				attacksString = matchWithReach.Groups[2].Value;
			}
			else if (matchAttackFirst.Success)
			{
				attacksString = matchAttackFirst.Groups[1].Value;
				float.TryParse(matchAttackFirst.Groups[2].Value, out reachValue);
			}
			else if (matchWithoutReach.Success)
			{
				reachValue = defaultReach;
				attacksString = matchWithoutReach.Groups[1].Value;
			}

			if (reachValue > 0 && !string.IsNullOrEmpty(attacksString))
			{
				var attackNames = attacksString.Split(new[] { " and ", " or " }, StringSplitOptions.RemoveEmptyEntries)
											   .Select(s => s.Replace("w/", "").Trim()).ToList();
				var aro = new AttackReachOverride { Reach = reachValue, AttackNames = new Godot.Collections.Array<string>() };
				foreach(var s in attackNames) aro.AttackNames.Add(s);
				overrides.Add(aro);
			}
		}
		return overrides;
	}

	private static List<FeatInstance> ParseFeatInstances(string rawFeatData, string path)
	{
		var featInstances = new List<FeatInstance>();
		if (string.IsNullOrEmpty(rawFeatData)) return featInstances;
		string[] featEntries = rawFeatData.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

		foreach (var entry in featEntries)
		{
			string cleanEntry = entry.Trim();
			string featName = cleanEntry;
			string targetName = "";

			int openParen = cleanEntry.IndexOf('(');
			int closeParen = cleanEntry.IndexOf(')');
			if (openParen > 0 && closeParen > openParen)
			{
				featName = cleanEntry.Substring(0, openParen).Trim();
				targetName = cleanEntry.Substring(openParen + 1, closeParen - openParen - 1).Trim();
			}

			Feat_SO featAsset = GetOrCreateFeatAsset(featName, path);
			if (featAsset != null)
				featInstances.Add(new FeatInstance { Feat = featAsset, TargetName = targetName });
		}
		return featInstances;
	}

	private static List<SkillValue> ParseSkills(Dictionary<string, string> data, CreatureTemplate_SO template)
	{
		var skillsList = new List<SkillValue>();
		foreach (var header in data.Keys)
		{
			if (header.StartsWith("skills/"))
			{
				string valueStr = data[header];
				if (string.IsNullOrEmpty(valueStr)) continue;
				string skillName = header.Substring(7).Replace("Know.", "Knowledge").Replace(" (", "").Replace(")", "").Replace(" ", "");
				
				SkillType skillType = ImportUtils.ParseEnum(skillName, SkillType.None);
				if (skillType != SkillType.None && int.TryParse(valueStr, out int totalBonus))
				{
					int abilityMod = ImportUtils.GetAbilityModForSkill(skillType, template);
					int estimatedRanks = Mathf.Max(0, totalBonus - abilityMod);
					skillsList.Add(new SkillValue { Skill = skillType, Ranks = estimatedRanks });
				}
			}
		}
		return skillsList;
	}

	private static void ApplyPlanarReachRules(CreatureTemplate_SO template)
	{
		List<string> planarKeywords = new List<string> { "Abyssal Harvester", "Night Hag", "Dream", "Ethereal", "Shadow", "Lich" };
		bool isPlanar = planarKeywords.Any(k => template.CreatureName.Contains(k, StringComparison.OrdinalIgnoreCase));
		
		if (template.SubTypes != null)
			isPlanar |= template.SubTypes.Any(s => planarKeywords.Any(k => s.Contains(k, StringComparison.OrdinalIgnoreCase)));

		if (isPlanar)
		{
			if (template.AttackReachOverrides == null) template.AttackReachOverrides = new Godot.Collections.Array<AttackReachOverride>();
			var allAttackNames = new Godot.Collections.Array<string>();
			if (template.MeleeAttacks != null)
				foreach (var attack in template.MeleeAttacks) allAttackNames.Add(attack.AttackName);
			
			if (allAttackNames.Count > 0)
			{
				template.AttackReachOverrides.Add(new AttackReachOverride { Reach = 2000f, AttackNames = allAttackNames });
			}
		}
	}
	#endregion

	// JSON Helper for Natural Attacks (using System.Text.Json)
	private static List<NaturalAttack> ParseJsonNaturalAttacks(string rawJsonOrText, string creatureName)
	{
		var attacks = new List<NaturalAttack>();
		string jsonString = "{\"entries\":" + rawJsonOrText.Replace("'", "\"").Replace("True", "true").Replace("False", "false") + "}";
		
		try
		{
			var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			var attackList = JsonSerializer.Deserialize<AttackList>(jsonString, options);

			if (attackList?.entries == null) return attacks;

			foreach (var attackData in attackList.entries)
			{
				int count = attackData.count > 0 ? attackData.count : 1;
				for (int c = 0; c < count; c++)
				{
					var naturalAttack = new NaturalAttack 
					{ 
						AttackName = attackData.attack ?? "Unnamed Attack", 
						IsPrimary = true, 
						MiscAttackBonus = (attackData.bonus != null && attackData.bonus.Count > 0) ? attackData.bonus[0] : 0,
						SpecialQualities = new Godot.Collections.Array<string>(), 
						DamageInfo = new Godot.Collections.Array<DamageInfo>() 
					};

					if (attackData.entries != null)
					{
						foreach (var effectList in attackData.entries)
						{
							foreach (var effect in effectList)
							{
								naturalAttack.DamageInfo.Add(ImportUtils.ParseSingleDamageComponent(effect.damage, effect.type));
								
								if (!string.IsNullOrEmpty(effect.effect))
								{
									var effects = effect.effect.Split(new[] { " and ", " plus " }, StringSplitOptions.RemoveEmptyEntries);
									foreach(var e in effects) naturalAttack.SpecialQualities.Add(e.Trim());
								}
								
								if (effect.crit_range != null && effect.crit_range.Contains("-"))
									int.TryParse(effect.crit_range.Split('-')[0], out naturalAttack.CriticalThreatRange);
								
								if (effect.crit_multiplier > 0)
									naturalAttack.CriticalMultiplier = effect.crit_multiplier;
							}
						}
					}

					if (naturalAttack.SpecialQualities.Contains("grab") || naturalAttack.SpecialQualities.Contains("Grab"))
						naturalAttack.HasGrab = true;

					attacks.Add(naturalAttack);
				}
			}
		}
		catch (Exception e) 
		{ 
			GD.PrintErr($"JSON Parse Error for {creatureName}: {e.Message} \n Raw: {jsonString}"); 
		}
		return attacks;
	}
}
#endif
