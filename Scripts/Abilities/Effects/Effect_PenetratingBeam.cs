using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_PenetratingBeam.cs (GODOT VERSION)
// PURPOSE: Handles Lightning Bolt logic. Deals damage to creatures in a line, damages objects, 
//          ignites combustibles, and stops at barriers unless the barrier is shattered.
// =================================================================================================
[GlobalClass]
public partial class Effect_PenetratingBeam : AbilityEffectComponent
{
    [ExportGroup("Beam Damage")]
    [Export] public DamageInfo Damage;
    
    [Export] 
    [Tooltip("Maximum number of dice the spell can roll (e.g. 10 for Lightning Bolt).")]
    public int MaxDiceCap = 10;

    [ExportGroup("Object Interaction")]
    [Export] 
    [Tooltip("If true, the beam will ignite any flammable objects it strikes.")]
    public bool SetsCombustiblesOnFire = true;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (Damage == null || context.Caster == null) return;

        // 1. Calculate Damage Dice
        int cl = context.Caster.Template.CasterLevel;
        int dice = Mathf.Min(cl, MaxDiceCap);
        
        // Mythic Lightning Bolt doubles the damage cap
        if (context.IsMythicCast) 
        {
            dice = Mathf.Min(cl, MaxDiceCap * 2);
        }

        // 2. Physics Raycast to find stopping distance
        Vector3 origin = context.Caster.GlobalPosition + Vector3.Up * 1.0f; // Shoot from chest height
        Vector3 dir = (context.AimPoint - context.Caster.GlobalPosition).Normalized();
        if (dir == Vector3.Zero) dir = -context.Caster.GlobalTransform.Basis.Z; // Default Forward

        float maxRange = ability.AreaOfEffect.Range > 0 ? ability.AreaOfEffect.Range : ability.Range.GetRange(context.Caster);
        float stopDistance = maxRange;

        var spaceState = context.Caster.GetWorld3D().DirectSpaceState;
        float currentTravel = 0f;
        Vector3 currentOrigin = origin;

        int safetyBreaker = 0;
        while (currentTravel < maxRange && safetyBreaker < 10)
        {
            safetyBreaker++;
            float remainingRange = maxRange - currentTravel;

            // We ONLY check for Walls (Layer 1) and Objects (Layer 4). We let the beam pass directly through Creatures (Layer 2).
            var query = PhysicsRayQueryParameters3D.Create(currentOrigin, currentOrigin + dir * remainingRange);
            query.CollisionMask = 1 | 4; 
            
            var result = spaceState.IntersectRay(query);
            if (result.Count == 0) break; // Path is completely clear to the end of range.

            Vector3 hitPoint = (Vector3)result["position"];
            Node3D collider = (Node3D)result["collider"];
            float distanceToHit = currentOrigin.DistanceTo(hitPoint);
            currentTravel += distanceToHit;

            var obj = collider as ObjectDurability ?? collider.GetNodeOrNull<ObjectDurability>("ObjectDurability");
            if (obj != null)
            {
                int objDamage = Dice.Roll(dice, Damage.DieSides) + Damage.FlatBonus;
                obj.TakeDamage(objDamage, Damage.DamageType);

                // Check for ignition
                if (SetsCombustiblesOnFire && obj.IsFlammable && obj.GetNodeOrNull<BurningObjectController>("BurningObjectController") == null)
                {
                    var fire = new BurningObjectController();
                    fire.Name = "BurningObjectController";
                    obj.AddChild(fire);
                    GD.Print($"{obj.Name} was ignited by the lightning bolt!");
                }

                // If destroyed, move the origin past the object and continue the loop
                if (obj.CurrentHP <= 0 || obj.IsQueuedForDeletion())
                {
                    GD.Print($"The lightning bolt shatters {obj.Name} and continues!");
                    currentOrigin = hitPoint + (dir * 0.1f);
                    continue;
                }
            }

            // If we hit an indestructible wall, or an object that survived the damage, the beam stops here.
            stopDistance = currentTravel;
            GD.Print($"The lightning bolt is stopped by a barrier at {stopDistance:F1} feet.");
            break;
        }

        // 3. Damage Creatures
        // CombatMagic has already filtered targets by SR, Sanctuary, and the 5ft line geometry!
        foreach (var target in context.AllTargetsInAoE)
        {
            if (target == null || target == context.Caster) continue;
            
            // Validate they aren't hiding behind a wall that stopped the beam
            float targetDist = context.Caster.GlobalPosition.DistanceTo(target.GlobalPosition);
            if (targetDist > stopDistance) continue;

            bool saved = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
            
            int totalDamage = Dice.Roll(dice, Damage.DieSides) + Damage.FlatBonus;
            
            // Mythic Lightning Bolt bypasses Electricity Resistance on a failed save
            bool bypassResistance = context.IsMythicCast && !saved;

            if (saved && ability.SavingThrow.EffectOnSuccess == SaveEffect.HalfDamage) 
            {
                totalDamage /= 2;
            }

            if (totalDamage > 0)
            {
                GD.Print($"{ability.AbilityName} strikes {target.Name} for {totalDamage} {Damage.DamageType} damage! (Save: {saved})");
                target.TakeDamage(totalDamage, Damage.DamageType, context.Caster, null, null, null, bypassResistance);
            }
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (Damage == null || context.Caster == null) return 0f;

        float score = 0f;
        int cl = context.Caster.Template.CasterLevel;
        int dice = Mathf.Min(cl, MaxDiceCap);
        if (context.IsMythicCast) dice = Mathf.Min(cl, MaxDiceCap * 2);

        float avgDamage = (dice * (Damage.DieSides / 2f + 0.5f)) + Damage.FlatBonus;

        foreach (var target in context.AllTargetsInAoE)
        {
            if (target == context.Caster) continue;

            if (target.IsInGroup("Player") != context.Caster.IsInGroup("Player"))
            {
                // Enemy
                float multiplier = context.Caster.GetNodeOrNull<AIController>("AIController")?.GetPredictedDamageMultiplier(target, Damage.DamageType) ?? 1.0f;
                score += avgDamage * multiplier;
            }
            else
            {
                // Ally hit by the beam
                score -= avgDamage * 1.5f; 
            }
        }

        return score;
    }
}