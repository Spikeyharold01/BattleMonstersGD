using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: Effect_CatchFloatingCreature.cs (GODOT VERSION)
// PURPOSE: Logic for catching a creature swept by a current.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_CatchFloatingCreature : AbilityEffectComponent
{
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
var caster = context.Caster;
var target = context.PrimaryTarget;

if (caster == null || target == null || !target.MyEffects.HasEffect("Carried by Current"))
    {
        GD.Print("Catch attempt fails: Target is not being carried by a current.");
        return;
    }

    // Find current. Assuming only one relevant current nearby or checking collision?
    // Original logic used FindObjectOfType, implying global search.
    // We will assume "PersistentEffect_WaterCurrent" nodes are in the scene.
    var currents = caster.GetTree().GetNodesInGroup("PersistentEffect_WaterCurrent"); // Assuming group
    // If not grouped, manual search
    if (currents.Count == 0)
    {
         // Try manual search if group fails
         // This is expensive in Godot without groups. Assuming PersistentEffect_WaterCurrent script handles grouping if used here.
         // Or rely on passing current via context? No context.
         return;
    }
    
    // Find the current affecting the target
    // We'll iterate and check overlaps or just take first for simplicity if only one river.
    var current = currents[0] as PersistentEffect_WaterCurrent; 
    
    if (current == null) return;

    int dc = 15 + Mathf.FloorToInt(current.CurrentSpeed / 10f);
    if (target.MyEffects.HasCondition(Condition.Helpless)) dc += 10;
    
    int strengthRoll = Dice.Roll(1, 20) + caster.StrModifier;

    GD.Print($"{caster.Name} attempts to catch {target.Name}. Strength Check: {strengthRoll} vs DC {dc}.");
    
    if (strengthRoll >= dc)
    {
        GD.PrintRich($"<color=green>Success! {caster.Name} pulls {target.Name} from the current!</color>");
        target.MyEffects.RemoveEffect("Carried by Current");
        
        var casterNode = GridManager.Instance.NodeFromWorldPoint(caster.GlobalPosition);
        var safeNode = GridManager.Instance.GetNeighbours(casterNode).FirstOrDefault(n => n.terrainType == TerrainType.Ground);
        if (safeNode != null) target.GlobalPosition = safeNode.worldPosition;
    }
    else
    {
        if (dc - strengthRoll >= 5)
        {
            GD.PrintRich($"<color=red>Critical Failure! {caster.Name} is pulled into the water!</color>");
            int reflexDC = dc + 5;
            int reflexRoll = Dice.Roll(1, 20) + caster.GetReflexSave(null);
            if (reflexRoll < reflexDC)
            {
                caster.GlobalPosition = target.GlobalPosition; 
            }
        }
        else
        {
            GD.PrintRich("<color=orange>Failure. The attempt fails.</color>");
        }
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    var target = context.PrimaryTarget;
    if (target == null || !target.MyEffects.HasEffect("Carried by Current")) return 0f;

    // AI Logic: Saving an ally is a very high priority action.
    return 350f;
}
}