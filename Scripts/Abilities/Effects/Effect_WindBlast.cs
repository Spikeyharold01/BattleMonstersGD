using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_WindBlast.cs (GODOT VERSION)
// PURPOSE: Handles Aerial Servant's Wind Blast (Damage + Push + Prone).
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_WindBlast : AbilityEffectComponent
{
[ExportGroup("Damage")]
[Export] public int DiceCount = 4;
[Export] public int DieSides = 8;
[Export] public string DamageType = "Bludgeoning";

[ExportGroup("Knockback")]
[Export] public int PushDiceCount = 2;
[Export] public int PushDieSides = 10;

[ExportGroup("Status")]
[Export] public StatusEffect_SO ProneEffect;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    var caster = context.Caster;
    
    foreach (var target in context.AllTargetsInAoE)
    {
        if (target == caster) continue;

        bool saved = targetSaveResults.ContainsKey(target) && targetSaveResults[target];
        
        int damage = Dice.Roll(DiceCount, DieSides);
        if (saved) damage /= 2;

        target.TakeDamage(damage, DamageType, caster);
        GD.Print($"Wind Blast hits {target.Name} for {damage}.");

        if (!saved)
        {
            if ((int)target.Template.Size <= (int)caster.Template.Size)
            {
                int pushDist = Dice.Roll(PushDiceCount, PushDieSides);
                
                Vector3 pushDir = (target.GlobalPosition - caster.GlobalPosition).Normalized();
                
                var spaceState = caster.GetParent<Node3D>().GetWorld3D().DirectSpaceState;
                var query = PhysicsRayQueryParameters3D.Create(target.GlobalPosition, target.GlobalPosition + pushDir * pushDist, 1); // Mask 1 Walls
                var result = spaceState.IntersectRay(query);
                
                Vector3 dest = target.GlobalPosition + (pushDir * pushDist);

                if (result.Count > 0)
                {
                    Vector3 hitPoint = (Vector3)result["position"];
                    dest = hitPoint - (pushDir * 2f); 
                }
                target.GlobalPosition = dest;
                GD.Print($"{target.Name} is blown back {pushDist}ft.");

                if (ProneEffect != null)
                {
                    var instance = (StatusEffect_SO)ProneEffect.Duplicate();
                    target.MyEffects.AddEffect(instance, caster);
                }
            }
            else
            {
                GD.Print($"{target.Name} is too large to be blown away.");
            }
        }
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    return 80f * context.AllTargetsInAoE.Count;
}
}
