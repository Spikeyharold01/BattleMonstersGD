using Godot;
using System.Linq;
using System.Threading.Tasks;
// =================================================================================================
// FILE: AIAction_Sunder.cs (GODOT VERSION)
// PURPOSE: Represents the Sunder combat maneuver action.
// ATTACH TO: Do not attach (Pure C# Class).
// =================================================================================================
public class AIAction_Sunder : AIAction
{
private CreatureStats target;
private ItemInstance itemToSunder;

public AIAction_Sunder(AIController controller, CreatureStats target) : base(controller)
{
    this.target = target;
    var targetInv = target.GetNodeOrNull<InventoryController>("InventoryController");
    this.itemToSunder = targetInv?.GetEquippedItemInstance(EquipmentSlot.MainHand) ?? targetInv?.GetEquippedItemInstance(EquipmentSlot.Shield);
    
    if (itemToSunder != null)
    {
        Name = $"Sunder {target.Name}'s {itemToSunder.ItemData.ItemName}";
    }
    else
    {
        Name = "Sunder (invalid target)";
    }
}

public override void CalculateScore()
{
    if (itemToSunder == null)
    {
        Score = -1f;
        return;
    }

    float score = 40f;
    
    if (target == controller.GetPerceivedHighestThreat())
    {
        score += 50f;
    }

    // More valuable if the item is magical (has an enhancement bonus).
    int enhancement = itemToSunder.ItemData.Modifications.FirstOrDefault(m => m.BonusType == BonusType.Enhancement)?.ModifierValue ?? 0;
    if (enhancement > 0)
    {
        score += enhancement * 25f;
    }

    int cmb = controller.MyStats.GetCMB(ManeuverType.Sunder);
    if (controller.MyStats.HasFeat("Improved Sunder")) cmb += 2;
    if (controller.MyStats.HasFeat("Greater Sunder")) cmb += 2;
    int targetCMD = target.GetCMD();
    
    // Note: Improved Sunder on defender adds to CMD vs Sunder.
    if (target.HasFeat("Improved Sunder")) targetCMD += 2;

    float successChance = Mathf.Clamp((10.5f + cmb - targetCMD) / 20f, 0f, 1f);
    if (successChance < 0.3f)
    {
        Score = -1f; 
        return;
    }

    Score = score * successChance;
}

public override async Task Execute()
{
    controller.MyActionManager.UseAction(ActionType.Standard);
    await CombatManeuvers.ResolveSunderCoroutine(controller.MyStats, target);
    // Note: ResolveSunderCoroutine is async in CombatManeuvers.cs
}
}
// Depend