using Godot;
using System.Collections.Generic;
using System.Linq;

public static class CombatCalculations
{
    internal static int CalculateFinalAC(CreatureStats defender, bool isTouchAttack, int coverBonus, CreatureStats attacker)
    {
        var defenderEffects = defender.GetNodeOrNull<StatusEffectController>("StatusEffectController");
        var defenderActionManager = defender.GetNodeOrNull<ActionManager>("ActionManager");
        int finalAC = 10;

        // --- 1. DETERMINE STATE ---
        bool isFeinted = defenderEffects != null && defenderEffects.ActiveEffects.Any(e => e.EffectData.ConditionApplied == Condition.Feinted && e.SourceCreature == attacker);
        bool isFlatFootedForThisAttack = defender.IsFlatFooted;
        
        // Invisibility Check
        if (attacker.MyEffects.HasCondition(Condition.Invisible))
        {
            if (!defender.Template.HasBlindsight || defender.GlobalPosition.DistanceTo(attacker.GlobalPosition) > defender.Template.SpecialSenseRange)
            {
                isFlatFootedForThisAttack = true;
            }
        }
        
        // Catch Off-Guard Check
        var attackerWeapon = attacker.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
        if (attacker.HasFeat("Catch Off-Guard") && attackerWeapon != null && attackerWeapon.IsImprovised)
        {
            var defenderWeapon = defender.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
            if (defenderWeapon == null && (defender.Template.MeleeAttacks == null || !defender.Template.MeleeAttacks.Any()))
            {
                isFlatFootedForThisAttack = true;
            }
        }

        // --- INTERNAL STOMACH AC OVERRIDE ---
        if (attacker != null && attacker.MyEffects != null && attacker.MyEffects.HasCondition(Condition.Swallowed))
        {
            var swallowedCtrl = attacker.GetNodeOrNull<SwallowedController>("SwallowedController");
            if (swallowedCtrl != null && swallowedCtrl.Swallower == defender)
            {
                int estimatedNA = defender.Template.ArmorClass_Total - 10 - CalculateModifier(defender.Template.Dexterity) - defender.GetSizeModifier();
                return 10 + Mathf.Max(0, estimatedNA / 2);
            }
        }

        // --- 2. BASE MODIFIERS ---
        finalAC += defender.GetSizeModifier();
        
        // Dex Bonus (Lost if Flat-Footed)
        // Note: Cowering, Blinded, Stunned, Helpless all imply losing Dex bonus logic usually.
        bool losesDex = isFlatFootedForThisAttack || isFeinted || 
                        (defenderEffects != null && (defenderEffects.HasCondition(Condition.Blinded) || 
                        defenderEffects.HasCondition(Condition.Cowering) || 
                        defenderEffects.HasCondition(Condition.Stunned) || 
                        defenderEffects.HasCondition(Condition.Helpless) ||
                        (defenderActionManager != null && defenderActionManager.IsRunning && !defender.HasFeat("Run"))));

        if (!losesDex)
        {
            finalAC += defender.DexModifier;
        }

        finalAC += coverBonus;

        // --- 3. ARMOR & NATURAL ARMOR (Gaseous Logic) ---
        bool isGaseous = defenderEffects != null && defenderEffects.HasCondition(Condition.Gaseous);
		 bool isIncorporeal = defenderEffects != null && defenderEffects.HasCondition(Condition.Incorporeal);

        if (!isTouchAttack && !isGaseous && !isIncorporeal)

              {
            // Normal: Add Template Natural Armor + Inventory Armor
            finalAC += defender.Template.ArmorClass_Total - (10 + CalculateModifier(defender.Template.Dexterity)); 
            finalAC += defender.GetNodeOrNull<InventoryController>("InventoryController")?.GetTotalStatModifierFromEquipment(StatToModify.ArmorClass) ?? 0;
        }
        // If Gaseous, we SKIP this block entirely (Armor is worthless).

        // --- 4. STATUS EFFECT BONUSES (Filtered Loop) ---
        if (defender.MyEffects != null)
        {
            // Get ALL AC modifiers (Buffs, Feats, Items processed via effects)
            var allMods = defender.MyEffects.GetAllModifiersForStat(StatToModify.ArmorClass);
            
            foreach (var mod in allMods)
            {
                // RULE: DODGE
                // "A condition that makes you lose your Dex bonus to AC also makes you lose dodge bonuses."
                if (mod.BonusType == BonusType.Dodge && losesDex)
                {
                    continue; // Skip Dodge if flat-footed
                }

                // RULE: GASEOUS FORM
                // "Material armor (including natural armor) becomes worthless... deflection bonuses and armor bonuses from force effects still apply."
                if (isGaseous)
                {
                    if (mod.BonusType == BonusType.Natural) continue; // Skip Barkskin
                    if (mod.BonusType == BonusType.Armor || mod.BonusType == BonusType.Shield)
                    {
                        // We need to allow Force effects (Mage Armor, Shield).
                        // Since we don't have a descriptor, we assume Spell-based Armor/Shield IS Force.
                        // But Physical Armor (Inventory) is handled in Step 3, so mods here usually ARE spells.
                        // Exception: Bracers of Armor (Force) vs Magic Vestment (Enhancement to Physical).
                        // For this sim, we assume all StatusEffect Armor bonuses are Force.
                    }
                }

                // Apply logic for Acrobatics Defense (Fighting Defensively)
                // This is usually a Dodge bonus, so it's handled by the Dodge check above.
                
                finalAC += mod.ModifierValue;
            }
        }
		
		        // --- 4.5. INCORPOREAL DEFLECTION BONUS ---
        if (defenderEffects != null && defenderEffects.HasCondition(Condition.Incorporeal))
        {
            int chaDeflection = Mathf.Max(1, defender.ChaModifier);
            finalAC += chaDeflection;
        }

        // --- 5. PENALTIES ---
        if (defenderEffects != null)
        {
            if (defenderEffects.HasCondition(Condition.Stunned)) finalAC -= 2;
            if (defenderEffects.HasCondition(Condition.Helpless)) finalAC -= 4;
            // Blinded -2 AC is usually handled by the status effect modification list, 
            // but if hardcoded:
            if (defenderEffects.HasCondition(Condition.Blinded)) finalAC -= 2;
        }

        return finalAC;
    }
    
    public static bool IsFlankedBy(CreatureStats defender, CreatureStats attacker)
    {
        // 1. Check for immunity. If the defender has All-around Vision or a similar ability, they cannot be flanked.
        if (defender.Template.IsImmuneToFlanking)
        {
            return false;
        }

        // 2. Original flanking logic proceeds if the creature is not immune.
        var allCombatants = TurnManager.Instance.GetAllCombatants();
        if (allCombatants == null) return false;
        
        // Godot Note: Ensure CompareTag is implemented or check Group membership
        var allies = allCombatants.Where(c => c != attacker && c.IsInGroup("Enemy") == attacker.IsInGroup("Enemy"));
        
        foreach (var ally in allies)
        {
            if (IsThreatening(ally, defender) && CheckFlankingGeometry(attacker, ally, defender))
            {
                return true;
            }
        }
        return false;
    }
    
    public static bool CheckFlankingGeometry(CreatureStats attacker1, CreatureStats attacker2, CreatureStats target)
    {
        List<GridNode> attacker1Nodes = GridManager.Instance.GetNodesOccupiedByCreature(attacker1);
        List<GridNode> attacker2Nodes = GridManager.Instance.GetNodesOccupiedByCreature(attacker2);
        List<GridNode> targetNodes = GridManager.Instance.GetNodesOccupiedByCreature(target);
        
        if (!attacker1Nodes.Any() || !attacker2Nodes.Any() || !targetNodes.Any()) return false;
        
        Vector3 targetCenter = Vector3.Zero;
        foreach(var node in targetNodes)
        {
            targetCenter += node.worldPosition;
        }
        targetCenter /= targetNodes.Count;
        targetCenter.Y = 0; // Flatten Y for 2D plane logic check
        
        foreach (var node1 in attacker1Nodes)
        {
            foreach (var node2 in attacker2Nodes)
            {
                Vector3 attacker1Pos = new Vector3(node1.worldPosition.X, 0, node1.worldPosition.Z);
                Vector3 attacker2Pos = new Vector3(node2.worldPosition.X, 0, node2.worldPosition.Z);
                Vector3 toAttacker1 = (attacker1Pos - targetCenter).Normalized();
                Vector3 toAttacker2 = (attacker2Pos - targetCenter).Normalized();
                
                // Godot Dot Product
                if (toAttacker1.Dot(toAttacker2) < -0.5f)
                {
                    return true;
                }
            }
        }
        return false;
    }
    
    internal static (int primary, int offhand) CalculateTwfPenalties(CreatureStats attacker)
    {
        bool hasTwfFeat = attacker.HasFeat("Two-Weapon Fighting");
        var offHandWeapon = attacker.MyInventory?.GetEquippedItem(EquipmentSlot.OffHand);
        bool isOffHandLight = (offHandWeapon != null && offHandWeapon.Handedness == WeaponHandedness.Light) || offHandWeapon == null;
        int primaryPenalty = -6;
        int offhandPenalty = -10;
        
        if (isOffHandLight)
        {
            primaryPenalty += 2;
            offhandPenalty += 2;
        }
        if (hasTwfFeat)
        {
            primaryPenalty += 2;
            offhandPenalty += 6;
        }
        return (primaryPenalty, offhandPenalty);
    }
    
    internal static int CalculateModifier(int score) => Mathf.FloorToInt((score - 10) / 2f);

    internal static bool IsNodeWalkable(CreatureStats attacker, GridNode startNode, GridNode targetNode)
    {
        // Now calling the real method from the Pathfinding class
        return Pathfinding.Instance.IsNodeWalkable(attacker, startNode, targetNode);
    }

    internal static bool IsSquareOccupied(GridNode node, CreatureStats creatureToIgnore)
    {
       // Godot note: Pathfinding should be a singleton or accessible static
       return Pathfinding.Instance.IsSquareOccupied(node, creatureToIgnore);
    }

    internal static VisibilityResult GetVisibilityFromPoint(Vector3 position, Vector3 originPoint)
    {
        // Now calling the real, static method from the LineOfSightManager class
        // Note: We need a dummy context for the Raycast usually, passing null or a global context if static
        // Assuming LineOfSightManager uses a static accessor to PhysicsDirectSpaceState or passed in Context.
        // For strict port, we assume the Unity version had a static method.
        // In Godot, LoSManager usually needs the World3D. 
        // We will assume GridManager.Instance is a valid context node.
        return LineOfSightManager.GetVisibilityFromPoint(GridManager.Instance, position, originPoint);
    }

    internal static bool IsThreatening(CreatureStats potentialThreatener, CreatureStats target)
    {
        if (potentialThreatener.MyEffects != null)
        {
            if (potentialThreatener.MyEffects.HasCondition(Condition.Helpless)) return false;
            if (potentialThreatener.MyEffects.HasCondition(Condition.WhirlwindForm)) return false;
        }
        
        // For general threat checks, we only care about the maximum possible reach.
        // A creature with a reach weapon still threatens adjacent squares.
        float maxReach = potentialThreatener.GetMaxReach();
        if (maxReach <= 0) return false;
        
        float distance = potentialThreatener.GlobalPosition.DistanceTo(target.GlobalPosition);
        return distance <= maxReach;
    }
}