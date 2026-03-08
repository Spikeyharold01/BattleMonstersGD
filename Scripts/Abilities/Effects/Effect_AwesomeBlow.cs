using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_AwesomeBlow.cs (GODOT VERSION)
// PURPOSE: An effect component for the Awesome Blow combat maneuver feat.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_AwesomeBlow : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
CombatManager.ResolveAwesomeBlow(context.Caster, context.PrimaryTarget);
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var caster = context.Caster;
    var target = context.PrimaryTarget;
    if (target == null) return 0f;

    // AI Rule: Target must be smaller.
    if (target.Template.Size >= caster.Template.Size) return 0f;
    
    float score = 60f; // Base value for damage + prone.

    // AI is smart: It looks for opportunities to knock enemies into things or other enemies.
    Vector3 casterPos = caster.GetParent<Node3D>().GlobalPosition;
    Vector3 targetPos = target.GlobalPosition;
    Vector3 direction = (targetPos - casterPos).Normalized();
    
    var spaceState = caster.GetParent<Node3D>().GetWorld3D().DirectSpaceState;
    
    // Raycast 10ft behind target
    // Mask 1 (Walls) + 2 (Creatures) assumed
    var query = PhysicsRayQueryParameters3D.Create(targetPos, targetPos + direction * 10f, 3);
    
    // Exclude the target itself from the raycast check so it doesn't hit itself immediately
    query.Exclude = new Godot.Collections.Array<Rid> { target.GetRid() };

    var result = spaceState.IntersectRay(query);
    
    if (result.Count > 0)
    {
        var hitCollider = (Node3D)result["collider"];
        
        // Check for ObjectDurability group/type
        // If ObjectDurability is attached to the collider node or parent
        if (hitCollider.IsInGroup("ObjectDurability") || hitCollider is ObjectDurability || hitCollider.GetNodeOrNull<ObjectDurability>("ObjectDurability") != null)
        {
            score += 75f; 
        }
        else if ((hitCollider is CreatureStats || hitCollider.GetNodeOrNull<CreatureStats>("CreatureStats") != null) && hitCollider != caster)
        {
            score += 85f; 
        }
    }
    
    return score;
}
}