using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: Effect_PhysicsWind.cs (GODOT VERSION)
// PURPOSE: Applies wind physics based on creature size (Gust of Wind logic).
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_PhysicsWind : AbilityEffectComponent
{
    [Export] public StatusEffect_SO ProneEffect;
    
    // Gust of Wind Defs:
    // Tiny or smaller (Flying): Blown Back 2d6x10, 2d6 Dmg. DC 25 Fly.
    // Tiny or smaller (Ground): Knocked Down, Rolled 1d4x10, 1d4 Nonlethal per 10ft.
    // Small (Ground): Prone.
    // Medium (Ground): Checked (handled by Zone).
    
    // We can expose these if we want a generic "Wind Blast" component, or hardcode for Gust of Wind.
    // Let's expose the basics.
    
    [Export] public CreatureSize BlownAwaySizeLimit = CreatureSize.Tiny;
    [Export] public CreatureSize ProneSizeLimit = CreatureSize.Small;
    
    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        Vector3 windDir = (context.AimPoint - context.Caster.GlobalPosition).Normalized();
		 if (windDir == Vector3.Zero)
        {
            windDir = context.Caster.GlobalBasis.Z;
        }

        float maxSpellDistance = ability?.Range?.GetRange(context.Caster) ?? 0f;
        // Area is Line? If so, wind dir matches line.
        // Assuming context.AllTargetsInAoE contains everyone in the line.

        foreach (var target in context.AllTargetsInAoE)
        {
            if (targetSaveResults.ContainsKey(target) && targetSaveResults[target])
            {
                GD.Print($"{target.Name} saved against the wind.");
                continue;
            }

            GridNode targetNode = GridManager.Instance?.NodeFromWorldPoint(target.GlobalPosition);
            bool isFlying = target.Template.Speed_Fly > 0 &&
                            targetNode != null &&
                            targetNode.terrainType != TerrainType.Ground &&
                            targetNode.terrainType != TerrainType.Solid &&
                            targetNode.terrainType != TerrainType.Water;
            // Assuming CreatureStats has state, or we check Mover. For sim, Template is close enough or use Z check.
            
            // 1. BLOWN AWAY (Tiny or smaller)
            if (target.Template.Size <= BlownAwaySizeLimit)
            {
                if (isFlying)
                {
                    // DC 25 Fly
                    int flyCheck = Dice.Roll(1, 20) + target.GetSkillBonus(SkillType.Fly);
                    if (flyCheck < 25)
                    {
                        int distance = Dice.Roll(2, 6) * 10;
                        int dmg = Dice.Roll(2, 6);
                        ApplyPush(target, windDir, distance, context.Caster.GlobalPosition, maxSpellDistance);
                        target.TakeDamage(dmg, "Bludgeoning", context.Caster);
                        GD.Print($"{target.Name} is blown away {distance}ft!");
                    }
                }
                else // Ground
                {
                    // Rolled 1d4x10
                    int distRoll = Dice.Roll(1, 4);
                    int distance = distRoll * 10;
                    int dmg = Dice.Roll(distRoll, 4); // 1d4 per 10ft
                    ApplyPush(target, windDir, distance, context.Caster.GlobalPosition, maxSpellDistance);
                    target.TakeNonlethalDamage(dmg);
                    ApplyProne(target, context.Caster);
                }
            }
            // 2. KNOCKED DOWN (Small)
            else if (target.Template.Size <= ProneSizeLimit && !isFlying)
            {
                ApplyProne(target, context.Caster);
            }
        }
    }

    private void ApplyPush(CreatureStats target, Vector3 dir, float dist, Vector3 spellOrigin, float maxSpellDistance)
    {
		Vector3 desiredDestination = target.GlobalPosition + dir * dist;

        if (maxSpellDistance > 0f)
        {
            Vector3 fromOrigin = desiredDestination - spellOrigin;
            if (fromOrigin.Length() > maxSpellDistance)
            {
                desiredDestination = spellOrigin + fromOrigin.Normalized() * maxSpellDistance;
            }
        }
        // Simple translation, ideally Raycast for walls
        var spaceState = target.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(target.GlobalPosition, desiredDestination, 1); // Wall mask
        var result = spaceState.IntersectRay(query);
        
        if (result.Count > 0)
        {
            target.GlobalPosition = (Vector3)result["position"] - (dir * target.Template.Space/2f);
        }
        else
        {
            target.GlobalPosition = desiredDestination;
        }
		// Notify target that it was pushed by caster
        target.TriggerForcedMovement(caster);
    }

    private void ApplyProne(CreatureStats target, CreatureStats caster)
    {
        if (ProneEffect != null)
        {
            var instance = (StatusEffect_SO)ProneEffect.Duplicate();
            target.MyEffects.AddEffect(instance, caster);
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        return 40f;
    }
}