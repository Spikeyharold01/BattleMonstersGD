using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class Effect_MoveOwnedZone : AbilityEffectComponent
{
    [Export] public string ZoneGroup = "Whirlpool"; // Or "FlamingSphere"
    [Export] public float MaxDistance = 30f;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        // Find zone owned by caster
        var zones = context.Caster.GetTree().GetNodesInGroup(ZoneGroup);
        foreach(Node n in zones)
        {
            if (n is PersistentEffect_Whirlpool pool) // Or Generic Interface
            {
                // Check Ownership (needs field in Whirlpool script, added in Initialize)
                // Assuming we added 'Caster' field in Initialize above.
                // pool.MoveWhirlpool(context.AimPoint);
                
                // Distance clamp
                Vector3 current = ((Node3D)n).GlobalPosition;
                float dist = current.DistanceTo(context.AimPoint);
                if (dist > MaxDistance)
                {
                    Vector3 dir = (context.AimPoint - current).Normalized();
                    pool.MoveWhirlpool(current + dir * MaxDistance);
                }
                else
                {
                    pool.MoveWhirlpool(context.AimPoint);
                }
            }
        }
    }
    
    public override float GetAIEstimatedValue(EffectContext context)
    {
        return 50f; // Moving hazards onto enemies is good
    }
}