using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_CreateRestrictedZone.cs (GODOT VERSION)
// PURPOSE: Spawns a zone that blocks entry or exit for specific creatures.
// REUSED BY: Magic Circle, Antilife Shell, Forcecage.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_CreateRestrictedZone : AbilityEffectComponent
{
    [Export] public PackedScene ZonePrefab;
    [Export] public bool IsMobile = true; // Attached to target?
    [Export] public float Radius = 10f;
    [Export] public bool DurationPerLevel = true;
    [Export] public int RoundsPerLevel = 1;
    
    // Config for the Controller
    [Export] public bool PreventEntry = true; // False = Prevent Exit
    [Export] public TargetFilter_SO RestrictionFilter; // Who is blocked?
    [Export] public bool AllowsSave = true; // Magic Circle allows save, Forcecage doesn't
    [Export] public SaveType SaveType = SaveType.Will;
    [Export] public bool CheckSpellResistance = true;

    // Optional: Buff applied to allies inside (Magic Circle bonus)
    [Export] public StatusEffect_SO AuraBuff; 

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (ZonePrefab == null) return;

        Node3D anchor = null;
        Vector3 spawnPos = context.AimPoint;

        if (IsMobile && context.PrimaryTarget != null)
        {
            anchor = context.PrimaryTarget;
            spawnPos = anchor.GlobalPosition;
        }

        var zoneGO = ZonePrefab.Instantiate<Node3D>();
        // Add to anchor if mobile, else scene root
        if (anchor != null) anchor.AddChild(zoneGO);
        else context.Caster.GetTree().CurrentScene.AddChild(zoneGO);
        
        zoneGO.GlobalPosition = spawnPos; // Local 0 if child? No, global ensures placement. If child, set Position zero.
        if (anchor != null) zoneGO.Position = Vector3.Zero;

        var ctrl = zoneGO as RestrictedZoneController ?? zoneGO.GetNode<RestrictedZoneController>("RestrictedZoneController");
        
        float duration = DurationPerLevel ? context.Caster.Template.CasterLevel * 10f * 6f : 60f; // Default 1 min
        
        ctrl.Initialize(context.Caster, duration, Radius, PreventEntry, RestrictionFilter, AllowsSave, SaveType, CheckSpellResistance, AuraBuff);
    }
    
    public override float GetAIEstimatedValue(EffectContext context)
    {
        return 100f; // Defensive utility
    }
}