using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_SpawnProtectiveAura.cs
// PURPOSE: A generic effect to spawn a protective zone (Magic Circle) and apply specific
//          alignment-based defenses (AC/Saves) and optional Globe of Invulnerability.
// REUSED BY: Cetaceal (Protective Aura), Angels (Protective Aura), Cleric (Magic Circle).
// =================================================================================================
[GlobalClass]
public partial class Effect_SpawnProtectiveAura : AbilityEffectComponent
{
    [ExportGroup("Zone Settings")]
    [Export] public PackedScene ZonePrefab;
    [Export] public float Radius = 20f;
    [Export] public bool Mobile = true;
    [Export] public int DurationRounds = 100;

    [ExportGroup("Protection Settings")]
    [Export(PropertyHint.Enum, "Evil,Good,Law,Chaos")] 
    public string ProtectAgainstAlignment = "Evil";
    
    [Export] public int DeflectionACBonus = 2; // Standard PFE is +2, Cetaceal is +4
    [Export] public int ResistanceSaveBonus = 2; // Standard PFE is +2, Cetaceal is +4
    
    [ExportGroup("Secondary Effects")]
    [Export] public bool GrantGlobeOfInvulnerability = false;
    [Export] public SpecialDefense GlobeType = SpecialDefense.GlobeOfInvulnerability_Lesser;
    
    [Export] public bool PreventSummonedContact = true;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (ZonePrefab == null)
        {
            GD.PrintErr($"Effect_SpawnProtectiveAura on {ability.AbilityName} missing ZonePrefab.");
            return;
        }

        // 1. Construct the Aura Buff (StatusEffect_SO) at runtime based on settings
        var auraBuff = new StatusEffect_SO();
        auraBuff.EffectName = $"Aura: Protection from {ProtectAgainstAlignment}";
        auraBuff.Description = $"+{DeflectionACBonus} AC and +{ResistanceSaveBonus} Saves vs {ProtectAgainstAlignment}.";
        auraBuff.DurationInRounds = 1; // Refreshed continuously by the zone
        auraBuff.IsDischargeable = false;

        // A. Deflection Bonus to AC vs Alignment
        if (DeflectionACBonus != 0)
        {
            var acMod = new StatModification();
            acMod.StatToModify = StatToModify.ArmorClass;
            acMod.ModifierValue = DeflectionACBonus;
            acMod.BonusType = BonusType.Deflection;

            // Create dynamic source filter
            var alignFilter = new TargetFilter_SO();
            alignFilter.RequiredAlignment = ProtectAgainstAlignment;
            // TargetFilter_SO checks for string containment of "Evil", "Good", etc.
            
            acMod.SourceFilter = alignFilter;
            auraBuff.Modifications.Add(acMod);
        }

        // B. Resistance Bonus to Saves vs Alignment
        if (ResistanceSaveBonus != 0)
        {
            var saveMod = new ConditionalSaveBonus();
            // Map string to Enum
            saveMod.Condition = GetSaveConditionForString(ProtectAgainstAlignment);
            saveMod.ModifierValue = ResistanceSaveBonus;
            saveMod.BonusType = BonusType.Resistance;
            auraBuff.ConditionalSaves.Add(saveMod);
        }

        // C. Globe of Invulnerability
        if (GrantGlobeOfInvulnerability)
        {
            auraBuff.SpecialDefenses.Add(GlobeType);
            auraBuff.EffectName += " + Globe";
        }

        // 2. Construct the Zone Restriction Filter (Magic Circle logic)
        TargetFilter_SO restrictionFilter = null;
        if (PreventSummonedContact)
        {
            restrictionFilter = new TargetFilter_SO();
            restrictionFilter.MustBeSummoned = true;
            restrictionFilter.RequiredAlignment = ProtectAgainstAlignment;
            restrictionFilter.RequiredRelationship = TargetFilter_SO.Relationship.Enemy;
        }
        else
        {
            // Empty filter that matches nothing implies no restriction on movement
            // But RestrictedZoneController expects a filter to know WHO to push.
            // If we don't prevent contact, we pass null to the controller or a dummy that matches nothing.
            // For this implementation, null implies no movement restriction.
        }

        // 3. Spawn Zone
        var zoneGO = ZonePrefab.Instantiate<Node3D>();
        
        if (Mobile)
        {
            context.Caster.AddChild(zoneGO);
            zoneGO.Position = Vector3.Zero;
        }
        else
        {
            context.Caster.GetTree().CurrentScene.AddChild(zoneGO);
            zoneGO.GlobalPosition = context.AimPoint;
        }

        var ctrl = zoneGO as RestrictedZoneController ?? zoneGO.GetNode<RestrictedZoneController>("RestrictedZoneController");
        
        // Calculate Duration
        int finalDuration = DurationRounds;
        if (ability.Category == AbilityCategory.Spell || ability.SpecialAbilityType == SpecialAbilityType.Sp)
        {
            finalDuration = context.Caster.Template.CasterLevel * 10; // Default 1 min/level
        }
        // Su abilities might be permanent/toggle, but here we set a high duration or rely on logic
        if (ability.SpecialAbilityType == SpecialAbilityType.Su) finalDuration = 9999;

        ctrl.Initialize(
            context.Caster,
            finalDuration,
            Radius,
            PreventSummonedContact, // Prevent Entry?
            restrictionFilter,
            true, // Allows Save (Magic Circle usually does against containment/hedging)
            SaveType.Will, // Will save to push through
            true, // Check SR
            auraBuff // Apply the generated buff to allies inside
        );

        GD.PrintRich($"<color=cyan>{context.Caster.Name} spawns {auraBuff.EffectName} (Radius {Radius}).</color>");
    }

    private SaveCondition GetSaveConditionForString(string align)
    {
        if (align.Contains("Evil")) return SaveCondition.AgainstEvil;
        if (align.Contains("Good")) return SaveCondition.AgainstGood;
        if (align.Contains("Law")) return SaveCondition.AgainstLaw;
        if (align.Contains("Chaos")) return SaveCondition.AgainstChaos;
        return SaveCondition.None;
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        // Simple heuristic: Value scales with radius and bonus magnitude
        float value = (DeflectionACBonus + ResistanceSaveBonus) * 15f;
        if (GrantGlobeOfInvulnerability) value += 100f;
        return value;
    }
}