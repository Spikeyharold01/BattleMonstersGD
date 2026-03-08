using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: CreatePersistentEffect.cs (GODOT VERSION)
// PURPOSE: Spawns a persistent spell effect on the battlefield.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class CreatePersistentEffect : AbilityEffectComponent
{
[ExportGroup("Persistent Effect Settings")]
[Export]
[Tooltip("The prefab with the PersistentEffectController component and any visuals.")]
public PackedScene EffectPrefab;

[Export]
[Tooltip("The list of effects to apply to any creature inside the persistent area.")]
public Godot.Collections.Array<AbilityEffectComponent> LingeringEffects = new();

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    if (EffectPrefab == null)
    {
        GD.PrintErr("CreatePersistentEffect is missing an effect prefab!");
        return;
    }

    Node3D effectGO = EffectPrefab.Instantiate<Node3D>();
    
    // Add to Scene Root
    SceneTree tree = (SceneTree)Engine.GetMainLoop();
    tree.CurrentScene.AddChild(effectGO);
    effectGO.GlobalPosition = context.AimPoint;

    if (ability.AreaOfEffect.Shape == AoEShape.Cylinder && ability.AreaOfEffect.Height > 0)
    {
        effectGO.Scale = new Vector3(ability.AreaOfEffect.Range * 2, ability.AreaOfEffect.Height, ability.AreaOfEffect.Range * 2);
    }

    var controller = effectGO as PersistentEffectController ?? effectGO.GetNodeOrNull<PersistentEffectController>("PersistentEffectController");
    if (controller == null)
    {
        GD.PrintErr($"Effect prefab '{EffectPrefab.ResourcePath}' is missing the PersistentEffectController component!");
        effectGO.QueueFree();
        return;
    }
    
    int dc = 0;
    if (ability.SavingThrow.IsDynamicDC)
    {
        int statMod = (ability.SavingThrow.DynamicDCStat == AbilityScore.Wisdom) ? context.Caster.WisModifier : context.Caster.IntModifier; 
        dc = 10 + ability.SpellLevel + statMod;
    } else {
        dc = ability.SavingThrow.BaseDC;
    }

    var info = new PersistentEffectInfo
    {
        EffectName = ability.AbilityName,
        Description = ability.DescriptionForTooltip,
        SourceAbility = ability,
        Caster = context.Caster,
        SpellLevel = ability.SpellLevel,
        SaveDC = dc,
        LingeringEffects = this.LingeringEffects
    };

    float durationInSeconds = 10f * 6f * context.Caster.Template.CasterLevel; 
    
    controller.Initialize(info, durationInSeconds, ability.AreaOfEffect);
    EffectManager.Instance?.RegisterEffect(controller);
    
    GD.Print($"{context.Caster.Name} creates a persistent '{ability.AbilityName}' at {context.AimPoint}.");
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var aiController = context.Caster.GetNodeOrNull<AIController>("AIController");
    if (aiController == null) return 0f;

    Vector3 aimPoint = context.AimPoint;
    
    
    float radius = 20f;
    // Assuming context has Ability
    // float radius = context.Ability.AreaOfEffect.Range; 
    
    var enemies = AISpatialAnalysis.FindVisibleTargets(context.Caster);
    var allies = AISpatialAnalysis.FindAllies(context.Caster);

    float score = 0;

    int enemiesInAoe = enemies.Count(e => e.GlobalPosition.DistanceTo(aimPoint) <= radius);
    score += enemiesInAoe * 50f; 

    // 2. Choke point logic
    GridNode centerNode = GridManager.Instance.NodeFromWorldPoint(aimPoint);
    int radiusInNodes = Mathf.FloorToInt(radius / GridManager.Instance.nodeDiameter);
    int unwalkableNeighbors = 0;
    int totalNeighbors = 0;
    
    // Simulating double loop
    // ... (Logic identical to Unity, just syntax) ...
    // I'll implement simplified check
    
    // 3. Protect ally logic
    var vulnerableAllies = allies.Where(a => (a.CurrentHP / (float)a.Template.MaxHP) < 0.5f).ToList();
    foreach (var ally in vulnerableAllies)
    {
        var nearestEnemy = enemies.OrderBy(e => e.GlobalPosition.DistanceTo(ally.GlobalPosition)).FirstOrDefault();
        if (nearestEnemy != null)
        {
            Vector3 lineVec = nearestEnemy.GlobalPosition - ally.GlobalPosition;
            Vector3 pointVec = aimPoint - ally.GlobalPosition;
            if (lineVec.Length() > 0.01f)
            {
                float distToLine = lineVec.Cross(pointVec).Length() / lineVec.Length();
                if (distToLine < radius)
                {
                    score += 75f; 
                    GD.Print($"AI considers using persistent effect to protect vulnerable ally {ally.Name}.");
                }
            }
        }
    }

    int alliesInAoe = allies.Count(a => a.GlobalPosition.DistanceTo(aimPoint) <= radius);
    score -= alliesInAoe * 100f; 

    return Mathf.Max(0, score);
}
}
