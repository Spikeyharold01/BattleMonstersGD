using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: PersistentEffect_DancingLights.cs (GODOT VERSION)
// PURPOSE: Manages the 4 lights of a Dancing Lights spell.
// ATTACH TO: Dancing Lights Parent Prefab (Node3D).
// =================================================================================================
public partial class PersistentEffect_DancingLights : Node3D
{
    [Export] public PackedScene SingleLightPrefab; // The individual wisp
    [Export] public int NumberOfLights = 4;
    [Export] public float RadiusConstraint = 10f; // Must stay within 10ft of center
    [Export] public float MoveSpeed = 100f; // Per round
	[Export] public bool IsHumanoidShape = false;
    [Export] public PackedScene HumanoidDecoyPrefab; // Prefab with a dummy CreatureStats or Distraction tag
    
    private CreatureStats caster;
    private float maxRangeFromCaster;
    private float duration = 60f; // 1 minute fixed
    
    private List<Node3D> lights = new List<Node3D>();

public void Initialize(CreatureStats source, float range)
    {
        caster = source;
        maxRangeFromCaster = range;
        AddToGroup("DancingLights");
        
        if (IsHumanoidShape && HumanoidDecoyPrefab != null)
        {
            var decoy = HumanoidDecoyPrefab.Instantiate<Node3D>();
            AddChild(decoy);
            decoy.Position = Vector3.Zero;
        }
        else
        {
            // Spawn lights
            for (int i = 0; i < NumberOfLights; i++)
            {
                if (SingleLightPrefab != null)
                {
                    var light = SingleLightPrefab.Instantiate<Node3D>();
                    AddChild(light); 
                    Vector3 offset = new Vector3(GD.Randf(), GD.Randf(), GD.Randf()).Normalized() * (RadiusConstraint * 0.5f);
                    light.Position = offset;
                    lights.Add(light);
                }
            }
        }
    }
    public override void _Process(double delta)
    {
        duration -= (float)delta;
        
        // Range Check (Wink out if too far)
        float dist = GlobalPosition.DistanceTo(caster.GlobalPosition);
        if (dist > maxRangeFromCaster || duration <= 0)
        {
            QueueFree();
        }
    }

    // Called by AI/Player Action
    public void MoveTo(Vector3 targetPos)
    {
        // Simply move the parent controller. The children move with it.
        // We could tween this for visuals, but instant for turn logic.
        
        // Check range constraint
        float distFromCaster = targetPos.DistanceTo(caster.GlobalPosition);
        if (distFromCaster > maxRangeFromCaster)
        {
            Vector3 dir = (targetPos - caster.GlobalPosition).Normalized();
            targetPos = caster.GlobalPosition + dir * maxRangeFromCaster;
        }

        GlobalPosition = targetPos;
        GD.Print($"{caster.Name} moves the Dancing Lights.");
    }
}