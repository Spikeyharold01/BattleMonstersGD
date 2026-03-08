using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_SwarmDamage.cs (GODOT VERSION)
// PURPOSE: Handles swarm auto-damage logic.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_SwarmDamage : AbilityEffectComponent
{
[ExportGroup("Swarm Stats")]
[Export] public int DiceCount = 3;
[Export] public int DieSides = 6;

[ExportGroup("Linked Effects")]
[Export] public StatusEffect_SO DistractionEffect; 
[Export] public StatusEffect_SO PoisonEffect; 

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    var swarm = context.Caster;
    
    var nodes = GridManager.Instance.GetNodesOccupiedByCreature(swarm);
    var victims = new HashSet<CreatureStats>();

    foreach(var node in nodes)
    {
        // Replicating OverlapSphere
        var spaceState = swarm.GetParent<Node3D>().GetWorld3D().DirectSpaceState;
        var shape = new SphereShape3D { Radius = GridManager.Instance.nodeRadius };
        var query = new PhysicsShapeQueryParameters3D 
        { 
            Shape = shape, 
            Transform = new Transform3D(Basis.Identity, node.worldPosition), 
            CollisionMask = 2 // Creature layer
        };
        
        var hits = spaceState.IntersectShape(query);
        foreach(var dict in hits)
        {
            var hitNode = (Node3D)dict["collider"];
            var c = hitNode as CreatureStats ?? hitNode.GetNodeOrNull<CreatureStats>("CreatureStats");
            if (c != null && c != swarm) victims.Add(c);
        }
    }

    foreach (var victim in victims)
    {
        // 1. Deal Damage (Automatic)
        int dmg = Dice.Roll(DiceCount, DieSides);
        victim.TakeDamage(dmg, "Physical", swarm);
        GD.Print($"Swarm deals {dmg} automatic damage to {victim.Name}.");

        // 2. Distraction (Save vs Nauseated)
        if (DistractionEffect != null)
        {
            // DC 10 + 1/2 HD + Con
            int dc = 10 + (swarm.Template.CasterLevel / 2) + swarm.ConModifier;
            int save = Dice.Roll(1, 20) + victim.GetFortitudeSave(swarm);
            if (save < dc)
            {
                GD.Print($"{victim.Name} fails Distraction save! Nauseated.");
                var instance = (StatusEffect_SO)DistractionEffect.Duplicate();
                instance.DurationInRounds = 1;
                victim.MyEffects.AddEffect(instance, swarm);
            }
        }

        // 3. Poison (Save vs Dex Damage)
        if (PoisonEffect != null)
        {
            var pInstance = (StatusEffect_SO)PoisonEffect.Duplicate();
            victim.MyEffects.AddEffect(pInstance, swarm);
        }
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    return 50f; 
}
}