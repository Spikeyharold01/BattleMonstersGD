using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: CombatOptionsController.cs (GODOT VERSION)
// PURPOSE: Manages the state of toggleable combat option feats like Power Attack.
// ATTACH TO: All creature prefabs (Child Node).
// =================================================================================================
/// <summary>
/// This component tracks which "combat option" feats (like Power Attack or Combat Expertise)
/// are currently active for a creature on its turn.
/// </summary>
public partial class CombatOptionsController : Godot.Node
{
// A HashSet provides fast lookups to see which feats are currently toggled on.
private HashSet<Feat_SO> activeOptions = new HashSet<Feat_SO>();

/// <summary>
/// Activates a combat option feat. This is a free action.
/// </summary>
public void ActivateOption(Feat_SO feat)
{
    if (feat != null && feat.Type == FeatType.CombatOption_Toggle)
    {
        activeOptions.Add(feat);
        GD.Print($"{GetParent().Name} activated combat option: {feat.FeatName}");
    }
}

/// <summary>
/// Deactivates a combat option feat. This is a free action.
/// </summary>
public void DeactivateOption(Feat_SO feat)
{
    if (feat != null)
    {
        activeOptions.Remove(feat);
        GD.Print($"{GetParent().Name} deactivated combat option: {feat.FeatName}");
    }
}

/// <summary>
/// Checks if a specific combat option is currently active.
/// </summary>
public bool IsOptionActive(string featName)
{
    return activeOptions.Any(f => f.FeatName == featName);
}

 /// <summary>
 /// Gets all stat modifications from currently active combat option feats,
 /// calculating dynamic values based on the creature's stats.
 /// </summary>
public List<StatModification> GetActiveOptionModifications(CreatureStats creature)
{
    var allMods = new List<StatModification>();
    if (creature == null) return allMods;

    foreach (var feat in activeOptions)
    {
        // --- DYNAMIC CALCULATION FOR POWER ATTACK ---
        if (feat.FeatName == "Power Attack")
        {
            // Rule: Penalty is -1 for every 4 points of BAB.
            int penalty = -1 * (1 + Mathf.FloorToInt((creature.Template.BaseAttackBonus - 1) / 4f));
            // Rule: Damage bonus is +2 for every 4 points of BAB.
            int damageBonus = 2 * (1 + Mathf.FloorToInt((creature.Template.BaseAttackBonus - 1) / 4f));

            allMods.Add(new StatModification { 
                StatToModify = StatToModify.AttackRoll, 
                ModifierValue = penalty 
            });
            allMods.Add(new StatModification { 
                StatToModify = StatToModify.MeleeDamage, 
                ModifierValue = damageBonus 
            });
        }
        // --- DYNAMIC CALCULATION FOR DEADLY AIM (Ranged equivalent) ---
        else if (feat.FeatName == "Deadly Aim")
        {
            int penalty = -1 * (1 + Mathf.FloorToInt((creature.Template.BaseAttackBonus - 1) / 4f));
            int damageBonus = 2 * (1 + Mathf.FloorToInt((creature.Template.BaseAttackBonus - 1) / 4f));
            
            allMods.Add(new StatModification { 
                StatToModify = StatToModify.AttackRoll, 
                ModifierValue = penalty 
            });
            allMods.Add(new StatModification { 
                StatToModify = StatToModify.RangedDamage, 
                ModifierValue = damageBonus 
            });
        }
        else
        {
            // For other, non-scaling combat options, add their mods directly.
            // In Godot C#, we need to convert the Array to List or loop
            foreach(var mod in feat.Modifications)
            {
                allMods.Add(mod);
            }
        }
    }
    return allMods;
}

/// <summary>
/// Resets all active options. Called at the start of a creature's turn.
/// Called by ActionManager.OnTurnStart.
/// </summary>
public void OnTurnStart()
{
    activeOptions.Clear();
}
}