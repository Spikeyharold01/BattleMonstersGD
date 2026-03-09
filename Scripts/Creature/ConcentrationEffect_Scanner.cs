using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: ConcentrationEffect_Scanner.cs (GODOT VERSION)
// PURPOSE: Generic logic for Detect Magic, Detect Thoughts, Detect Evil, Detect Undead.
// REPLACES: DetectMagicController.cs
// =================================================================================================
public enum ScanType { Magic, Thoughts, Evil, Undead }

public partial class ConcentrationEffect_Scanner : GridNode
{
    private CreatureStats caster;
    private float duration;
    private int roundsConcentrated = 0;
    private ScanType scanType;
    private AreaOfEffect area;

    public void Initialize(CreatureStats source, float dur, ScanType type, AreaOfEffect aoe)
    {
        caster = source;
        duration = dur;
        scanType = type;
        area = aoe;
        AddToGroup("Scanners");
        GD.Print($"{caster.Name} begins concentrating on {scanType}.");
    }

    public override void _Process(double delta)
    {
        duration -= (float)delta;
        if (duration <= 0)
        {
            GD.Print($"{scanType} detection on {caster.Name} has expired.");
            QueueFree();
        }
    }

    public void Concentrate()
    {
        roundsConcentrated++;
        GD.Print($"{caster.Name} concentrates on detecting {scanType} (Round {roundsConcentrated})...");
        
        // Get generic cone targets (using -Z forward for caster)
        var targets = AoEHelper.GetTargetsInCone(caster.GetParent<Node3D>(), -caster.GetParent<Node3D>().GlobalTransform.Basis.Z, area, "Creature"); 
        // Note: For Magic, we also need Items/Objects, but AoEHelper currently does Creatures.
        // Expanding scope for Magic requires querying objects or scanning all nodes.
        // For Simplicity, we stick to Creatures + Equipped Items logic here.

        switch (scanType)
        {
            case ScanType.Thoughts: ProcessThoughts(targets); break;
            case ScanType.Magic:    ProcessMagic(targets); break;
            case ScanType.Evil:     ProcessAlignment(targets, "Evil"); break;
            case ScanType.Undead:   ProcessCreatureType(targets, CreatureType.Undead); break;
        }
    }
    
    // --- DETECT THOUGHTS LOGIC ---
    private void ProcessThoughts(IEnumerable<CreatureStats> targets)
    {
        var thinkingTargets = targets.Where(t => t.Template.Intelligence >= 1 && !t.HasImmunity(ImmunityType.MindAffecting)).ToList();
        
        if (roundsConcentrated == 1)
        {
             if (thinkingTargets.Any()) GD.Print("Presence of thinking minds detected.");
        }
        else if (roundsConcentrated == 2)
        {
             GD.Print($"{thinkingTargets.Count} thinking minds detected.");
             foreach(var t in thinkingTargets)
             {
                 // DYNAMIC CALCULATION
                 BehaviorTag tag = GetBehaviorTag(t); 
                 
                 GD.Print($"-- {t.Name}: {tag}");
                 
                 // STORE THE ENUM (Works for both AI and Player memory now)
                 CombatMemory.RecordBehaviorTag(t, tag);
             }
        }
        else if (roundsConcentrated >= 3)
        {
             foreach(var t in thinkingTargets)
             {
                 bool volatileState = (float)t.CurrentHP / t.Template.MaxHP < 0.5f; 
                 GD.Print($"-- {t.Name} State: {(volatileState ? "Volatile" : "Stable")}");
             }
        }
    }

    private BehaviorTag GetBehaviorTag(CreatureStats target)
    {
        var ai = target.GetNodeOrNull<AIController>("AIController");
        if (ai == null) return BehaviorTag.Unknown; // Player or Mindless
        
        var p = ai.Personality;

        // 1. Check Capability (Highest priority)
        if (target.Template.KnownAbilities.Any(a => a.AiTacticalTag != null && a.AiTacticalTag.Role == TacticalRole.Healing))
        {
            return BehaviorTag.Support;
        }
        if (target.Template.KnownAbilities.Any(a => a.AiTacticalTag != null && (a.AiTacticalTag.Role == TacticalRole.BattlefieldControl || a.AiTacticalTag.Role == TacticalRole.Debuff_Offensive)))
        {
            return BehaviorTag.Control;
        }

        // 2. Check Personality Weights
        if (p.W_Aggressive >= 150) return BehaviorTag.Aggressive;
        if (p.W_Defensive >= 150) return BehaviorTag.Defensive;
        
        // 3. Check Logic
        if (p.W_TargetLowHealth > 20) return BehaviorTag.Predatory;

        return BehaviorTag.Balanced;
    }

    // --- DETECT MAGIC LOGIC (Migrated) ---
    private void ProcessMagic(IEnumerable<CreatureStats> targets)
    {
        var magicalTargets = new List<CreatureStats>();
        foreach (var t in targets)
        {
            var auraCtrl = t.GetNodeOrNull<AuraController>("AuraController");
            if (auraCtrl != null && auraCtrl.Auras.Any()) magicalTargets.Add(t);
        }

        if (roundsConcentrated == 1)
        {
            if (magicalTargets.Any()) GD.Print("Presence of magical auras detected.");
        }
        else if (roundsConcentrated == 2)
        {
            int totalAuras = magicalTargets.Sum(t => t.GetNode<AuraController>("AuraController").Auras.Count);
            GD.Print($"{totalAuras} auras detected.");
            foreach(var t in magicalTargets)
            {
                var str = t.GetNode<AuraController>("AuraController").GetMostPotentAuraStrength();
                CombatMemory.RecordMagicalAura(t, str);
            }
        }
        else if (roundsConcentrated >= 3)
        {
            foreach (var t in magicalTargets)
            {
                var auras = t.GetNode<AuraController>("AuraController").Auras;
                foreach(var aura in auras)
                {
                    GD.Print($"-- {t.Name}: {aura.Strength} ({aura.School}) from {aura.SourceName}");
                }
            }
        }
    }

    // --- DETECT EVIL / ALIGNMENT LOGIC ---
    private void ProcessAlignment(IEnumerable<CreatureStats> targets, string alignmentKey)
    {
        var alignedTargets = targets.Where(t => t.Template.Alignment.Contains(alignmentKey)).ToList();
        
        // Note: Detect Evil requires Line of Sight (unlike Detect Magic/Thoughts which penetrate barriers).
        // Adding LoS Check for Evil:
        alignedTargets = alignedTargets.Where(t => LineOfSightManager.HasLineOfEffect(caster, caster.GetParent<Node3D>().GlobalPosition, t.GlobalPosition)).ToList();

        if (roundsConcentrated == 1)
        {
            if (alignedTargets.Any()) GD.Print($"Presence of {alignmentKey} detected.");
        }
        else if (roundsConcentrated == 2)
        {
             GD.Print($"{alignedTargets.Count} {alignmentKey} auras detected.");
        }
        else if (roundsConcentrated >= 3)
        {
             foreach(var t in alignedTargets)
             {
                 // Calculate Aura Strength based on HD/Cleric Level (Simplified to CR for now)
                 string strength = "Faint";
                 if (t.Template.ChallengeRating >= 11) strength = "Strong";
                 else if (t.Template.ChallengeRating >= 5) strength = "Moderate";
                 
                 GD.Print($"-- {t.Name}: {strength} {alignmentKey} Aura.");
                 CombatMemory.RecordAlignment(t, t.Template.Alignment);
             }
        }
    }

    // --- DETECT UNDEAD / TYPE LOGIC ---
    private void ProcessCreatureType(IEnumerable<CreatureStats> targets, CreatureType type)
    {
        var typeTargets = targets.Where(t => t.Template.Type == type).ToList();

        if (roundsConcentrated == 1)
        {
            if (typeTargets.Any()) GD.Print($"Presence of {type} detected.");
        }
        else if (roundsConcentrated == 2)
        {
             GD.Print($"{typeTargets.Count} {type} creatures detected.");
        }
        else if (roundsConcentrated >= 3)
        {
             foreach(var t in typeTargets)
             {
                 string strength = "Faint"; // Undead aura strength logic (HD based)
                 if (t.Template.ChallengeRating >= 5) strength = "Moderate"; 
                 GD.Print($"-- {t.Name}: {strength} Aura.");
             }
        }
    }
}