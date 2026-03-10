using Godot;
using System.Linq;
// =================================================================================================
// FILE: PassiveAdhesiveController.cs (GODOT VERSION)
// PURPOSE: Handles the Adherer's "Adhesive" ability.
// - Disarms weapons on hit (Ref save).
// - Grapples unarmed attackers (Ref save?). No, auto-attempt.
// - Suppressed by Fire damage.
// ATTACH TO: Adherer Scene (Child of CreatureStats or Root).
// =================================================================================================
public partial class PassiveAdhesiveController : Node
{
private CreatureStats myStats;
public bool IsAdhesiveActive { get; private set; } = true;
private int suppressionTimer = 0;


[ExportGroup("Settings")]
[Export] public int StickSaveDC = 14;
[Export] public int RetrieveStrengthDC = 17;
[Export] public int GrappleBonus = 8; // Used by CreatureStats.GetCMB(Grapple)

public override void _Ready()
{
    myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
    
    // Connect Signals
    if (myStats != null)
    {
        myStats.OnTakeDamageDetailed += HandleIncomingDamage;
    }
    

}

public override void _ExitTree()
{
    if (myStats != null)
    {
        myStats.OnTakeDamageDetailed -= HandleIncomingDamage;
    }
}

// This method needs to be called. See "Required Changes" below.
public void OnTurnStart()
{
    if (suppressionTimer > 0)
    {
        suppressionTimer--;
        if (suppressionTimer <= 0)
        {
            IsAdhesiveActive = true;
            GD.Print($"{myStats.Name}'s adhesive coating regenerates.");
        }
    }
}

private void HandleIncomingDamage(int damage, string type, CreatureStats attacker, Item_SO weapon, NaturalAttack natural)
{
    // 1. Check Fire Suppression
    if (type == "Fire" && damage >= 10)
    {
        int rounds = Dice.Roll(1, 4);
        suppressionTimer = rounds;
        IsAdhesiveActive = false;
        GD.PrintRich($"[color=orange]{myStats.Name}'s adhesive is burned away for {rounds} rounds![/color]");
        return; 
    }

    if (!IsAdhesiveActive || attacker == null) return;

    // 2. Weapon Hit -> Try to Stick (Disarm)
    if (weapon != null && weapon.WeaponType == WeaponType.Melee) 
    {
        // Assuming "IsNatural" isn't a prop on Item_SO, relying on logic: Weapons are items.
        // Natural attacks come in as `natural` param, so weapon != null implies manufactured.
        
        int reflex = Dice.Roll(1, 20) + attacker.GetReflexSave(myStats);
        if (reflex < StickSaveDC)
        {
            GD.PrintRich($"[color=red]{attacker.Name} fails Reflex save ({reflex} vs {StickSaveDC})! Weapon sticks to {myStats.Name}.</color>");
            
            // Force Disarm
            var attackerInv = attacker.GetNodeOrNull<InventoryController>("InventoryController");
            if (attackerInv != null)
            {
                // Drop the item at Adherer's location
                var body = GetParent<Node3D>();
                attackerInv.DropItemFromSlot(weapon.EquipSlot, body.GlobalPosition);
                
                // Find the WorldItem that was just spawned and parent it to the Adherer
                var droppedItem = ItemManager.Instance.GetItemsInRadius(body.GlobalPosition, 0.5f)
                    .FirstOrDefault(i => i.ItemData == weapon);
                
                if (droppedItem != null)
                {
                    // In Godot, Reparent(newParent) keeps global transform by default in 4.0? 
                    // Node3D logic: AddChild changes parent. 
                    droppedItem.GetParent()?.RemoveChild(droppedItem);
                    body.AddChild(droppedItem);
                    
                    droppedItem.Position = new Vector3(0, 1f, 0.5f); // Visually stick it to chest (Local)
                    
                    // Disable collision if needed
                    var col = droppedItem.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
                    if (col != null) col.Disabled = true;
                }
            }
        }
    }

    // 3. Natural/Unarmed Hit -> Free Grapple
    // If weapon is null (Unarmed or Natural)
    if (weapon == null)
    {
        GD.PrintRich($"[color=red]{attacker.Name} hits adhesive skin! {myStats.Name} attempts to grapple back.</color>");
        // Trigger Grapple (Free Action, No AoO)
       CombatManeuvers.ResolveGrapple(myStats, attacker, isFreeAction: true, initiatingAttack: null);
    }
}

// Called by external "Solvent" item logic
public void ApplySolvent(CreatureStats user)
{
    int save = Dice.Roll(1, 20) + myStats.GetReflexSave(user);
    if (save < 15)
    {
        suppressionTimer = 600; // 1 hour (600 rounds)
        GD.Print("Solvent dissolves adhesive for 1 hour!");
    }
    else
    {
        suppressionTimer = Dice.Roll(1, 4);
        GD.Print("Solvent partially works (1d4 rounds).");
    }
    IsAdhesiveActive = false;
}
}