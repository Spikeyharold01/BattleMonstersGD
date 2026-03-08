using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// =================================================================================================
// FILE: CombatManeuvers.cs
// PURPOSE: Handles resolution of Bull Rush, Grapple, Trip, Sunder, etc.
// GODOT PORT: Full fidelity using async/await.
// =================================================================================================
public static class CombatManeuvers
{
    /// <summary>
    /// Resolves a Bull Rush combat maneuver attempt.
    /// </summary>
    public static async Task ResolveBullRushCoroutine(CreatureStats attacker, CreatureStats defender)
    {
        if (attacker == null || defender == null) return;
        
        // Rule: Bull Rush provokes unless the attacker has the feat.
        if (!attacker.HasFeat("Improved Bull Rush"))
        {
            await AoOManager.Instance.CheckAndResolve(attacker, ProvokingActionType.CombatManeuver);
            if (attacker == null || attacker.CurrentHP <= 0) return;
        }

        GD.Print($"{attacker.Name} attempts a Bull Rush against {defender.Name}.");
        
        int cmb = attacker.GetCMB(ManeuverType.BullRush);
        if (attacker.HasFeat("Improved Bull Rush"))
        {
            cmb += 2;
        }

        int maneuverRoll = Dice.Roll(1, 20) + cmb;
        int defenderCMD = defender.GetCMD(ManeuverType.BullRush); 

        GD.Print($"Rolls {maneuverRoll} vs CMD {defenderCMD}.");
        if (maneuverRoll >= defenderCMD)
        {
            int excess = maneuverRoll - defenderCMD;
            int squaresToPush = 1 + (excess / 5);
            float distanceToPush = squaresToPush * 5f;
            GD.PrintRich($"[color=green]Success![/color] {defender.Name} will be pushed back {distanceToPush}ft.");
            Vector3 direction = (defender.GlobalPosition - attacker.GlobalPosition).Normalized();
            
            // This needs a proper path check to handle collisions, similar to Awesome Blow logic
            Vector3 destination = defender.GlobalPosition + direction * distanceToPush;
            defender.GlobalPosition = destination;
        }
        else
        {
            GD.PrintRich("[color=red]Bull Rush failed.[/color]");
        }
    }
    
    /// <summary>
    /// Resolves a Dirty Trick combat maneuver attempt.
    /// </summary>
    public static void ResolveDirtyTrick(CreatureStats attacker, CreatureStats defender, StatusEffect_SO effectToApply)
    {
        if (attacker == null || defender == null || effectToApply == null) return;
        GD.Print($"{attacker.Name} attempts a Dirty Trick ({effectToApply.EffectName}) against {defender.Name}.");
        int maneuverRoll = Dice.Roll(1, 20) + attacker.GetCMB(ManeuverType.DirtyTrick);
        int defenderCMD = defender.GetCMD(ManeuverType.DirtyTrick);
        GD.Print($"Rolls {maneuverRoll} vs CMD {defenderCMD}.");
        if (maneuverRoll >= defenderCMD)
        {
            int excess = maneuverRoll - defenderCMD;
            int duration = 1 + (excess / 5);
            StatusEffect_SO newInstance = (StatusEffect_SO)effectToApply.Duplicate();
            newInstance.DurationInRounds = duration;
            defender.GetNode<StatusEffectController>("StatusEffectController").AddEffect(newInstance, attacker);
            GD.PrintRich($"[color=green]Success![/color] {defender.Name} is now {newInstance.EffectName} for {duration} round(s).");
        }
        else
        {
            GD.PrintRich("[color=red]Dirty Trick failed.[/color]");
        }
    }
    
    /// <summary>
    /// Resolves a Disarm combat maneuver attempt.
    /// </summary>
    public static void ResolveDisarm(CreatureStats attacker, CreatureStats defender)
    {
        if (attacker == null || defender == null) return;
        GD.Print($"{attacker.Name} attempts to Disarm {defender.Name}.");
        
        InventoryController targetInv = defender.GetNodeOrNull<InventoryController>("InventoryController");
        if (targetInv == null || targetInv.GetEquippedItem(EquipmentSlot.MainHand) == null)
        {
            GD.Print($"{defender.Name} has no weapon to disarm. The attempt automatically fails.");
            return;
        }

        int cmb = attacker.GetCMB(ManeuverType.Disarm);
        var attackerWeapon = attacker.GetNodeOrNull<InventoryController>("InventoryController")?.GetEquippedItem(EquipmentSlot.MainHand);
        if (attackerWeapon != null && attackerWeapon.HasDisarmFeature)
        {
            cmb += 2;
            GD.Print("+2 bonus from disarm weapon.");
        }
        if (attackerWeapon == null) cmb -= 4;

        int maneuverRoll = Dice.Roll(1, 20) + cmb;
        int defenderCMD = defender.GetCMD(ManeuverType.Disarm);
        
        GD.Print($"Rolls {maneuverRoll} vs CMD {defenderCMD}.");

        if (maneuverRoll >= defenderCMD)
        {
            targetInv.DropItemFromSlot(EquipmentSlot.MainHand, defender.GlobalPosition);
            GD.PrintRich($"[color=green]Success![/color] {defender.Name} is disarmed!");
        }
        else
        {
            if (defenderCMD - maneuverRoll >= 10)
            {
                attacker.GetNodeOrNull<InventoryController>("InventoryController")?.DropItemFromSlot(EquipmentSlot.MainHand, attacker.GlobalPosition);
                GD.PrintRich($"[color=orange]Critical Failure![/color] {attacker.Name} drops their weapon and is now disarmed!");
            }
            else
            {
                GD.PrintRich("[color=red]Disarm failed.[/color]");
            }
        }
    }
    
    /// <summary>
    /// Resolves a Drag combat maneuver attempt.
    /// </summary>
    public static void ResolveDrag(CreatureStats attacker, CreatureStats defender)
    {
        if (attacker == null || defender == null) return;
        GD.Print($"{attacker.Name} attempts to Drag {defender.Name}.");
        int cmb = attacker.GetCMB(ManeuverType.Drag);
        
        // Note: Assuming GetEquippedItemInstance is available or using SO logic
        var attackerWeapon = attacker.GetNodeOrNull<InventoryController>("InventoryController")?.GetEquippedItem(EquipmentSlot.MainHand);
        // Assuming HasTripFeature applies to Drag if using a weapon like a hook, per specific weapon rules, 
        // though standard Pathfinder separates them. The original code checked HasTripFeature.
        if (attackerWeapon != null && attackerWeapon.HasTripFeature)
        {
            int enhancementBonus = attackerWeapon.Modifications.FirstOrDefault(m => m.BonusType == BonusType.Enhancement)?.ModifierValue ?? 0;
            if (enhancementBonus > 0)
            {
                cmb += enhancementBonus;
                GD.Print($"+{enhancementBonus} bonus to Drag from trip weapon enhancement.");
            }
        }
        
        int maneuverRoll = Dice.Roll(1, 20) + cmb;
        int defenderCMD = defender.GetCMD(ManeuverType.Drag);
        
        GD.Print($"Rolls {maneuverRoll} vs CMD {defenderCMD}.");

        if (maneuverRoll >= defenderCMD)
        {
            int excess = maneuverRoll - defenderCMD;
            int squaresToMove = 1 + (excess / 5);
            float distanceToMove = squaresToMove * 5f;

            GD.PrintRich($"[color=green]Success![/color] Both creatures will be moved {distanceToMove}ft.");
            
            Vector3 dragDirection = (attacker.GlobalPosition - defender.GlobalPosition).Normalized();
            Vector3 defenderDestination = defender.GlobalPosition + dragDirection * distanceToMove;
            Vector3 attackerDestination = attacker.GlobalPosition + dragDirection * distanceToMove;

            defender.GlobalPosition = defenderDestination;
            attacker.GlobalPosition = attackerDestination;
        }
        else
        {
            GD.PrintRich("[color=red]Drag failed.[/color]");
        }
    }

    /// <summary>
    /// Resolves an attempt to initiate a grapple. Wrapped in Task for AoO wait.
    /// </summary>
    public static void ResolveGrapple(CreatureStats attacker, CreatureStats defender, bool isFreeAction = false, NaturalAttack initiatingAttack = null)
    {
        // Fire and forget, or handle by TurnManager
        _ = ResolveGrappleAsync(attacker, defender, isFreeAction, initiatingAttack);
    }


    internal static async Task ResolveGrappleAsync(CreatureStats attacker, CreatureStats defender, bool isFreeAction, NaturalAttack initiatingAttack)
    {
        if (attacker == null || defender == null) return;
		if ((attacker.MyEffects != null && attacker.MyEffects.HasCondition(Condition.Incorporeal)) || 
            (defender.MyEffects != null && defender.MyEffects.HasCondition(Condition.Incorporeal)))
        {
             GD.Print("Incorporeal creatures cannot participate in grapples.");
             return;
        }
		 if (defender.MyEffects.HasCondition(Condition.FreedomOfMovement))
        {
             GD.Print($"{defender.Name} is protected by Freedom of Movement. Grapple fails.");
             return;
        }
        
        // --- GRAB ABILITY & SIZE ---
        bool hasGrabAbility = attacker.Template.SpecialAttacks.Any(a => a.AbilityName.Contains("Grab")) || 
                              (attacker.Template.MeleeAttacks.Any(a => a.HasGrab));
        
        if (isFreeAction && hasGrabAbility)
        {
            if ((int)defender.Template.Size > (int)attacker.Template.Size)
            {
                GD.Print($"{attacker.Name} cannot Grab {defender.Name} (Target too large).");
                return;
            }
        }

        bool provokesAoO = !isFreeAction && CombatCalculations.IsThreatening(defender, attacker);
        bool hasImprovedGrapple = attacker.HasFeat("Improved Grapple");

        if (provokesAoO && !hasImprovedGrapple)
        {
            await AoOManager.Instance.CheckAndResolve(attacker, ProvokingActionType.CombatManeuver, null, null);
            if (attacker == null || attacker.CurrentHP <= 0) return; // Stop if the AoO was lethal
        }

        int cmb = attacker.GetCMB(ManeuverType.Grapple);
        
        // --- GRAB BONUS & CHOICE ---
        if (hasGrabAbility) cmb += 4;

        bool useBodyPartOnly = false;
        
        if (isFreeAction && hasGrabAbility)
        {
            if (attacker.IsInGroup("Player"))
            {
                // Placeholder for UI prompt
                useBodyPartOnly = false; 
            }
            else // AI Logic
            {
                int estimatedDefCMD = 10 + defender.Template.BaseAttackBonus + defender.StrModifier;
                int penaltyRoll = 10 + (cmb - 20);
                
                if (penaltyRoll >= estimatedDefCMD) 
                {
                    useBodyPartOnly = true;
                    cmb -= 20;
                    GD.Print($"{attacker.Name} attempts to hold with body part only (-20 CMB).");
                }
            }
        }

        var inv = attacker.GetNodeOrNull<InventoryController>("InventoryController");
        if (inv != null && inv.GetEquippedItem(EquipmentSlot.MainHand) != null && inv.GetEquippedItem(EquipmentSlot.OffHand) != null)
        {
            cmb -= 4; // Penalty for not having two free hands (unless creature has >2 arms, logic simplified)
        }

        int maneuverRoll = Dice.Roll(1, 20) + cmb;
        int defenderCMD = defender.GetCMD(ManeuverType.Grapple);
        GD.Print($"{attacker.Name} attempts to Grapple {defender.Name}. Rolls {maneuverRoll} vs CMD {defenderCMD}.");
        
        if (maneuverRoll >= defenderCMD)
        {
            GD.PrintRich($"[color=green]Success![/color] A grapple has been initiated.");
            var grappleState = new GrappleState(attacker, defender);
            grappleState.IsHoldingWithBodyPartOnly = useBodyPartOnly;
			 grappleState.InitiatingAttack = initiatingAttack;
            
            var adhesive = attacker.GetNodeOrNull<PassiveAdhesiveController>("PassiveAdhesiveController");
            if (adhesive != null && adhesive.IsAdhesiveActive)
            {
                grappleState.IsHoldingWithBodyPartOnly = true; 
                GD.Print($"{attacker.Name} uses Adhesive to grapple without being grappled.");
            }

            bool hasTenaciousGrapple = attacker.HasSpecialRule("Tenacious Grapple");
            if (hasTenaciousGrapple)
            {
                GD.Print($"{attacker.Name}'s Tenacious Grapple keeps them from gaining the Grappled condition.");
            }

            attacker.CurrentGrappleState = grappleState;
            defender.CurrentGrappleState = grappleState;
            
            var grappleEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Grappled_Effect.tres");
            if (grappleEffect != null)
            {
                defender.MyEffects.AddEffect(grappleEffect, attacker);
                if (!useBodyPartOnly && !hasTenaciousGrapple)
                {
                    attacker.MyEffects.AddEffect(grappleEffect, attacker);
                }
            }

            if (attacker.GlobalPosition.DistanceTo(defender.GlobalPosition) > 6f)
            {
                var attackerNode = GridManager.Instance.NodeFromWorldPoint(attacker.GlobalPosition);
                // Move defender adjacent
                var emptyNode = GridManager.Instance.GetNeighbours(attackerNode).FirstOrDefault(n => n.terrainType == TerrainType.Ground && !CombatCalculations.IsSquareOccupied(n, attacker));
                if (emptyNode != null)
                {
                    defender.GlobalPosition = emptyNode.worldPosition;
                }
            }
        }
        else
        {
            GD.PrintRich("<color=red>Grapple attempt failed.</color>");
        }
    }
    
    /// <summary>
    /// Resolves an attempt to break a grapple. Does not provoke.
    /// </summary>
    public static void ResolveBreakGrapple(CreatureStats tryingToEscape)
    {
		if (tryingToEscape.MyEffects.HasCondition(Condition.FreedomOfMovement))
        {
             GD.PrintRich($"[color=green]{tryingToEscape.Name} slips away freely using Freedom of Movement.[/color]");
             BreakGrapple(tryingToEscape);
             return;
        }
        if (tryingToEscape == null || tryingToEscape.CurrentGrappleState == null) return;
        var grappleState = tryingToEscape.CurrentGrappleState;
        var controller = grappleState.Controller;
        int maneuverRoll = Dice.Roll(1, 20) + tryingToEscape.GetCMB();
        int controllerCMD = controller.GetCMD();
        GD.Print($"{tryingToEscape.Name} attempts to break grapple. Rolls {maneuverRoll} vs {controller.Name}'s CMD {controllerCMD}.");
        if (maneuverRoll >= controllerCMD)
        {
            GD.PrintRich($"[color=green]Success![/color] {tryingToEscape.Name} breaks free!");
            BreakGrapple(tryingToEscape);
        }
        else
        {
            GD.PrintRich("<color=red>Failed to break grapple.</color>");
        }
    }

    /// <summary>
    /// Helper method to cleanly end a grapple for all participants.
    /// </summary>
    public static void BreakGrapple(CreatureStats creatureInGrapple)
    {
        if (creatureInGrapple == null) return;

        var swallowedCtrl = creatureInGrapple.GetNodeOrNull<SwallowedController>("SwallowedController");
        if (swallowedCtrl != null)
        {
            swallowedCtrl.EscapeViaGrapple();
            return;
        }

        if (creatureInGrapple.CurrentGrappleState == null) return;
        var grappleState = creatureInGrapple.CurrentGrappleState;
        var controller = grappleState.Controller;
        var target = grappleState.Target;
        controller.CurrentGrappleState = null;
        target.CurrentGrappleState = null;
        controller.MyEffects.RemoveEffect("Grappled");
        controller.MyEffects.RemoveEffect("Pinned");
        target.MyEffects.RemoveEffect("Grappled");
        target.MyEffects.RemoveEffect("Pinned");
        GD.Print("The grapple has been broken.");
    }
    
    /// <summary>
    /// Resolves the grappler's standard action to maintain the grapple and perform a sub-action.
    /// </summary>
    public static void ResolveMaintainGrapple(CreatureStats grappler, GrappleSubAction subAction)
    {
        if (grappler == null || grappler.CurrentGrappleState == null || grappler.CurrentGrappleState.Controller != grappler) return;
        var grappleState = grappler.CurrentGrappleState;
        var target = grappleState.Target;
        int bonus = (grappleState.RoundsMaintained > 1) ? 5 : 0;
// ADD -20 PENALTY LOGIC HERE
        int cmb = grappler.GetCMB(ManeuverType.Grapple, true) + bonus;
        if (grappleState.IsHoldingWithBodyPartOnly) cmb -= 20;

        int maneuverRoll = Dice.Roll(1, 20) + cmb;
        int targetCMD = target.GetCMD(ManeuverType.Grapple);
        
        GD.Print($"{grappler.Name} attempts to maintain grapple. Rolls {maneuverRoll} vs CMD {targetCMD}.");
        if (maneuverRoll >= targetCMD)
        {
            GD.PrintRich("<color=green>Grapple maintained.</color>");
            grappleState.RoundsMaintained++;
			// --- RAKE IMPLEMENTATION ---
            // Rule: "A monster with the rake ability must begin its turn already grappling to use its rake"
            bool startedBeforeThisRound = grappleState.StartRound < TurnManager.Instance.GetCurrentRound();
            if (startedBeforeThisRound && grappler.Template.RakeAttacks.Count > 0)
            {
                GD.PrintRich($"[color=orange]{grappler.Name} rakes {target.Name} while maintaining the grapple![/color]");
                foreach (var rake in grappler.Template.RakeAttacks)
                {
                    // Rake attacks are made at the standard bonus (handled by ResolveMeleeAttack reading the NaturalAttack data)
                    CombatManager.ResolveMeleeAttack(grappler, target, rake);
                }
            }
            // ---------------------------
            switch (subAction)
            {
                case GrappleSubAction.Damage:
                    GD.Print($"{grappler.Name} chooses to damage {target.Name}.");
 bool damageDealt = false;

                    // 1. If triggered by Grab (InitiatingAttack exists), deal that attack's damage automatically.
                    if (grappleState.InitiatingAttack != null)
                    {
                        var att = grappleState.InitiatingAttack;
                        // Calculate damage similar to CombatAttacks
                        int dmg = 0;
                        if (att.DamageInfo != null)
                        {
                            foreach(var info in att.DamageInfo)
                                dmg += Dice.Roll(info.DiceCount, info.DieSides) + info.FlatBonus;
                        }
                        
                        // Handle Primary/Secondary str bonus logic if possible, or default to full Str for Grapple damage
                        // Rules say "damage indicated for the attack". 
                        // If it was secondary, it stays secondary? Usually Grapple damage uses full Str unless noted. 
                        // Keeping simple: StrModifier.
                        dmg += grappler.StrModifier; 
                        
                        if (att.DamageInfo.Count > 0)
                            target.TakeDamage(dmg, att.DamageInfo[0].DamageType, grappler, null, att);
                        
                        damageDealt = true;
                    }

                    // 2. Check for Constrict or other effects (Effect_OnGrappleMaintain)
                    var grappleAbility = grappler.Template.KnownAbilities.FirstOrDefault(a => 
                        a.EffectComponents.Any(c => c is Effect_OnGrappleMaintain));
                    
                    if (grappleAbility != null && grappleAbility.IsImplemented)
                    {
                        var context = new EffectContext { Caster = grappler, PrimaryTarget = target };
                        foreach (var component in grappleAbility.EffectComponents) component.ExecuteEffect(context, grappleAbility, new Dictionary<CreatureStats, bool>());
                        damageDealt = true;
                    }

                    // 3. Fallback: If no Grab and no Constrict, deal Unarmed Damage
                    if (!damageDealt)
                    {
                        var primary = grappler.Template.MeleeAttacks.FirstOrDefault(a => a.IsPrimary);
                        if (primary != null)
                        {
                            // This path is for standard grapples without Grab/Constrict attempting to damage
                            int dmg = Dice.Roll(primary.DamageInfo[0].DiceCount, primary.DamageInfo[0].DieSides) + primary.DamageInfo[0].FlatBonus + grappler.StrModifier;
                            target.TakeDamage(dmg, primary.DamageInfo[0].DamageType, grappler, null, primary);
                        }
                        else
                        {
                            target.TakeDamage(1 + grappler.StrModifier, "Bludgeoning", grappler);
                        }
                    }
                    break;
                case GrappleSubAction.Pin:
                    GD.Print($"{grappler.Name} chooses to Pin {target.Name}.");
                    var pinEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Pinned_Effect.tres");
                    if (pinEffect != null) target.MyEffects.AddEffect(pinEffect, grappler);
                    break;
                case GrappleSubAction.Move:
                    GD.Print($"{grappler.Name} chooses to move the grapple (not fully implemented).");
                    break;
            }
        }
        else
        {
            GD.PrintRich($"[color=red]Failed to maintain grapple![/color] The grapple is broken.");
            BreakGrapple(grappler);
        }
    }
    
    /// <summary>
    /// Resolves a Sunder combat maneuver attempt against an opponent's gear.
    /// </summary>
    public static async Task ResolveSunderCoroutine(CreatureStats attacker, CreatureStats defender)
    {
        if (attacker == null || defender == null) return;

        // Rule: Sunder provokes an AoO unless the attacker has Improved Sunder.
        if (!attacker.HasFeat("Improved Sunder"))
        {
            await AoOManager.Instance.CheckAndResolve(attacker, ProvokingActionType.CombatManeuver);
            if (attacker == null || attacker.CurrentHP <= 0) return;
        }

        var targetInv = defender.GetNodeOrNull<InventoryController>("InventoryController");
        if (targetInv == null) return;

        // Prioritize Weapon, Shield, Armor
        // Assuming InventoryController has logic to return ItemInstance not just Item_SO
        // We use SO for logic flow here as per original script pattern, assuming ItemInstance wrapper
        var itemToSunder = targetInv.GetEquippedItemInstance(EquipmentSlot.MainHand) ?? 
                           targetInv.GetEquippedItemInstance(EquipmentSlot.Shield) ?? 
                           targetInv.GetEquippedItemInstance(EquipmentSlot.Armor);
                           
        if (itemToSunder == null)
        {
            GD.Print($"{defender.Name} has no items to Sunder.");
            return;
        }

        GD.Print($"{attacker.Name} attempts to Sunder {defender.Name}'s {itemToSunder.ItemData.ItemName}.");
        
        int cmb = attacker.GetCMB(ManeuverType.Sunder);
        if (attacker.HasFeat("Improved Sunder")) cmb += 2;
        if (attacker.HasFeat("Greater Sunder")) cmb += 2;

        int maneuverRoll = Dice.Roll(1, 20) + cmb;
        int defenderCMD = defender.GetCMD(ManeuverType.Sunder);
        
        GD.Print($"Rolls {maneuverRoll} vs CMD {defenderCMD}.");
        if (maneuverRoll >= defenderCMD)
        {
            var weaponInstance = attacker.MyInventory.GetEquippedItemInstance(EquipmentSlot.MainHand);
            int damage = 0;
            if (weaponInstance != null && weaponInstance.ItemData.DamageInfo.Any())
            {
                var baseDmg = weaponInstance.ItemData.DamageInfo.First();
                damage = Dice.Roll(baseDmg.DiceCount, baseDmg.DieSides) + attacker.StrModifier;
            }
            else
            {
                damage = Dice.Roll(1, 3) + attacker.StrModifier; // Unarmed
            }
            
            int damageToObject = Mathf.Max(0, damage - itemToSunder.ItemData.Hardness);
            int excessDamage = damageToObject - itemToSunder.CurrentHP;
            
            itemToSunder.TakeDamage(damage);
            GD.PrintRich($"[color=green]Success![/color] {itemToSunder.ItemData.ItemName} takes {damage} damage and now has {itemToSunder.CurrentHP} HP.");
            
            if (itemToSunder.CurrentHP <= 0)
            {
                GD.PrintRich($"[color=red]{itemToSunder.ItemData.ItemName} is destroyed![/color]");
                targetInv.DropItemFromSlot(itemToSunder.ItemData.EquipSlot, defender.GlobalPosition);

                // Greater Sunder: Apply excess damage to wielder.
                if (attacker.HasFeat("Greater Sunder") && excessDamage > 0)
                {
                    GD.PrintRich($"[color=orange]Excess damage ({excessDamage}) from Greater Sunder is transferred to {defender.Name}![/color]");
                    defender.TakeDamage(excessDamage, "Untyped", attacker);
                }
            }
            else if (itemToSunder.IsBroken)
            {
                GD.PrintRich($"[color=orange]{itemToSunder.ItemData.ItemName} gains the broken condition![/color]");
            }
        }
        else
        {
            GD.PrintRich("[color=red]Sunder attempt failed.[/color]");
        }
    }

    /// <summary>
    /// Resolves a Trip combat maneuver attempt.
    /// </summary>
    public static void ResolveTrip(CreatureStats attacker, CreatureStats defender)
    {
        if (attacker == null || defender == null) return;
		 if ((attacker.MyEffects != null && attacker.MyEffects.HasCondition(Condition.Incorporeal)) || 
            (defender.MyEffects != null && defender.MyEffects.HasCondition(Condition.Incorporeal)))
        {
             GD.Print("Incorporeal creatures cannot participate in trip maneuvers.");
             return;
        }

        if (defender.Template.IsImmuneToTrip)
        {
            GD.Print($"{defender.Name} is immune to being tripped. The attempt automatically fails.");
            return;
        }

        GD.Print($"{attacker.Name} attempts to Trip {defender.Name}.");
        int maneuverRoll = Dice.Roll(1, 20) + attacker.GetCMB(ManeuverType.Trip);
        int defenderCMD = defender.GetCMD(ManeuverType.Trip);
        GD.Print($"Rolls {maneuverRoll} vs CMD {defenderCMD}.");
        
        if (maneuverRoll >= defenderCMD)
        {
            GD.PrintRich($"[color=green]Success![/color] {defender.Name} is knocked prone.");
            var proneEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Prone_Effect.tres");
            if (proneEffect != null)
            {
                defender.MyEffects.AddEffect(proneEffect, attacker);
            }
        }
        else
        {
            var weaponInstance = attacker.MyInventory.GetEquippedItemInstance(EquipmentSlot.MainHand);
            if (defenderCMD - maneuverRoll >= 10 && (weaponInstance == null || !weaponInstance.ItemData.HasTripFeature))
            {
                GD.PrintRich($"[color=orange]Critical Failure![/color] {attacker.Name} trips and is knocked prone!");
                var proneEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Prone_Effect.tres");
                if (proneEffect != null)
                {
                    attacker.MyEffects.AddEffect(proneEffect, attacker);
                }
            }
            else
            {
                 GD.PrintRich("[color=red]Trip attempt failed.[/color]");
            }
        }
    }

    /// <summary>
    /// Resolves an Overrun combat maneuver attempt.
    /// </summary>
    public static void ResolveOverrun(CreatureStats attacker, CreatureStats defender, bool targetAvoids)
    {
        if (attacker == null || defender == null) return;
        GD.Print($"{attacker.Name} attempts to Overrun {defender.Name}.");
        if (targetAvoids)
        {
            GD.Print($"{defender.Name} chooses to avoid the overrun. {attacker.Name} moves through.");
            attacker.GlobalPosition = defender.GlobalPosition;
            return;
        }

        int maneuverRoll = Dice.Roll(1, 20) + attacker.GetCMB(ManeuverType.Overrun);
        int defenderCMD = defender.GetCMD(ManeuverType.Overrun);
        GD.Print($"Rolls {maneuverRoll} vs CMD {defenderCMD}.");
        if (maneuverRoll >= defenderCMD)
        {
            GD.PrintRich($"[color=green]Success![/color] {attacker.Name} moves through {defender.Name}'s space.");
            attacker.GlobalPosition = defender.GlobalPosition;
            if (maneuverRoll - defenderCMD >= 5)
            {
                GD.PrintRich($"[color=green]...and knocks them prone![/color]");
                var proneEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Prone_Effect.tres");
                if (proneEffect != null)
                {
                    defender.MyEffects.AddEffect(proneEffect, attacker);
                }
            }
        }
        else
        {
            GD.PrintRich($"[color=red]Overrun attempt failed.[/color] Movement is stopped.");
        }
    }
    
    /// <summary>
    /// Resolves a Reposition combat maneuver attempt.
    /// </summary>
    public static void ResolveReposition(CreatureStats attacker, CreatureStats defender)
    {
        if (attacker == null || defender == null) return;
        GD.Print($"{attacker.Name} attempts to Reposition {defender.Name}.");
        int cmb = attacker.GetCMB(ManeuverType.Reposition);
        var attackerWeapon = attacker.MyInventory?.GetEquippedItemInstance(EquipmentSlot.MainHand);
        if (attackerWeapon != null && attackerWeapon.ItemData.HasTripFeature)
        {
            int enhancementBonus = attackerWeapon.ItemData.Modifications.FirstOrDefault(m => m.BonusType == BonusType.Enhancement)?.ModifierValue ?? 0;
            if (enhancementBonus > 0)
            {
                cmb += enhancementBonus;
                GD.Print($"+{enhancementBonus} bonus to Reposition from trip weapon enhancement.");
            }
        }
        int maneuverRoll = Dice.Roll(1, 20) + cmb;
        int defenderCMD = defender.GetCMD(ManeuverType.Reposition);
        GD.Print($"Rolls {maneuverRoll} vs CMD {defenderCMD}.");
        if (maneuverRoll >= defenderCMD)
        {
            int squaresToMove = 1 + ((maneuverRoll - defenderCMD) / 5);
            GD.PrintRich($"[color=green]Success![/color] {defender.Name} can be moved {squaresToMove} square(s).");
            
            var attackerNode = GridManager.Instance.NodeFromWorldPoint(attacker.GlobalPosition);
            var emptyNode = GridManager.Instance.GetNeighbours(attackerNode).FirstOrDefault(n => n.terrainType == TerrainType.Ground);

            if (emptyNode != null)
            {
                defender.GlobalPosition = emptyNode.worldPosition;
                GD.Print($"{defender.Name} is moved to {emptyNode.worldPosition}.");
            }
        }
        else
        {
            GD.PrintRich("[color=red]Reposition attempt failed.[/color]");
        }
    }
    
    /// <summary>
    /// Resolves a Steal combat maneuver attempt.
    /// </summary>
    public static void ResolveSteal(CreatureStats attacker, CreatureStats defender, EquipmentSlot slotToSteal)
    {
        if (attacker == null || defender == null) return;
        GD.Print($"{attacker.Name} attempts to Steal from {defender.Name}'s {slotToSteal} slot.");
        var targetInv = defender.GetNodeOrNull<InventoryController>("InventoryController");
        var attackerInv = attacker.GetNodeOrNull<InventoryController>("InventoryController");
        var itemInstance = targetInv?.GetEquippedItemInstance(slotToSteal);
        if (itemInstance == null)
        {
            GD.Print($"{defender.Name} has nothing to steal from that slot.");
            return;
        }
        int maneuverRoll = Dice.Roll(1, 20) + attacker.GetCMB(ManeuverType.Steal);
        int defenderCMD = defender.GetCMD(ManeuverType.Steal) + 5;
        GD.Print($"Rolls {maneuverRoll} vs CMD {defenderCMD}.");
        if (maneuverRoll >= defenderCMD)
        {
            GD.PrintRich($"[color=green]Success![/color] {attacker.Name} steals {itemInstance.ItemData.ItemName}!");
            targetInv.DropItemFromSlot(slotToSteal, defender.GlobalPosition);
            attackerInv.AddItem(new ItemInstance(itemInstance.ItemData)); 
        }
        else
        {
             GD.PrintRich("[color=red]Steal attempt failed.[/color]");
        }
    }
    
    /// <summary>
    /// Resolves an Awesome Blow combat maneuver attempt.
    /// </summary>
    public static void ResolveAwesomeBlow(CreatureStats attacker, CreatureStats defender)
    {
        if (attacker == null || defender == null) return;
        GD.Print($"{attacker.Name} attempts an Awesome Blow against {defender.Name}!");

        if (defender.Template.Size >= attacker.Template.Size)
        {
            GD.Print("Awesome Blow fails: Target is not smaller than the attacker.");
            return;
        }

        int maneuverRoll = Dice.Roll(1, 20) + attacker.GetCMB();
        int defenderCMD = defender.GetCMD();
        GD.Print($"Rolls {maneuverRoll} vs CMD {defenderCMD}.");

        if (maneuverRoll >= defenderCMD)
        {
            GD.PrintRich($"[color=green]Success![/color]");
            // 1. Apply Damage (Slam + STR)
            var slamAttack = attacker.Template.MeleeAttacks.FirstOrDefault(a => a.AttackName.ToLower().Contains("slam"));
            if (slamAttack != null)
            {
                int damage = Dice.Roll(slamAttack.DamageInfo[0].DiceCount, slamAttack.DamageInfo[0].DieSides) + attacker.StrModifier;
                defender.TakeDamage(damage, slamAttack.DamageInfo[0].DamageType, attacker, null, slamAttack);
            }

            // 2. Knock Flying 10ft and Prone
            if (defender == null || defender.CurrentHP <= 0) return;

            Vector3 direction = (defender.GlobalPosition - attacker.GlobalPosition).Normalized();
            Vector3 destination = defender.GlobalPosition + direction * 10f;
            
            // Replicating Unity Physics.Linecast for obstacle check
            var spaceState = attacker.GetWorld3D().DirectSpaceState;
            var query = PhysicsRayQueryParameters3D.Create(defender.GlobalPosition, destination);
            query.CollisionMask = 1; // Unwalkable/Walls layer assumed 1
            var result = spaceState.IntersectRay(query);

            if (result.Count > 0)
            {
                var hitCollider = (Node3D)result["collider"];
                Vector3 hitPoint = (Vector3)result["position"];
                
                GD.Print($"{defender.Name} is knocked into {hitCollider.Name}!");
                int collisionDamage = Dice.Roll(1, 6);
                
                defender.TakeDamage(collisionDamage, "Bludgeoning");
                // Damage obstacle logic if implemented
                
                defender.GlobalPosition = hitPoint - (direction * (defender.Template.Space / 2f));
            }
            else
            {
                defender.GlobalPosition = destination;
            }
            
            var proneEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Prone_Effect.tres");
            if(proneEffect != null) defender.MyEffects.AddEffect(proneEffect, attacker);
        }
        else
        {
            GD.PrintRich("<color=red>Awesome Blow maneuver failed.</color>");
        }
    }
}
