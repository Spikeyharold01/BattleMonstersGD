using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_CreateZone.cs (GODOT VERSION)
// PURPOSE: Spawns a persistent effect area (Fog Cloud, Acid Pit, etc).
// Handles standard and mythic radius scaling.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_CreateZone : AbilityEffectComponent
{
[ExportGroup("Zone Settings")]
[Export] public string ZoneName = "Fog Cloud";
[Export] public PackedScene ZonePrefab;

[ExportGroup("Dimensions")]
[Export] public float Radius = 20f;
[Export] public float MythicRadius = 50f;

[ExportGroup("Duration")]
[Export] public int DurationRounds = -1;
[Export] public bool DurationPerLevel = true; 
[Export] public int RoundsPerLevel = 100; 

[ExportGroup("Grid Tags")]
[Export] public Godot.Collections.Array<string> EnvironmentalTags = new();

[ExportGroup("Terrain Modifications")]
[Export] public bool MakesTerrainIcy = false;
[Export] public bool ExtinguishesFire = false;
[Export] public bool IsDifficultTerrain = false;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    // 1. Calculate Duration
    int finalDuration = DurationRounds;
    if (DurationRounds == -1)
    {
        finalDuration = context.Caster.Template.CasterLevel * RoundsPerLevel;
    }

    // 2. Calculate Radius (Check Mythic Flag)
    float finalRadius = context.IsMythicCast ? MythicRadius : Radius;

    // 3. Spawn Object
    if (ZonePrefab == null)
    {
         GD.PrintErr($"Effect_CreateZone {ability.AbilityName} missing Prefab.");
         return;
    }

    Node3D go = ZonePrefab.Instantiate<Node3D>();
	 
    Vector3 zonePosition = context.AimPoint;
    Vector3 windDirection = (context.AimPoint - context.Caster.GlobalPosition).Normalized();
    if (windDirection == Vector3.Zero)
    {
        windDirection = context.Caster.GlobalBasis.Z;
    }

    if (ability.AreaOfEffect.Shape == AoEShape.Line)
    {
        // For line effects, place the zone midway so the box can cover the full distance from caster.
        float lineLength = Mathf.Max(ability.AreaOfEffect.Range, 5f);
        zonePosition = context.Caster.GlobalPosition + (windDirection * (lineLength * 0.5f));
        go.LookAt(zonePosition + windDirection, Vector3.Up);
    }
    go.Name = ZoneName + (context.IsMythicCast ? " (Mythic)" : "");
    
    SceneTree tree = (SceneTree)Engine.GetMainLoop();
    tree.CurrentScene.AddChild(go);
    go.GlobalPosition = zonePosition;
    
    // 4. Initialize Controller
    var controller = go as PersistentEffectController ?? go.GetNodeOrNull<PersistentEffectController>("PersistentEffectController");
    if (controller == null) 
    {
        controller = new PersistentEffectController();
        controller.Name = "PersistentEffectController";
        go.AddChild(controller);
    }

    var info = new PersistentEffectInfo
    {
        EffectName = ZoneName,
        Caster = context.Caster,
        SourceAbility = ability,
        SpellLevel = ability.SpellLevel,
        LingeringEffects = new Godot.Collections.Array<AbilityEffectComponent>() 
    };

    var area = new AreaOfEffect { Range = finalRadius, Shape = AoEShape.Burst };
    controller.Initialize(info, finalDuration, area);

    // 5. Apply Grid Tags via Helper
    var zone = go.GetNodeOrNull<EnvironmentalZone>("EnvironmentalZone");
    if (zone == null)
    {
        zone = new EnvironmentalZone();
        zone.Name = "EnvironmentalZone";
        go.AddChild(zone);
    }
    
    zone.Tags = EnvironmentalTags;
    zone.Radius = finalRadius;
    zone.MakesTerrainIcy = MakesTerrainIcy;
    zone.ExtinguishesFire = ExtinguishesFire;
    zone.IsDifficultTerrain = IsDifficultTerrain;
	
    if (UseAbilityShapeForZone && ability.AreaOfEffect.Shape == AoEShape.Line)
    {
        float lineLength = Mathf.Max(ability.AreaOfEffect.Range, 5f);
        float lineWidth = ability.AreaOfEffect.Width > 0 ? ability.AreaOfEffect.Width : 5f;
        zone.IsBoxShape = true;
        zone.BoxSize = new Vector3(lineWidth, GridManager.Instance != null ? GridManager.Instance.nodeDiameter : 5f, lineLength);
        zone.WindDirection = windDirection;
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    return 20f; 
}
}