using Godot;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// =================================================================================================
// FILE: Effect_Summon.cs
// PURPOSE: Generic summon effect pipeline for summon-list style spells.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_Summon : AbilityEffectComponent
{
    [Export] public SummonList_SO SummonList;
    [Export] public int DurationRoundsPerLevel = 1;
    [Export] public bool Dismissible = true;
    [Export] public bool AIPicksBest = true;
    [Export] public CreatureTemplate_SO ForcedCreature;

    [Export] public string RestrictionText = "";
    [Export] public string RequiredNameContains = "";
    [Export] public string RequiredSubtypeContains = "";

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (context?.Caster == null)
        {
            GD.PrintErr("Summon failed: missing caster context.");
            return;
        }

        if (SummonList == null)
        {
            GD.PrintErr("Summon failed: SummonList is missing.");
            return;
        }

        SummonEntry chosenEntry = ChooseEntry(context.Caster);
        if (chosenEntry == null)
        {
            GD.PrintErr($"Summon failed: no valid summon entry for list level {SummonList.SpellLevel}.");
            return;
        }

        int count = RollCountForEntry(chosenEntry);
        for (int i = 0; i < count; i++)
        {
            Vector3 spawnPosition = FindSpawnPosition(context.AimPoint, i);
            SpawnCreature(context.Caster, chosenEntry, spawnPosition);
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        int listLevel = SummonList?.SpellLevel ?? 1;
        return Math.Max(1, listLevel) * 10f;
    }

    private SummonEntry ChooseEntry(CreatureStats caster)
    {
        if (ForcedCreature != null)
        {
            return new SummonEntry
            {
                CreatureTemplate = ForcedCreature,
                CreatureNameFallback = ForcedCreature.CreatureName,
                SourceListLevel = SummonList.SpellLevel,
                CountDice = 1,
                CountBonus = 0
            };
        }

        var eligible = new List<SummonEntry>();
        foreach (var entry in SummonList.Entries)
        {
            if (entry == null) continue;
            if (!PassesRestriction(entry)) continue;
            eligible.Add(entry);
        }

        if (eligible.Count == 0) return null;

        int bestSourceLevel = -1;
        foreach (var entry in eligible)
        {
            if (entry.SourceListLevel > bestSourceLevel) bestSourceLevel = entry.SourceListLevel;
        }

        foreach (var entry in eligible)
        {
            if (entry.SourceListLevel == bestSourceLevel)
            {
                return entry;
            }
        }

        return eligible[0];
    }

    private int RollCountForEntry(SummonEntry entry)
    {
        int dice = Math.Max(1, entry.CountDice);
        int bonus = entry.CountBonus;
        return Dice.Roll(1, dice) + bonus;
    }

    private void SpawnCreature(CreatureStats caster, SummonEntry entry, Vector3 position)
    {
        CreatureTemplate_SO template = entry.CreatureTemplate;
        if (template == null && !string.IsNullOrWhiteSpace(entry.CreatureNameFallback))
        {
            template = TryResolveCreatureTemplate(entry.CreatureNameFallback);
        }

        if (template?.CharacterPrefab == null)
        {
            string templateName = template?.CreatureName ?? entry.CreatureNameFallback;
            GD.PrintErr($"Summon failed: '{templateName}' has no resolvable CharacterPrefab.");
            return;
        }

        Node3D spawned = template.CharacterPrefab.Instantiate<Node3D>();
        SceneTree tree = (SceneTree)Engine.GetMainLoop();
        tree.CurrentScene.AddChild(spawned);
        spawned.GlobalPosition = position;

        CreatureStats stats = spawned as CreatureStats ?? spawned.GetNodeOrNull<CreatureStats>("CreatureStats");
        if (stats == null)
        {
            GD.PrintErr($"Summon failed: spawned prefab for '{template.CreatureName}' has no CreatureStats.");
            spawned.QueueFree();
            return;
        }

        stats.IsSummoned = true;
        stats.Caster = caster;

        if (caster.IsInGroup("Player"))
        {
            stats.RemoveFromGroup("Enemy");
            stats.AddToGroup("Player");
        }
        else if (caster.IsInGroup("Enemy"))
        {
            stats.RemoveFromGroup("Player");
            stats.AddToGroup("Enemy");
        }

        bool casterIsGood = AlignmentContains(caster, "good") || AlignmentContains(caster, "lg") || AlignmentContains(caster, "ng") || AlignmentContains(caster, "cg");
        bool casterIsEvil = AlignmentContains(caster, "evil") || AlignmentContains(caster, "le") || AlignmentContains(caster, "ne") || AlignmentContains(caster, "ce");

        bool celestial = entry.ApplyCelestialIfGood && casterIsGood;
        bool fiendish = entry.ApplyFiendishIfEvil && casterIsEvil;

        var summonController = new SummonedCreatureController { Name = "SummonedCreatureController" };
        spawned.AddChild(summonController);

        int casterLevel = Math.Max(1, caster.Template?.CasterLevel ?? 1);
        int duration = Math.Max(1, casterLevel * Math.Max(1, DurationRoundsPerLevel));
        summonController.Initialize(duration, caster, RestrictionText);
        summonController.MarkTemplateFlags(celestial, fiendish);

        GD.Print($"{caster.Name} summons {template.CreatureName} at {position}. Restriction: {RestrictionText}");
    }

    private bool PassesRestriction(SummonEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(RequiredNameContains) &&
            entry.CandidateName().IndexOf(RequiredNameContains.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(RequiredSubtypeContains))
        {
            string[] tokens = RequiredSubtypeContains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string token in tokens)
            {
                bool inSubtype = (entry.SubtypesCsv ?? "").IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
                bool inName = entry.CandidateName().IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!inSubtype && !inName) return false;
            }
        }

        return true;
    }

    private static bool AlignmentContains(CreatureStats creature, string needle)
    {
        string alignment = creature?.Template?.Alignment ?? "";
        return alignment.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Vector3 FindSpawnPosition(Vector3 center, int offsetIndex)
    {
        float angle = offsetIndex * 0.8f;
        float radius = 1.5f + (offsetIndex * 0.2f);
        return center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
    }

    private static CreatureTemplate_SO TryResolveCreatureTemplate(string creatureName)
    {
        if (string.IsNullOrWhiteSpace(creatureName)) return null;

        string safeName = Regex.Replace(creatureName.Trim(), @"[^A-Za-z0-9_\- ]", "");
        safeName = Regex.Replace(safeName, @"\s+", "_");
        string assetPath = $"res://Data/Creatures/{safeName}.tres";

        if (!FileAccess.FileExists(assetPath)) return null;
        return GD.Load<CreatureTemplate_SO>(assetPath);
    }
}

public static class SummonEntryExtensions
{
    public static string CandidateName(this SummonEntry entry)
    {
        if (entry == null) return string.Empty;
        if (entry.CreatureTemplate != null && !string.IsNullOrWhiteSpace(entry.CreatureTemplate.CreatureName))
        {
            return entry.CreatureTemplate.CreatureName;
        }

        return entry.CreatureNameFallback ?? string.Empty;
    }
}