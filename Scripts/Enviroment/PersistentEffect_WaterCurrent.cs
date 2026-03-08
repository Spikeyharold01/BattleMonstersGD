using Godot;
using System.Linq;
// =================================================================================================
// FILE: PersistentEffect_WaterCurrent.cs (GODOT VERSION)
// PURPOSE: Manages a fast-moving water hazard that can sweep creatures away.
// ATTACH TO: An Area3D defining the current's area (Child Node).
// =================================================================================================
public partial class PersistentEffect_WaterCurrent : GridNode
{
[ExportGroup("Current Properties")]
[Export]
[Tooltip("The direction the water is flowing.")]
public Vector3 CurrentDirection = Vector3.Forward; // Godot Forward is (0,0,-1) usually, but Z+ is Back. Adjust in Inspector.

[Export]
[Tooltip("The speed of the current in feet per round.")]
public float CurrentSpeed = 60f;

[Export]
[Tooltip("Is the riverbed rocky, causing lethal damage instead of nonlethal?")]
public bool IsRocky = false;

private StatusEffect_SO carriedEffect;

// In Godot, to check bounds, we usually use an Area3D. 
// This script should be attached to an Area3D or have access to one.
// Assuming attached to Area3D.
private Area3D area;

public override void _Ready()
{
	AddToGroup("PersistentEffect_WaterCurrent");
    area = GetParent() as Area3D;
    if (area == null)
    {
        GD.PrintErr("PersistentEffect_WaterCurrent must be a child of an Area3D!");
    }
    
    if (ResourceLoader.Exists("res://Data/StatusEffects/SE_CarriedByCurrent.tres"))
        carriedEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/SE_CarriedByCurrent.tres");
}

// Called by TurnManager or EnvironmentManager at the start of a creature's turn
public void ApplyTurnStartEffects(CreatureStats creature)
{
    if (area == null) return;

    // Check intersection using Area3D overlaps
    if (!area.OverlapsBody(creature)) // CreatureStats is CharacterBody3D
    {
        creature.MyEffects.RemoveEffect("Carried by Current");
        return;
    }

    float requiredDepth = creature.Template.VerticalReach / 3f;
    GridNode creatureNode = GridManager.Instance.NodeFromWorldPoint(creature.GlobalPosition);
    float currentDepth = (creatureNode.waterDepth + 1) * 5f; 

    if (currentDepth < requiredDepth)
    {
        GD.Print($"{creature.Name} is large enough to stand firm in the shallow current.");
        creature.MyEffects.RemoveEffect("Carried by Current");
        return;
    }

    int swimCheck = Dice.Roll(1, 20) + creature.GetSkillBonus(SkillType.Swim);
    if (swimCheck < 15)
    {
        GD.PrintRich($"[color=orange]{creature.Name} fails DC 15 Swim check ({swimCheck}) and is swept by the current![/color]");
        
        int damage = Dice.Roll(1, IsRocky ? 6 : 3);
        if(IsRocky) creature.TakeDamage(damage, "Bludgeoning");
        else creature.TakeNonlethalDamage(damage);

        if (!creature.MyEffects.HasEffect("Carried by Current") && carriedEffect != null)
        {
            creature.MyEffects.AddEffect((StatusEffect_SO)carriedEffect.Duplicate(), null);
        }
        
        // Apply Forced Movement
        // Movement in one round = Speed.
        // But we assume this is instantaneous displacement at start of turn.
        // Godot position update:
        creature.GlobalPosition += CurrentDirection.Normalized() * CurrentSpeed * 0.1f; // Arbitrary 0.1 scaling or 5ft/square logic?
        // Original Unity code: currentSpeed * (6f / 60f) -> speed * 0.1. Correct.
    }
    else
    {
        GD.Print($"{creature.Name} succeeds on DC 15 Swim check ({swimCheck}) and holds their position.");
        creature.MyEffects.RemoveEffect("Carried by Current");
    }
}
}