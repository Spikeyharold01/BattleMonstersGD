using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_SpawnStructure.cs
// PURPOSE: Generic spawner for Walls (Force, Stone, Ice, Iron, etc.).
//          Scales dimensions based on Caster Level and initializes durability.
// =================================================================================================
[GlobalClass]
public partial class Effect_SpawnStructure : AbilityEffectComponent
{
    [Export] public PackedScene StructurePrefab;
    
    [ExportGroup("Durability Stats")]
    [Export] public int BaseHP = 0;
    [Export] public int HPPerLevel = 0; // Wall of Force=20, Stone=15/inch
    [Export] public int Hardness = 0; // Force=30, Stone=8
    
    [ExportGroup("Scaling Dimensions")]
    [Export] public float BaseWidth = 10f;
    [Export] public float WidthPerLevel = 0f; // Wall of Force=10, Stone=5
    [Export] public float BaseHeight = 10f;
    [Export] public float HeightPerLevel = 0f;
    [Export] public float Thickness = 1f;

    [ExportGroup("Placement Rules")]
    [Export] public bool FailsIfOccupied = true; // Force=True, Stone=False (pushes)
    [Export] public bool IsUndispellable = false;
    [Export] public bool FaceCaster = true; // True for Walls, False for domes/cages

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (StructurePrefab == null) return;

        int cl = context.Caster.Template.CasterLevel;
        float finalWidth = BaseWidth + (WidthPerLevel * cl);
        float finalHeight = BaseHeight + (HeightPerLevel * cl);
        
        Vector3 direction = (context.AimPoint - context.Caster.GlobalPosition).Normalized();
        if (direction == Vector3.Zero) direction = Vector3.Forward;

        Vector3 spawnPos = context.AimPoint;
        
        // 1. Check for Occupied Space
        if (FailsIfOccupied)
        {
            var spaceState = context.Caster.GetWorld3D().DirectSpaceState;
            // Assuming Box Shape for check. Ideally match prefab shape.
            var shape = new BoxShape3D { Size = new Vector3(finalWidth, finalHeight, Thickness) };
            
            Transform3D shapeTrans = new Transform3D(Basis.Identity, spawnPos);
            if (FaceCaster) shapeTrans.Basis = Basis.LookingAt(direction, Vector3.Up);

            var query = new PhysicsShapeQueryParameters3D 
            { 
                Shape = shape, 
                Transform = shapeTrans,
                CollisionMask = 2 // Creature Layer
            };
            
            var hits = spaceState.IntersectShape(query);
            if (hits.Count > 0)
            {
                GD.PrintRich($"[color=orange]{ability.AbilityName} fails: Space is occupied.[/color]");
                return;
            }
        }

        // 2. Spawn
        var structureNode = StructurePrefab.Instantiate<Node3D>();
        context.Caster.GetTree().CurrentScene.AddChild(structureNode);
        
        structureNode.GlobalPosition = spawnPos;
        if (FaceCaster) structureNode.LookAt(context.Caster.GlobalPosition, Vector3.Up);
        
        // 3. Scale Visuals/Collider
        // We apply scale to the root node, assuming the prefab components (Mesh/Collision) act as children relative to 1 unit.
        structureNode.Scale = new Vector3(finalWidth, finalHeight, Thickness);

        // 4. Initialize Durability
        var dur = structureNode.GetNodeOrNull<ObjectDurability>("ObjectDurability");
        if (dur != null)
        {
            int totalHP = BaseHP + (HPPerLevel * cl);
            dur.Initialize(totalHP, Hardness);
        }

        // 5. Initialize Persistence (Dispelling/Duration)
        var persist = structureNode.GetNodeOrNull<PersistentEffectController>("PersistentEffectController");
        if (persist != null)
        {
            var info = new PersistentEffectInfo
            {
                EffectName = ability.AbilityName,
                Caster = context.Caster,
                SourceAbility = ability,
                IsUndispellable = IsUndispellable
            };
            
            // Duration logic: Should normally be in Ability_SO or passed here.
            // Wall of Force is 1 rnd/level. Stone is Instantaneous.
            // We use a safe default or assume PersistentEffectController handles instant/permanent logic if duration <= 0?
            // For now, hardcode 1 rnd/level if it's Force, or pass via Export?
            // BETTER: Add DurationOverride to this component.
            // Defaults to 1 rnd/level for Force.
            float durationSec = cl * 6f; 
            
            persist.Initialize(info, durationSec, new AreaOfEffect());
        }

        GD.Print($"{context.Caster.Name} created {ability.AbilityName} (Size: {finalWidth}x{finalHeight}).");
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        var enemy = context.Caster.GetNode<AIController>("AIController")?.GetPerceivedHighestThreat();
        if (enemy != null)
        {
             // Check if aim point is between Caster and Enemy
             float d1 = context.Caster.GlobalPosition.DistanceTo(context.AimPoint);
             float d2 = context.AimPoint.DistanceTo(enemy.GlobalPosition);
             float total = context.Caster.GlobalPosition.DistanceTo(enemy.GlobalPosition);
             
             if (Mathf.Abs((d1 + d2) - total) < 2f) return 150f; 
        }
        return 0f;
    }
}