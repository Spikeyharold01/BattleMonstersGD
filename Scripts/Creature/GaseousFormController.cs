using Godot;
using System;
// =================================================================================================
// FILE: GaseousFormController.cs (GODOT VERSION)
// PURPOSE: Enforces Gaseous Form rules (No Attacks, Flight, Pass Walls).
// ATTACH TO: Creature Scenes via AddChild in Effect logic (or manually).
// =================================================================================================
public partial class GaseousFormController : Godot.Node
{
private CreatureStats myStats;
private ActionManager actionManager;
private float originalFlySpeed;

[Export] public bool IsWindWalk = false;
[Export] public bool UseNormalSpeedInGaseousForm = false;
[Export] public bool IsMythic = false;
[Export] public bool CanShiftAsMoveAction = false;

// Wind Walk State properties accessed by CreatureMover
public bool InFastWindMode => IsWindWalk && inCloudForm && !isTransforming && myStats.Template.Speed_Fly > 10f;

// Wind Walk State
private bool inCloudForm = true; // Start as cloud
private bool isTransforming = false;
private int transformationRoundsLeft = 0;

public override void _Ready()
{
    myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
    actionManager = GetParent().GetNodeOrNull<ActionManager>("ActionManager");

    if (myStats != null)
    {
        // Apply Speed Override
        // Wind Walk: 10ft (Perfect) or 600ft (Poor)
        // Gaseous: 10ft (Perfect) or 30ft (Mythic)
        originalFlySpeed = myStats.Template.Speed_Fly;
        
		if (IsWindWalk)
        {
            myStats.Template.Speed_Fly = 10f; // Start slow
        }
        else if (UseNormalSpeedInGaseousForm)
        {
            myStats.Template.Speed_Fly = GetNormalMovementSpeed();
        }
        else
        {
            myStats.Template.Speed_Fly = IsMythic ? 30f : 10f;
        }
		
        myStats.Template.FlyManeuverability = FlyManeuverability.Perfect;
    }
}

private float GetNormalMovementSpeed()
{
    if (myStats?.Template == null)
    {
        return 10f;
    }

    float landSpeed = myStats.Template.Speed_Land;
    float flySpeed = myStats.Template.Speed_Fly;
    float fallback = IsMythic ? 30f : 10f;

    float normalSpeed = Mathf.Max(landSpeed, flySpeed);
    return normalSpeed > 0f ? normalSpeed : fallback;
}

public override void _ExitTree()
{
    if (myStats != null)
    {
        myStats.Template.Speed_Fly = originalFlySpeed; // Restore
    }
}

// Called manually by ActionManager/TurnManager or via Signal
// In Godot setup, ActionManager calls components directly if implemented there, 
// OR we hook a signal if TurnManager exposes one.
// Given previous pattern: We assume a direct call or manual hook. 
// Since TurnManager.cs in context does NOT emit a signal for individual creature turn starts directly to components,
// and ActionManager.OnTurnStart does not call GaseousFormController explicitly yet, we must update ActionManager.
// However, I will expose the method to be called.
public void OnTurnStart()
{
    if (isTransforming)
    {
        transformationRoundsLeft--;
        GD.Print($"{GetParent().Name} transforming... {transformationRoundsLeft} rounds left.");
        if (transformationRoundsLeft <= 0)
        {
            isTransforming = false;
            inCloudForm = !inCloudForm; // Toggle
            
            if (inCloudForm)
            {
                myStats.Template.Speed_Fly = 600f; // Fast Wind
                myStats.Template.FlyManeuverability = FlyManeuverability.Poor;
                GD.Print($"{GetParent().Name} is now in Fast Wind form (600ft).");
            }
            else
            {
                myStats.Template.Speed_Fly = 10f; // Slow/Solid-ish
                myStats.Template.FlyManeuverability = FlyManeuverability.Perfect;
                GD.Print($"{GetParent().Name} is now in Solid/Slow form.");
            }
        }
    }
}

// AI/Player Hook: Call this to start switching form
public void ToggleWindWalkSpeed()
{
    if (!IsWindWalk || isTransforming) return;
    isTransforming = true;
    transformationRoundsLeft = 5;
    GD.Print($"{GetParent().Name} begins transforming Wind Walk form (5 rounds)...");
}

// Hook for ActionManager (Already implemented in ActionManager.cs from previous context check)
public bool IsActionRestricted(Ability_SO ability)
{
    if (ability == null) return true;

    if (ability.ActionCost == ActionType.Move && CanShiftAsMoveAction)
    {
        return false;
    }

    // "It can’t attack or cast spells with verbal, somatic, material, or focus components"
    if (ability.Category == AbilityCategory.SpecialAttack) return true;
    if (ability.Category == AbilityCategory.Spell)
    {
        bool silent = myStats.HasFeat("Silent Spell");
        bool still = myStats.HasFeat("Still Spell");
        bool eschew = myStats.HasFeat("Eschew Materials");

        var components = ability.Components;
        if (components == null) return false;

        if (components.HasVerbal && !silent) return true;
        if (components.HasSomatic && !still) return true;
        if (components.HasFocus) return true;
        if (components.HasMaterial && !eschew) return true;
        if (components.HasDivineFocus) return true;

        return false;
    }
    
    // Wind Walk Logic
    if (IsWindWalk && !inCloudForm && !isTransforming) return false;

    return true; // Restricted
}
}
// Dependenc