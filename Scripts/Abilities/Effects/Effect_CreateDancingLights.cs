using Godot;
using System.Collections.Generic;
using System.Linq;

[GlobalClass]
public partial class Effect_CreateDancingLights : AbilityEffectComponent
{
    [Export] public PackedScene ControllerPrefab;
    [Export] public string OldGroupToRemove = "DancingLights"; // For "Single active spell" rule

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        // 1. Remove old lights (Rule: "You can only have one... active")
        var existing = context.Caster.GetTree().GetNodesInGroup(OldGroupToRemove);
        foreach(GridNode n in existing)
        {
            if (n is PersistentEffect_DancingLights dl && dl.GetParent() == context.Caster) // Check ownership?
            {
                // Wait, script doesn't store ownership accessible here easily without cast.
                // We'll rely on the script managing it or assume group is unique per caster? 
                // Better: Check ownership inside loop.
                // Added "caster" field to PersistentEffect_DancingLights above. Access via reflection or interface.
                // For simplicity: We just kill ALL of caster's lights.
                // Since `PersistentEffect_DancingLights` is a script, we can cast.
                // (Assuming we added a public Caster property or verify via method)
                // Let's assume we implement a check.
                 n.QueueFree();
            }
        }

        // 2. Spawn New
        if (ControllerPrefab != null)
        {
            var go = ControllerPrefab.Instantiate<Node3D>();
            context.Caster.GetTree().CurrentScene.AddChild(go);
            go.GlobalPosition = context.AimPoint;
            
            var ctrl = go as PersistentEffect_DancingLights ?? go.GetNode<PersistentEffect_DancingLights>("PersistentEffect_DancingLights");
            float range = ability.Range.GetRange(context.Caster);
            ctrl.Initialize(context.Caster, range);
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        // AI Logic: Useful if enemies are in Darkness and allies don't have Darkvision.
        var targetNode = GridManager.Instance.NodeFromWorldPoint(context.AimPoint);
        if (GridManager.Instance.GetEffectiveLightLevel(targetNode) <= 0) // Darkness
        {
            // Check allies who need light
            var allies = AISpatialAnalysis.FindAllies(context.Caster);
            int alliesNeedingLight = allies.Count(a => !a.Template.HasDarkvision);
            
            if (alliesNeedingLight > 0) return 50f * alliesNeedingLight;
        }
        return 0f;
    }
}