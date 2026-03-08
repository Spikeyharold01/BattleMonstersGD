using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// =================================================================================================
// FILE: CombatAttacks.cs (GODOT VERSION - SANITIZED)
// PURPOSE: Handles attack resolution.
// CHANGES: Removed hardcoded Shaken, Sickened, etc., checks from Attack/Damage calculation.
//          Relying on CreatureStats.GetTotalModifier + StatusEffect_SO.
// =================================================================================================
public static class CombatAttacks
{
	private static readonly Dictionary<string, string> ElementalBurstDamageTypes = new()
    {
        { "icy burst", "Cold" },
        { "shocking burst", "Electricity" },
        { "flaming burst", "Fire" }
    };

    public static void ResolveMeleeAttack_Object(CreatureStats attacker, ObjectDurability targetObject)
    {
        if (attacker == null || targetObject == null) return;
        
        GD.Print($"{attacker.Name} attacks the object: {targetObject.Name}");

        var weapon = attacker.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
        string damageType = "Bludgeoning";
        int damage = attacker.StrModifier;

        if (weapon != null)
        {
            var dmgInfo = weapon.DamageInfo.FirstOrDefault();
            if (dmgInfo != null)
            {
                damage += Dice.Roll(dmgInfo.DiceCount, dmgInfo.DieSides);
                damageType = dmgInfo.DamageType;
            }
        }
        else
        {
            damage += Dice.Roll(1, 3); 
        }

        targetObject.TakeDamage(Mathf.Max(1, damage), damageType);
    }
    
    public static void ResolveMeleeAttack(CreatureStats attacker, CreatureStats defender, NaturalAttack intendedNaturalAttack = null, Item_SO intendedWeapon = null)
    {
        if (attacker == null || defender == null) return;

        if (attacker.MyEffects != null && attacker.MyEffects.HasCondition(Condition.WhirlwindForm))
        {
            GD.PrintRich($"[color=orange]{attacker.Name} cannot make normal attacks while in Whirlwind form![/color]");
            return;
        }
        
		SoundSystem.EmitCreatureActionSound(attacker, SoundActionType.Attack, isSneaking: false, durationSeconds: 1.2f);
		
        if (attacker.IsMounted)
        {
            var mountedController = attacker.GetNodeOrNull<MountedCombatController>("MountedCombatController");
            if (mountedController != null && mountedController.IsUsingMountAsCover) return;
            
            if (attacker.MyInventory != null)
            {
                var mountedWeapon = attacker.MyInventory.GetEquippedItem(EquipmentSlot.MainHand);
                if (mountedWeapon != null && mountedWeapon.Handedness == WeaponHandedness.TwoHanded)
                {
                    if (mountedController != null && !mountedController.IsGuidingWithKnees) return;
                }
            }
        }

        Item_SO weapon = intendedWeapon ?? attacker.GetNodeOrNull<InventoryController>("InventoryController")?.GetEquippedItem(EquipmentSlot.MainHand);

        if (weapon != null && weapon.ItemType == ItemType.Weapon)
        {
            int attackBonus = attacker.Template.BaseAttackBonus + attacker.StrModifier;
            int damageBonus = (weapon.Handedness == WeaponHandedness.TwoHanded)
                ? Mathf.FloorToInt(attacker.StrModifier * 1.5f)
                : attacker.StrModifier;
            ResolveSingleMeleeAttack(attacker, defender, attackBonus, damageBonus, weapon.ItemName, weapon);
        }
        else if (attacker.Template.MeleeAttacks != null && attacker.Template.MeleeAttacks.Any())
        {
            var attackToUse = intendedNaturalAttack ?? attacker.Template.MeleeAttacks.FirstOrDefault(a => a.IsPrimary) ?? attacker.Template.MeleeAttacks.First();
            int attackBonus = attacker.Template.BaseAttackBonus + attacker.StrModifier + attackToUse.MiscAttackBonus;
            bool hasOnlyOneNaturalAttack = attacker.Template.MeleeAttacks.Count == 1;
            int damageBonus = hasOnlyOneNaturalAttack 
                ? Mathf.FloorToInt(attacker.StrModifier * 1.5f) 
                : attacker.StrModifier;
            ResolveSingleMeleeAttack(attacker, defender, attackBonus, damageBonus, attackToUse.AttackName, null, attackToUse);
        }
        else
        {
            int unarmedAttackBonus = attacker.Template.BaseAttackBonus + attacker.StrModifier;
            int damageBonus = attacker.StrModifier;
            ResolveSingleMeleeAttack(attacker, defender, unarmedAttackBonus, damageBonus, "Unarmed Strike");
        }
    }
    
    public static void ResolveFullAttack(CreatureStats attacker, CreatureStats defender)
    {
        if (attacker == null || defender == null) return;

        if (attacker.MyEffects != null && attacker.MyEffects.HasCondition(Condition.WhirlwindForm))
        {
            GD.PrintRich($"[color=orange]{attacker.Name} cannot make normal attacks while in Whirlwind form![/color]");
            return;
        }

        GD.PrintRich($"[color=lightblue]{attacker.Name} performs a FULL ATTACK on {defender.Name}![/color]");

        bool isTwoWeaponFighting = attacker.IsTwoWeaponFighting;
        var mainHandWeapon = attacker.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
        var offHandWeapon = isTwoWeaponFighting ? (mainHandWeapon.IsDoubleWeapon ? mainHandWeapon : attacker.MyInventory.GetEquippedItem(EquipmentSlot.OffHand)) : null;
 
        bool isUsingAnyWeapon = mainHandWeapon != null;

        if (isUsingAnyWeapon)
        {
            int primaryPenalty = 0;
            int offhandPenalty = 0;

            if (isTwoWeaponFighting)
            {
                (primaryPenalty, offhandPenalty) = CombatCalculations.CalculateTwfPenalties(attacker);
            }
            
            int currentBab = attacker.Template.BaseAttackBonus;
            int iteration = 0;
            while (currentBab > 0)
            {
                int totalAttackBonus = currentBab + attacker.StrModifier + primaryPenalty;
                int damageBonus = mainHandWeapon.Handedness == WeaponHandedness.TwoHanded 
                    ? Mathf.FloorToInt(attacker.StrModifier * 1.5f) 
                    : attacker.StrModifier;
                string attackName = $"Iterative Attack #{iteration + 1} ({mainHandWeapon.ItemName})";
                
                ResolveSingleMeleeAttack(attacker, defender, totalAttackBonus, damageBonus, attackName, mainHandWeapon, isOffHand: false);
                
                currentBab -= 5;
                iteration++;
            }

             if (isTwoWeaponFighting)
            {
                int totalAttackBonus = attacker.Template.BaseAttackBonus + attacker.StrModifier + offhandPenalty;
                string attackName = mainHandWeapon.IsDoubleWeapon ? $"Off-Hand Attack ({mainHandWeapon.ItemName} - End 2)" : $"Off-Hand Attack ({offHandWeapon.ItemName})";
                
                int offHandDamageBonus = Mathf.FloorToInt(attacker.StrModifier * 0.5f);
                ResolveSingleMeleeAttack(attacker, defender, totalAttackBonus, offHandDamageBonus, attackName, offHandWeapon, isOffHand: true);
            }
        }

        if (attacker.Template.MeleeAttacks != null && attacker.Template.MeleeAttacks.Any())
        {
            bool allAttacksAreSecondary = isUsingAnyWeapon;

            foreach (var naturalAttack in attacker.Template.MeleeAttacks)
            {
                int attackBonus;
                int damageBonus;

                if (allAttacksAreSecondary || !naturalAttack.IsPrimary)
                {
                    int penalty = attacker.HasFeat("Multiattack") ? -2 : -5;
                    attackBonus = attacker.Template.BaseAttackBonus + penalty + attacker.StrModifier + naturalAttack.MiscAttackBonus;
                    damageBonus = Mathf.FloorToInt(attacker.StrModifier * 0.5f);
                }
                else
                {
                    attackBonus = attacker.Template.BaseAttackBonus + attacker.StrModifier + naturalAttack.MiscAttackBonus;
                    bool hasOnlyOneNaturalAttack = attacker.Template.MeleeAttacks.Count == 1;
                    damageBonus = hasOnlyOneNaturalAttack 
                        ? Mathf.FloorToInt(attacker.StrModifier * 1.5f) 
                        : attacker.StrModifier;
                }

                ResolveSingleMeleeAttack(attacker, defender, attackBonus, damageBonus, naturalAttack.AttackName, null, naturalAttack);
            }
        }
        else if (!isUsingAnyWeapon)
        {
            ResolveMeleeAttack(attacker, defender);
        }
		 // --- NEW: QUICK STRIKES ---
        if (attacker.HasSpecialRule("Quick Strikes"))
        {
            GD.PrintRich($"[color=orange]{attacker.Name} uses Quick Strikes for an extra slam attack![/color]");
            int attackBonus = attacker.Template.BaseAttackBonus + attacker.StrModifier;
            int damageBonus = attacker.StrModifier;
            ResolveSingleMeleeAttack(attacker, defender, attackBonus, damageBonus, "Slam (Quick Strikes)");
        }
    }
    
    public static void ResolveChargeAttack(CreatureStats attacker, CreatureStats defender)
    {
        if (attacker == null || defender == null) return;
		        SoundSystem.EmitCreatureActionSound(attacker, SoundActionType.Charge, isSneaking: false, durationSeconds: 1.2f);
        GD.PrintRich($"[color=lightblue]{attacker.Name} ends their CHARGE with an attack on {defender.Name}![/color]");
        
        var chargePenaltyEffect = new StatusEffect_SO();
        chargePenaltyEffect.EffectName = "Charging Penalty";
        chargePenaltyEffect.DurationInRounds = 1;
        chargePenaltyEffect.Modifications.Add(new StatModification { StatToModify = StatToModify.ArmorClass, ModifierValue = -2 });
        attacker.GetNodeOrNull<StatusEffectController>("StatusEffectController")?.AddEffect(chargePenaltyEffect, attacker, null);

        Item_SO weapon = attacker.GetNodeOrNull<InventoryController>("InventoryController")?.GetEquippedItem(EquipmentSlot.MainHand);
        int chargeBonus = 2;
        int damageMultiplier = 1;

        if (attacker.IsMounted && weapon != null && weapon.IsLance)
        {
            damageMultiplier = 2;
        }
// --- NEW: HASTE / POUNCE CHECK ---
        bool hasHasteAttack = attacker.Template.SpecialAttacks.Any(a => a.AbilityName.Contains("Haste", System.StringComparison.OrdinalIgnoreCase)) ||
                              attacker.Template.SpecialQualities.Any(q => q.Contains("Haste", System.StringComparison.OrdinalIgnoreCase)) ||
                              attacker.Template.SpecialQualities.Contains("Pounce");

        if (hasHasteAttack)
        {
            GD.PrintRich($"[color=orange]{attacker.Name} uses Haste/Pounce to Full Attack on a charge![/color]");
            ResolveFullAttack(attacker, defender);
			// --- RAKE ON POUNCE ---
            // "including rake attacks if the creature also has the rake ability"
            if (attacker.Template.RakeAttacks != null && attacker.Template.RakeAttacks.Count > 0)
            {
                GD.PrintRich($"[color=orange]...and Rakes![/color]");
                foreach (var rake in attacker.Template.RakeAttacks)
                {
                    // Rake is a natural attack, passing it directly handles stats
                    ResolveSingleMeleeAttack(attacker, defender, 
                        attacker.Template.BaseAttackBonus + attacker.StrModifier + rake.MiscAttackBonus + chargeBonus, 
                        attacker.StrModifier, 
                        rake.AttackName, 
                        null, 
                        rake, 
                        1); // Multiplier 1 for natural attacks usually
                }
            }
            // ----------------------
            return;
        }
		// ------------------------ //
        int attackBonus = attacker.Template.BaseAttackBonus + attacker.StrModifier + chargeBonus;
        int damageBonus = attacker.StrModifier;
        string attackName = "Charge Attack";
        Item_SO weaponUsed = null;
        NaturalAttack naturalAttackUsed = null;

        if (weapon != null && weapon.ItemType == ItemType.Weapon)
        {
            attackName = $"{weapon.ItemName} (Charge)";
            weaponUsed = weapon;
        }
        else if (attacker.Template.MeleeAttacks != null && attacker.Template.MeleeAttacks.Any())
        {
             var attackToUse = attacker.Template.MeleeAttacks.FirstOrDefault(a => a.IsPrimary) ?? attacker.Template.MeleeAttacks.First();
             attackBonus = attacker.Template.BaseAttackBonus + attacker.StrModifier + attackToUse.MiscAttackBonus + chargeBonus;
             attackName = $"{attackToUse.AttackName} (Charge)";
             naturalAttackUsed = attackToUse;
        }

        ResolveSingleMeleeAttack(attacker, defender, attackBonus, damageBonus, attackName, weaponUsed, naturalAttackUsed, damageMultiplier);
    }
    
    public static void ResolveRangedAttack(CreatureStats attacker, CreatureStats defender, Item_SO weapon)
    {
        if (attacker == null || defender == null || weapon == null) return;
		SoundSystem.EmitCreatureActionSound(attacker, SoundActionType.Attack, isSneaking: false, durationSeconds: 1.0f);
        
        Ability_SO abilityUsed = null; 
        NaturalAttack naturalAttackData = null;
        int totalDamage = 0;
        
        int threatRange = weapon?.CriticalThreatRange ?? 20; 
        bool isImprovised = abilityUsed != null && abilityUsed.AbilityName == "Throw Improvised Weapon";
        
        var visibility = LineOfSightManager.GetVisibility(attacker, defender);
        
        var blockingIllusion = IllusionManager.Instance?.GetIllusionBlockingLoS(attacker.GlobalPosition, defender.GlobalPosition, attacker);
        if (!visibility.HasLineOfSight && blockingIllusion != null && blockingIllusion.HasDisbelieved(attacker))
        {
            visibility.HasLineOfEffect = true;
            visibility.ConcealmentMissChance = 50;
        }

        if (!visibility.HasLineOfEffect) return;

        if (attacker.MyEffects.HasCondition(Condition.Swallowed))
        {
            var swallowedCtrl = attacker.GetNodeOrNull<SwallowedController>("SwallowedController");
            if (swallowedCtrl != null && swallowedCtrl.Swallower == defender)
            {
                bool isValidWeapon = false;
                if (weapon != null)
                {
                    bool isLight = weapon.Handedness == WeaponHandedness.Light;
                    bool isSorP = weapon.DamageInfo.Any(d => d.DamageType == "Slashing" || d.DamageType == "Piercing");
                    if (isLight && isSorP) isValidWeapon = true;
                }
                else if (naturalAttackData != null)
                {
                    if (naturalAttackData.DamageInfo.Any(d => d.DamageType == "Slashing" || d.DamageType == "Piercing")) isValidWeapon = true;
                }

                if (!isValidWeapon)
                {
                    GD.PrintRich($"[color=orange]{attacker.Name} cannot attack {defender.Name}'s stomach without a light piercing or slashing weapon![/color]");
                    return;
                }
            }
        }

        var mirrorImageController = defender.GetNodeOrNull<MirrorImageController>("MirrorImageController");
        if (mirrorImageController != null && mirrorImageController.ImageCount > 0)
        {
            if (!attacker.MyEffects.HasCondition(Condition.Blinded) && !defender.MyEffects.HasCondition(Condition.Invisible) && visibility.HasLineOfSight)
            {
                int totalTargets = mirrorImageController.ImageCount + 1;
                int roll = Dice.Roll(1, totalTargets);
                if (roll > 1) 
                {
                    GD.PrintRich($"[color=cyan]{attacker.Name}'s ranged attack strikes a mirror image![/color]");
                    mirrorImageController.DestroyImage("a direct hit");
                    return; 
                }
            }
        }

        int attackBonus = attacker.Template.BaseAttackBonus + attacker.DexModifier + attacker.GetSizeModifier();

        // Weather penalties are skipped when a protective status effect says precipitation penalties are ignored.
        if (WeatherManager.Instance != null && WeatherManager.Instance.CurrentWeather != null)
        {
            bool ignoreWeatherPenalty = attacker.MyEffects != null && attacker.MyEffects.IgnoresPrecipitationCombatPenalties();
            
            string wName = WeatherManager.Instance.CurrentWeather.WeatherName.ToLower();
            if ((wName.Contains("snow") || wName.Contains("blizzard")) && (attacker.Template.HasSnowsight || (attacker.MyEffects != null && attacker.MyEffects.HasCondition(Condition.Snowsight))))
            {
                ignoreWeatherPenalty = true;
            }

            if (!ignoreWeatherPenalty)
            {
                attackBonus += WeatherManager.Instance.CurrentWeather.RangedAttackPenalty;
            }
        }
        
        if (weapon.IsThrowableRock && attacker.Template.SpecialAttacks.Any(sa => sa.AbilityName.Equals("Rock Throwing", System.StringComparison.OrdinalIgnoreCase)))
        {
            attackBonus = attacker.Template.BaseAttackBonus + attacker.StrModifier + attacker.GetSizeModifier() + 1;
        }

        attackBonus += CalculateFiringIntoMeleePenalty(attacker, defender);

        float distance = attacker.GlobalPosition.DistanceTo(defender.GlobalPosition);
        float rangeIncrement = isImprovised ? 10f : weapon.RangeIncrement;
        if (rangeIncrement > 0)
        {
            int numIncrements = Mathf.FloorToInt((distance - 0.1f) / rangeIncrement);
            if (numIncrements > 0)
            {
                int rangePenalty = numIncrements * -2;
                attackBonus += rangePenalty;
            }
        }
        
        if (isImprovised && !attacker.HasFeat("Throw Anything")) attackBonus -= 4;
        
        int rollResult = RollManager.Instance.MakeD20Roll(attacker);
        int totalAttackRoll = rollResult + attackBonus;
        int finalAC = CombatCalculations.CalculateFinalAC(defender, false, visibility.CoverBonusToAC, attacker);
        
        if (rollResult == 1 && weapon != null && weapon.HasFragileFeature)
        {
            var weaponInstance = attacker.MyInventory?.GetEquippedItemInstance(EquipmentSlot.MainHand);
            if (weaponInstance != null)
            {
                if (weaponInstance.IsBroken)
                {
                    attacker.MyInventory.DropItemFromSlot(EquipmentSlot.MainHand, attacker.GlobalPosition);
                }
                else
                {
                    weaponInstance.TakeDamage((weaponInstance.ItemData.MaxHP / 2) + 1);
                }
            }
        }

        if (mirrorImageController != null && mirrorImageController.ImageCount > 0)
        {
            int missAmount = finalAC - totalAttackRoll;
            if (missAmount > 0 && missAmount <= 5)
            {
                mirrorImageController.DestroyImage("a near miss");
            }
        }

        string weaponNameForFeats = weapon?.ItemName ?? naturalAttackData?.AttackName ?? "Unarmed Strike";
        if (attacker.HasFeat("Improved Critical", weaponNameForFeats) && !(weapon?.HasKeenProperty ?? false))
        {
            int rangeToDouble = 21 - threatRange;
            threatRange = 21 - (rangeToDouble * 2);
        }
        
        bool isHit = (totalAttackRoll >= finalAC && rollResult != 1) || rollResult == 20;
        bool isCriticalThreat = rollResult >= threatRange;

        GD.Print($"{attacker.Name} fires {weapon.ItemName} at {defender.Name}! Rolls {totalAttackRoll} vs AC {finalAC}.");
        
        if (isHit && visibility.ConcealmentMissChance > 0)
        {
            if (Dice.Roll(1, 100) <= visibility.ConcealmentMissChance) isHit = false;
        }

        if (isHit)
        {
            var defenderInv = defender.GetNodeOrNull<InventoryController>("InventoryController");
            var dMainHand = defenderInv?.GetEquippedItem(EquipmentSlot.MainHand);
            var dOffHand = defenderInv?.GetEquippedItem(EquipmentSlot.OffHand);
            var dShield = defenderInv?.GetEquippedItem(EquipmentSlot.Shield);
            bool hasFreeHand = (dMainHand == null || dMainHand.Handedness != WeaponHandedness.TwoHanded) && dOffHand == null && dShield == null;

            if (defender.Template.DefensiveAbilities != null && defender.Template.DefensiveAbilities.Contains("Rock Catching") && !defender.IsFlatFooted && !defender.HasUsedRockCatchingThisRound && weapon != null && weapon.IsThrowableRock && hasFreeHand)
            {
                if (ResolveRockCatchingAttempt(defender, attacker, weapon)) return;
            }

            bool isCriticalHit = false;
            if (isCriticalThreat)
            {
                int confirmationBonus = attackBonus;
                if (attacker.HasFeat("Critical Focus")) confirmationBonus += 4;
               
                int confirmationRoll = Dice.Roll(1, 20) + confirmationBonus;
                if (confirmationRoll >= finalAC)
                {
                    isCriticalHit = true;
                    GD.PrintRich($"[color=yellow]CRITICAL HIT CONFIRMED![/color]");
                    
                    if (attacker.HasFeat("Sickening Critical"))
                    {
                        var sickenedEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Sickened_Effect.tres");
                        if (sickenedEffect != null)
                        {
                            var instance = (StatusEffect_SO)sickenedEffect.Duplicate();
                            instance.DurationInRounds = 10;
                            defender.MyEffects.AddEffect(instance, attacker);
                        }
                    }
                }
            }

            int flatDamageBonus = 0;
            if (weapon.WeaponType == WeaponType.Thrown || isImprovised)
            {
                flatDamageBonus += attacker.StrModifier;
            }
             
            if (weapon.IsThrowableRock && attacker.Template.SpecialAttacks.Any(sa => sa.AbilityName.Equals("Rock Throwing", System.StringComparison.OrdinalIgnoreCase)))
            {
                var slamAttack = attacker.Template.MeleeAttacks.FirstOrDefault(a => a.AttackName.ToLower().Contains("slam"));
                int calculatedDamage = 0;

                if (slamAttack != null)
                {
                    var slamDmgInfo = slamAttack.DamageInfo.FirstOrDefault();
                    if (slamDmgInfo != null) calculatedDamage = 2 * (Dice.Roll(slamDmgInfo.DiceCount, slamDmgInfo.DieSides) + slamDmgInfo.FlatBonus);
                }

                var rockDmgInfo = weapon.DamageInfo.FirstOrDefault();
                int rockBaseDamage = 0;
                if (rockDmgInfo != null) rockBaseDamage = Dice.Roll(rockDmgInfo.DiceCount, rockDmgInfo.DieSides) + rockDmgInfo.FlatBonus;

                totalDamage = Mathf.Max(calculatedDamage, rockBaseDamage);
                flatDamageBonus = Mathf.FloorToInt(attacker.StrModifier * 1.5f);
            }
            else if (weapon.WeaponType == WeaponType.Thrown)
            {
                flatDamageBonus += attacker.StrModifier;
            }
            else if (weapon.WeaponType == WeaponType.Projectile && weapon.IsCompositeBow)
            {
                if (attacker.StrModifier < 0) flatDamageBonus += attacker.StrModifier;
                else flatDamageBonus += Mathf.Min(attacker.StrModifier, weapon.StrengthRating);
            }

            int critMultiplier = isCriticalHit ? weapon.CriticalMultiplier : 1;
            if (isImprovised && isCriticalHit) critMultiplier = 2;

            if (!weapon.IsThrowableRock || !attacker.Template.SpecialAttacks.Any(sa => sa.AbilityName.Equals("Rock Throwing", System.StringComparison.OrdinalIgnoreCase)))
            {
                for (int i = 0; i < critMultiplier; i++)
                {
                    foreach (var dmg in weapon.DamageInfo.Where(d => d.MultipliesOnCrit))
                    {
                        totalDamage += Dice.Roll(dmg.DiceCount, dmg.DieSides) + dmg.FlatBonus + flatDamageBonus;
                    }
                }
                foreach (var dmg in weapon.DamageInfo.Where(d => !d.MultipliesOnCrit))
                {
                    totalDamage += Dice.Roll(dmg.DiceCount, dmg.DieSides) + dmg.FlatBonus;
                }
				
                if (isCriticalHit)
                {
                    foreach (var critBonus in GetCriticalBonusDamageFromWeaponQualities(weapon))
                    {
                        totalDamage += Dice.Roll(critBonus.DiceCount, critBonus.DieSides);
                    }
                }
            }
            else
            {
                totalDamage += flatDamageBonus;
            }

            totalDamage = Mathf.Max(1, totalDamage);
            string primaryDamageType = weapon.DamageInfo.First().DamageType;
            defender.TakeDamage(totalDamage, primaryDamageType, attacker, weapon, null);
			
            foreach (var extraDamage in GetExtraRangedDamageFromEffects(attacker, weapon, critMultiplier))
            {
                int bonusDamage = Dice.Roll(extraDamage.DiceCount, extraDamage.DieSides) + extraDamage.FlatBonus;
                if (bonusDamage > 0)
                {
                    defender.TakeDamage(bonusDamage, extraDamage.DamageType, attacker, weapon, null);
                }
            }
        }
    }

    private static IEnumerable<DamageInfo> GetExtraRangedDamageFromEffects(CreatureStats attacker, Item_SO weapon, int critMultiplier)
    {
        if (attacker?.MyEffects == null || weapon == null) yield break;

        foreach (var activeEffect in attacker.MyEffects.ActiveEffects)
        {
            if (activeEffect == null || activeEffect.IsSuppressed || activeEffect.EffectData == null) continue;

            var effectData = activeEffect.EffectData;
            if (!effectData.GrantsExtraDamageOnRangedAttacks) continue;

            bool allowProjectile = !effectData.RestrictExtraRangedDamageToProjectileWeapons || weapon.WeaponType == WeaponType.Projectile;
            bool allowThrown = !effectData.RestrictExtraRangedDamageToThrownWeapons || weapon.WeaponType == WeaponType.Thrown;
            if (!allowProjectile || !allowThrown) continue;

            if (effectData.ExtraRangedDamage == null || effectData.ExtraRangedDamage.DiceCount <= 0 || effectData.ExtraRangedDamage.DieSides <= 0) continue;

            int finalDiceCount = effectData.ExtraRangedDamage.DiceCount;
            if (effectData.ExtraRangedDamageMultipliesOnCritical && critMultiplier > 1)
            {
                finalDiceCount *= critMultiplier;
            }

            yield return new DamageInfo
            {
                DiceCount = finalDiceCount,
                DieSides = effectData.ExtraRangedDamage.DieSides,
                FlatBonus = effectData.ExtraRangedDamage.FlatBonus,
                DamageType = effectData.ExtraRangedDamage.DamageType,
                MultipliesOnCrit = false
            };
        }
    }
	
    public static void ResolveCoupDeGrace(CreatureStats attacker, CreatureStats defender)
    {
        if (attacker == null || defender == null || !defender.MyEffects.HasCondition(Condition.Helpless)) return;

        GD.PrintRich($"[color=red]{attacker.Name} performs a Coup de Grace on {defender.Name}![/color]");

        int massiveBonus = 100;
        var weapon = attacker.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
        
        if (weapon != null)
        {
            ResolveSingleMeleeAttack(attacker, defender, attacker.Template.BaseAttackBonus + attacker.StrModifier + massiveBonus, attacker.StrModifier, "Coup de Grace", weapon);
        }
        else
        {
            var naturalAttack = attacker.Template.MeleeAttacks.FirstOrDefault();
            int damageBonus = (naturalAttack != null && attacker.Template.MeleeAttacks.Count == 1) ? Mathf.FloorToInt(attacker.StrModifier * 1.5f) : attacker.StrModifier;
            ResolveSingleMeleeAttack(attacker, defender, attacker.Template.BaseAttackBonus + attacker.StrModifier + massiveBonus, damageBonus, "Coup de Grace", null, naturalAttack);
        }

        if (defender != null && defender.CurrentHP > -defender.Template.Constitution)
        {
            int estimatedDamage = (weapon != null) ? (Dice.Roll(1, 8) * 2 + attacker.StrModifier * 2) : (Dice.Roll(1, 6) * 2 + attacker.StrModifier * 2);
            int dc = 10 + estimatedDamage;
            
            if (weapon != null && weapon.HasDeadlyFeature) dc += 4;

            int fortSave = Dice.Roll(1, 20) + defender.GetFortitudeSave(attacker);
            GD.Print($"{defender.Name} must make a DC {dc} Fortitude save to survive. Rolls {fortSave}.");

            if (fortSave < dc)
            {
                GD.PrintRich("[color=black]Save failed! The Coup de Grace is lethal![/color]");
                defender.TakeDamage(9999, "Death");
            }
        }
    }
	
    internal static int CalculateFiringIntoMeleePenalty(CreatureStats attacker, CreatureStats defender)
    {
        if (attacker.Template.Feats != null && attacker.Template.Feats.Any(f => f.Feat.FeatName == "Precise Shot")) return 0;
        
        var allies = TurnManager.Instance?.GetAllCombatants()?.Where(c => c != attacker && c.IsInGroup(attacker.GetGroups()[0].ToString())).ToList();
        if (allies == null || !allies.Any()) return 0;
        
        var engagedAllies = allies.Where(ally => CombatCalculations.IsThreatening(ally, defender) || CombatCalculations.IsThreatening(defender, ally)).ToList();
        if (!engagedAllies.Any()) return 0;
        
        int largestSizeDifference = engagedAllies.Max(ally => (int)defender.Template.Size - (int)ally.Template.Size);
        if (largestSizeDifference >= 3) return 0;
        if (largestSizeDifference == 2) return -2;
        return -4;
    }
	
    private static void ResolveSingleMeleeAttack(CreatureStats attacker, CreatureStats defender, int specificAttackBonus, int specificDamageBonus, string attackName, Item_SO weapon = null, NaturalAttack naturalAttackData = null, int baseDamageMultiplier = 1, bool isOffHand = false)
    {
        if (attacker == null || defender == null) return;
        
        if (!CombatManager.CheckSanctuary(attacker, defender)) return;
        
        var stateController = attacker.GetNodeOrNull<CombatStateController>("CombatStateController");
        if (attacker.MyEffects.HasCondition(Condition.Blinded) && stateController != null)
        {
            LocationStatus locStatus = stateController.GetLocationStatus(defender);
            if (locStatus < LocationStatus.Pinpointed)
            {
                var startNode = GridManager.Instance.NodeFromWorldPoint(attacker.GlobalPosition);
                var validNeighbors = GridManager.Instance.GetNeighbours(startNode).Where(n => CombatCalculations.IsNodeWalkable(attacker, startNode, n)).ToList();
                if (validNeighbors.Any())
                {
                    GridNode randomTargetNode = validNeighbors[GD.RandRange(0, validNeighbors.Count - 1)];
                    if (GridManager.Instance.NodeFromWorldPoint(defender.GlobalPosition) != randomTargetNode)
                    {
                        GD.Print($"...attacks {randomTargetNode.worldPosition}, but {defender.Name} isn't there. Miss!");
                        return; 
                    }
                }
            }
        }

        if (defender.MyVeil != null && !CombatMemory.HasDisbelievedIllusion(attacker, defender))
        {
            CombatMagic.ResolveGlamerDisbelief(attacker, defender);
        }

        attacker.GetNodeOrNull<StealthController>("StealthController")?.BreakStealth();
        
        for (int i = attacker.MyEffects.ActiveEffects.Count - 1; i >= 0; i--)
        {
            var effect = attacker.MyEffects.ActiveEffects[i];
            if (effect.EffectData.ConditionApplied == Condition.Invisible && effect.EffectData.BreaksOnAttack)
            {
                attacker.MyEffects.RemoveEffect(effect.EffectData.EffectName);
            }
            else if (effect.EffectData.ConditionApplied == Condition.Sanctuary)
            {
                attacker.MyEffects.RemoveEffect(effect.EffectData.EffectName);
            }
        }

        var defenderEffects = defender.GetNodeOrNull<StatusEffectController>("StatusEffectController");

        // Protection from Evil Check
        if (defenderEffects != null && defenderEffects.HasSpecialDefense(SpecialDefense.BlockContact_EvilSummoned))
        {
            if (attacker.IsSummoned && attacker.Template.Alignment.Contains("Evil") && naturalAttackData != null)
            {
                var protectionEffect = defender.MyEffects.ActiveEffects.FirstOrDefault(e => e.EffectData.SpecialDefenses.Contains(SpecialDefense.BlockContact_EvilSummoned));
                if (protectionEffect != null && protectionEffect.SourceCreature != null)
                {
                    int casterLevelCheck = Dice.Roll(1, 20) + protectionEffect.SourceCreature.Template.CasterLevel;
                    if (casterLevelCheck < attacker.Template.SpellResistance)
                    {
                        GD.PrintRich($"[color=cyan]{defender.Name}'s Protection from Evil blocks attack! (SR failed: {casterLevelCheck})[/color]");
                        return;
                    }
                }
                else if (attacker.Template.SpellResistance == 0)
                {
                    GD.PrintRich($"[color=cyan]{defender.Name}'s Protection from Evil blocks the attack![/color]");
                    return;
                }
            }
        }
        
        // Attacker's own Protection from Evil break check
        var attackerEffects = attacker.GetNodeOrNull<StatusEffectController>("StatusEffectController");
        if (attackerEffects != null && attackerEffects.HasSpecialDefense(SpecialDefense.BlockContact_EvilSummoned))
        {
             if (defender.IsSummoned && defender.Template.Alignment.Contains("Evil"))
             {
                GD.PrintRich($"[color=orange]{attacker.Name} attacks a blocked creature, breaking Protection from Evil![/color]");
                var pfeEffect = attacker.MyEffects.ActiveEffects.FirstOrDefault(e => e.EffectData.SpecialDefenses.Contains(SpecialDefense.BlockContact_EvilSummoned));
                if (pfeEffect != null) attacker.MyEffects.RemoveEffect(pfeEffect.EffectData.EffectName);
             }
        }
        
        var mirrorImageController = defender.GetNodeOrNull<MirrorImageController>("MirrorImageController");
        if (mirrorImageController != null && mirrorImageController.ImageCount > 0)
        {
            var attackerVisibility = LineOfSightManager.GetVisibility(attacker, defender);
            if (!attacker.MyEffects.HasCondition(Condition.Blinded) && !defender.MyEffects.HasCondition(Condition.Invisible) && attackerVisibility.HasLineOfSight)
            {
                int totalTargets = mirrorImageController.ImageCount + 1;
                int roll = Dice.Roll(1, totalTargets);
                if (roll > 1) 
                {
                    GD.PrintRich($"[color=cyan]{attacker.Name}'s attack strikes a mirror image![/color]");
                    mirrorImageController.DestroyImage("a direct hit");
                    return; 
                }
            }
        }

        var visibility = LineOfSightManager.GetVisibility(attacker, defender);
        if (!visibility.HasLineOfEffect) return;

        // --- 1. Calculate Total Attack Bonus ---
        int totalAttackBonus = CalculateTotalMeleeAttackBonus(attacker, defender, specificAttackBonus, weapon, naturalAttackData);

        // --- 2. Make Roll ---
        int rollResult = RollManager.Instance.MakeD20Roll(attacker);
        int totalAttackRoll = rollResult + totalAttackBonus;
       bool isAttackerIncorporeal = attacker.MyEffects != null && attacker.MyEffects.HasCondition(Condition.Incorporeal);
        bool isDefenderIncorporeal = defender.MyEffects != null && defender.MyEffects.HasCondition(Condition.Incorporeal);
        bool isEffectiveTouch = isAttackerIncorporeal && !isDefenderIncorporeal;
        
        int finalAC = CombatCalculations.CalculateFinalAC(defender, isEffectiveTouch, visibility.CoverBonusToAC, attacker);

        if (mirrorImageController != null && mirrorImageController.ImageCount > 0)
        {
            int missAmount = finalAC - totalAttackRoll;
            if (missAmount > 0 && missAmount <= 5)
            {
                mirrorImageController.DestroyImage("a near miss");
            }
        }

        int baseThreatRange = weapon?.CriticalThreatRange ?? naturalAttackData?.CriticalThreatRange ?? 20;
        string wName = weapon?.ItemName ?? naturalAttackData?.AttackName ?? "Unarmed";
        
        if (attacker.HasFeat("Improved Critical", wName) && !(weapon?.HasKeenProperty ?? false))
        {
            int rangeToDouble = 21 - baseThreatRange;
            baseThreatRange = 21 - (rangeToDouble * 2);
        }
        
        bool isHit = (totalAttackRoll >= finalAC && rollResult != 1) || rollResult == 20;
        bool isCriticalThreat = rollResult >= baseThreatRange;

        if (defender.MyEffects.HasCondition(Condition.Gaseous)) isCriticalThreat = false;

        GD.Print($"{attacker.Name} attacks {defender.Name} with {wName}! Rolls {totalAttackRoll} vs AC {finalAC}.");
        
        if (isHit && visibility.ConcealmentMissChance > 0)
        {
            if (Dice.Roll(1, 100) <= visibility.ConcealmentMissChance) isHit = false;
        }
        
        float distance = attacker.GlobalPosition.DistanceTo(defender.GlobalPosition);
        if (isHit && !visibility.HasLineOfSight && (attacker.Template.BlindsenseRange > 0 && distance <= attacker.Template.BlindsenseRange))
        {
            if (Dice.Roll(1, 100) <= 50) isHit = false;
        }

        if (isHit)
        {
            int damageMultiplier = Mathf.Max(1, baseDamageMultiplier);
            if (naturalAttackData != null && attacker.MyEffects.HasCondition(Condition.Charging) && ShouldApplyChargeNaturalAttackMultiplier(attacker, naturalAttackData))
            {
                damageMultiplier *= Mathf.Max(1, naturalAttackData.ChargeDamageMultiplier);
            }

            bool isCriticalHit = false;
            if (isCriticalThreat)
            {
                int confirmationBonus = totalAttackBonus;
                if (attacker.HasFeat("Critical Focus")) confirmationBonus += 4;

                int confirmationRoll = Dice.Roll(1, 20) + confirmationBonus;
                if (confirmationRoll >= finalAC)
                {
                    isCriticalHit = true;
                    GD.PrintRich($"[color=yellow]CRITICAL HIT CONFIRMED![/color]");
                    
                    if (attacker.HasFeat("Sickening Critical"))
                    {
                        var sickenedEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Sickened_Effect.tres");
                        if (sickenedEffect != null)
                        {
                            var instance = (StatusEffect_SO)sickenedEffect.Duplicate();
                            instance.DurationInRounds = 10;
                            defender.MyEffects.AddEffect(instance, attacker);
                        }
                    }
                }
            }

            // --- 3. Gather Damage Components ---
            var allDamageComponents = new List<DamageInfo>();
            var weaponInstance = attacker.MyInventory?.GetEquippedItemInstance(EquipmentSlot.MainHand);
            
            if (weaponInstance != null && weapon == weaponInstance.ItemData)
            {
                var (sizedDiceCount, sizedDieSides) = WeaponDamageConverter.GetSizedDamage(weaponInstance, attacker.Template.Size);
                var sizedDamage = new DamageInfo { DiceCount = sizedDiceCount, DieSides = sizedDieSides, DamageType = weapon.DamageInfo[0].DamageType, MultipliesOnCrit = true };
                allDamageComponents.Add(sizedDamage);
                allDamageComponents.AddRange(weapon.DamageInfo.Skip(1));
            }
            if (weapon != null && weapon.DamageInfo.Any()) allDamageComponents.AddRange(weapon.DamageInfo);
            else if (naturalAttackData != null && naturalAttackData.DamageInfo.Any()) allDamageComponents.AddRange(naturalAttackData.DamageInfo);
            else allDamageComponents.Add(new DamageInfo { DiceCount = 1, DieSides = attacker.GetUnarmedDamageDieSides(), DamageType = "Bludgeoning" });

            int critMultiplier = isCriticalHit ? (weapon?.CriticalMultiplier ?? naturalAttackData?.CriticalMultiplier ?? 2) : 1;
            
            if (weaponInstance != null && weaponInstance.IsBroken)
            {
                if(isCriticalHit && rollResult == 20) critMultiplier = 2;
                else { isCriticalHit = false; critMultiplier = 1; }
            }

             if (weapon != null && isCriticalHit)
            {
                allDamageComponents.AddRange(GetCriticalBonusDamageFromWeaponQualities(weapon));
            }

			// --- 4. APPLY DAMAGE ---
             foreach (var dmgInfo in allDamageComponents)
            {
                int damageToApply = 0;
                int flatBonusForComponent = 0;
                int extraVitalDice = 0;
                
                if (attacker.IsUsingVitalStrike && dmgInfo == allDamageComponents[0])
                {
                    int multiplier = 1;
                    if (attacker.HasFeat("Greater Vital Strike")) multiplier = 3;
                    else if (attacker.HasFeat("Improved Vital Strike")) multiplier = 2;
                    extraVitalDice = dmgInfo.DiceCount * multiplier;
                }

                if (dmgInfo.MultipliesOnCrit)
                {
                    flatBonusForComponent = CalculateTotalMeleeDamageBonus(attacker, specificDamageBonus, weapon, naturalAttackData, isOffHand);
                    for (int i = 0; i < critMultiplier; i++)
                    {
                        damageToApply += Dice.Roll(dmgInfo.DiceCount, dmgInfo.DieSides) + dmgInfo.FlatBonus + flatBonusForComponent;
                    }
                    if (extraVitalDice > 0) damageToApply += Dice.Roll(extraVitalDice, dmgInfo.DieSides);
                }
                else
                {
                    damageToApply = Dice.Roll(dmgInfo.DiceCount, dmgInfo.DieSides) + dmgInfo.FlatBonus;
                }

                if (defender.MyEffects.HasCondition(Condition.Charging) && weapon != null && weapon.HasBraceFeature)
                {
                    damageToApply *= 2;
                }

                if (dmgInfo.DamageType.Equals("None", System.StringComparison.OrdinalIgnoreCase))
                {
                    damageToApply = 0;
                }
                else
                {
                    damageToApply = Mathf.Max(1, damageToApply * damageMultiplier);
                    damageToApply = Mathf.Max(1, Mathf.CeilToInt(damageToApply * attacker.GetCorruptionDamageMultiplier()));
                }

                if (damageToApply > 0)
                {

                defender.TakeDamage(damageToApply, dmgInfo.DamageType, attacker, weapon, naturalAttackData, (finalDamage) => {
                    if (attacker.IsInGroup("Enemy"))
                    {
                        AITacticalMatrix.RecordWeaponEffectivenessFeedback(defender, weapon, finalDamage, damageToApply);
                    }
                    var swallowedCtrl = attacker.GetNodeOrNull<SwallowedController>("SwallowedController");
                    if (swallowedCtrl != null && swallowedCtrl.Swallower == defender)
                    {
                        swallowedCtrl.RecordCutDamage(finalDamage);
                    }
                }, false); // Pass false for bypassResistances
				     }

                if (defender == null || defender.CurrentHP <= 0) break;
            }

            string weaponNameForBonus = weapon?.ItemName ?? naturalAttackData?.AttackName ?? "Unarmed Strike";
            bool hasPoison = (weapon != null && weapon.DamageInfo.Any(d => d.DamageType.ToLower().Contains("poison"))) ||
                             (naturalAttackData != null && naturalAttackData.SpecialQualities.Any(s => s.ToLower().Contains("poison"))) ||
                             (weaponNameForBonus.ToLower().Contains("plus poison"));
			 if (attacker.MyEffects.HasEffect("Natural Poison Suppressed"))
            {
                hasPoison = false;
            }

            if (hasPoison)
            {
                var poisonAbility = attacker.Template.KnownAbilities.FirstOrDefault(a => a.AbilityName.Equals("Poison", System.StringComparison.OrdinalIgnoreCase));
                if (poisonAbility != null)
                {
                    GD.PrintRich($"[color=green]Applying Poison from {attacker.Name}...[/color]");
                    var context = new EffectContext { Caster = attacker, PrimaryTarget = defender };
                    foreach(var effect in poisonAbility.EffectComponents) effect.ExecuteEffect(context, poisonAbility, new Dictionary<CreatureStats, bool>());
                }
            }

            if (defender != null && naturalAttackData != null && naturalAttackData.HasGrab)
            {
			CombatManeuvers.ResolveGrapple(attacker, defender, isFreeAction: true, initiatingAttack: naturalAttackData);
            }

            if (naturalAttackData != null && naturalAttackData.OnHitAbility != null)
            {
				if (attacker.MyUsage != null && !attacker.MyUsage.HasUsesRemaining(naturalAttackData.OnHitAbility)) goto SkipOnHit;
                attacker.MyUsage?.ConsumeUse(naturalAttackData.OnHitAbility);
                 // Logic to check Immunity (e.g. for Wrenching Spasms once per day per target) could be added here via Effect filters
                 GD.PrintRich($"[color=purple]{attacker.Name}'s {naturalAttackData.AttackName} triggers {naturalAttackData.OnHitAbility.AbilityName}![/color]");
                 _ = CombatManager.ResolveAbility(attacker, defender, defender, defender.GlobalPosition, naturalAttackData.OnHitAbility, false);
            }
SkipOnHit:
				
			if (isCriticalHit && naturalAttackData != null && naturalAttackData.OnCritAbility != null)
            {
                if (attacker.MyUsage == null || attacker.MyUsage.HasUsesRemaining(naturalAttackData.OnCritAbility))
                {
                    attacker.MyUsage?.ConsumeUse(naturalAttackData.OnCritAbility);
                    GD.PrintRich($"[color=yellow]{attacker.Name}'s {naturalAttackData.AttackName} scores a critical hit and triggers {naturalAttackData.OnCritAbility.AbilityName}![/color]");
                    _ = CombatManager.ResolveAbility(attacker, defender, defender, defender.GlobalPosition, naturalAttackData.OnCritAbility, false);
                }
            }
            if (isCriticalHit && defender != null && weapon != null && weapon.HasGrappleFeature)
            {
                GD.PrintRich($"[color=yellow]{weapon.ItemName} scores a critical hit! Attempting a free grapple action.[/color]");
                 CombatManeuvers.ResolveGrapple(attacker, defender, isFreeAction: true, initiatingAttack: null); // Weapon grapple doesn't use NaturalAttack data
            }

            if (attacker.IsInGroup("Player")) CombatMemory.RecordPlayerDamage(attacker, 0); 
            if (attacker.IsInGroup("Enemy") && (defender == null || defender.CurrentHP <= 0)) AITacticalMatrix.RecordFeedback(FeedbackType.KilledTarget, 5f);
        }
        else
        {
             GD.PrintRich("[color=red]Miss![/color]");
        }
    }
	
    private static bool ShouldApplyChargeNaturalAttackMultiplier(CreatureStats attacker, NaturalAttack naturalAttackData)
    {
        if (attacker == null || naturalAttackData == null) return false;

        if (naturalAttackData.ChargeDamageMultiplier <= 1) return false;

        string requiredSpecialAttack = naturalAttackData.ChargeMultiplierRequiredSpecialAttack?.Trim();
        if (string.IsNullOrEmpty(requiredSpecialAttack)) return true;

        if (attacker.Template?.SpecialAttacks == null) return false;

        return attacker.Template.SpecialAttacks.Any(sa =>
            sa != null &&
            !string.IsNullOrWhiteSpace(sa.AbilityName) &&
            sa.AbilityName.Equals(requiredSpecialAttack, System.StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<DamageInfo> GetCriticalBonusDamageFromWeaponQualities(Item_SO weapon)
    {
        if (weapon == null || weapon.SpecialQualities == null || !weapon.SpecialQualities.Any())
        {
            yield break;
        }

        int burstDiceCount = Mathf.Max(1, weapon.CriticalMultiplier - 1);

        foreach (string quality in weapon.SpecialQualities)
        {
            if (string.IsNullOrWhiteSpace(quality)) continue;

            string normalized = quality.Trim().ToLower();
            if (!ElementalBurstDamageTypes.TryGetValue(normalized, out string damageType)) continue;

            yield return new DamageInfo
            {
                DiceCount = burstDiceCount,
                DieSides = 10,
                DamageType = damageType,
                MultipliesOnCrit = false
            };
        }
    }
	
    private static int CalculateTotalMeleeAttackBonus(CreatureStats attacker, CreatureStats defender, int specificAttackBonus, Item_SO weapon, NaturalAttack naturalAttackData)
    {
        var attackerEffects = attacker.MyEffects;
        var attackerInv = attacker.MyInventory;
        string weaponNameForBonus = weapon?.ItemName ?? naturalAttackData?.AttackName ?? "Unarmed Strike";
        
        int totalBonus = specificAttackBonus;
        totalBonus += attacker.GetSizeModifier();
        
        var weaponInstance = attackerInv?.GetEquippedItemInstance(EquipmentSlot.MainHand);
        if (weapon != null && weapon.IsImprovised && !attacker.HasFeat("Catch Off-Guard"))
        {
            totalBonus -= 4;
        }
        if (weaponInstance != null && weapon == weaponInstance.ItemData)
        {
            int sizeDifference = System.Math.Abs((int)attacker.Template.Size - (int)weaponInstance.DesignedForSize);
            if (sizeDifference > 0) totalBonus -= sizeDifference * 2;
        }
		
		 if (attackerEffects != null && attackerEffects.HasCondition(Condition.Incorporeal))
        {
            totalBonus -= attacker.StrModifier;
            totalBonus += attacker.DexModifier;
        }
        
        // DATA DRIVEN: Get all bonuses/penalties from Status Effects (Bless, Shaken, Sickened, etc.)
        totalBonus += attackerEffects?.GetTotalModifier(StatToModify.AttackRoll, defender, weaponNameForBonus) ?? 0;
        
        totalBonus += attackerInv?.GetTotalStatModifierFromEquipment(StatToModify.AttackRoll) ?? 0;
        
        if(naturalAttackData != null) totalBonus += naturalAttackData.EnhancementBonus;
        if (CombatCalculations.IsFlankedBy(defender, attacker)) totalBonus += 2;
        
        var optionsController = attacker.GetNodeOrNull<CombatOptionsController>("CombatOptionsController");
        if (optionsController != null && optionsController.IsOptionActive("Power Attack"))
        {
            var activeMods = optionsController.GetActiveOptionModifications(attacker);
            var powerAttackMod = activeMods.FirstOrDefault(m => m.StatToModify == StatToModify.AttackRoll);
            if (powerAttackMod != null) totalBonus += powerAttackMod.ModifierValue;
        }

        if (weaponInstance != null && weaponInstance.IsBroken && weapon == weaponInstance.ItemData) totalBonus -= 2;

        if (attackerEffects != null)
        {
            // SANITIZED: Removed Shaken, Sickened, Dazzled, LightBlindness checks.
            // Prone remains as a contextual hard check for melee only (-4).
            if (attackerEffects.HasCondition(Condition.Prone)) totalBonus -= 4;
            
            // Entangled and Grappled apply -2 via StatusEffect_SO if generated correctly, or can be kept here if using legacy flag logic.
            // Assuming CoreRulesGenerator adds Attack penalties for them.
            
            if (attackerEffects.HasCondition(Condition.Underwater))
            {
                 if (!attackerEffects.HasCondition(Condition.FreedomOfMovement))
                {
                     if (weapon != null && (weapon.DamageInfo.First().DamageType == "Slashing" || weapon.DamageInfo.First().DamageType == "Bludgeoning"))
                     {
                         totalBonus -= 2;
                     }
                }
            }
        }

        var attackerNode = GridManager.Instance.NodeFromWorldPoint(attacker.GlobalPosition);
        if (attacker.Template.HasLightSensitivity && GridManager.Instance.GetEffectiveLightLevel(attackerNode) >= 3)
        {
            int penalty = -1;
            if (GridManager.Instance.IsNodeInMythicDaylight(attackerNode)) penalty *= 2;
            totalBonus += penalty;
        }
        
        return totalBonus;
    }

    private static int CalculateTotalMeleeDamageBonus(CreatureStats attacker, int specificDamageBonus, Item_SO weapon, NaturalAttack naturalAttackData, bool isOffHand)
    {
        var attackerEffects = attacker.MyEffects;
        var attackerInv = attacker.MyInventory;
        
        int totalBonus = isOffHand ? Mathf.FloorToInt(attacker.StrModifier * 0.5f) : specificDamageBonus;
         if (attackerEffects != null && attackerEffects.HasCondition(Condition.Incorporeal))
        {
            totalBonus = 0;
        }

        // DATA DRIVEN: Get damage penalties (Sickened) via GetTotalModifier
        string weaponNameForBonus = weapon?.ItemName ?? naturalAttackData?.AttackName ?? "Unarmed Strike";
        totalBonus += (attackerEffects?.GetTotalModifier(StatToModify.MeleeDamage, null, weaponNameForBonus) ?? 0);
        
        totalBonus += (attackerInv?.GetTotalStatModifierFromEquipment(StatToModify.MeleeDamage) ?? 0);

        if (naturalAttackData != null) totalBonus += naturalAttackData.EnhancementBonus;

        var weaponInstance = attacker.MyInventory?.GetEquippedItemInstance(EquipmentSlot.MainHand);
        if (weaponInstance != null && weaponInstance.IsBroken && weapon == weaponInstance.ItemData) totalBonus -= 2;

        var optionsController = attacker.GetNodeOrNull<CombatOptionsController>("CombatOptionsController");
        if (optionsController != null && optionsController.IsOptionActive("Power Attack"))
        {
            var activeMods = optionsController.GetActiveOptionModifications(attacker);
            var powerAttackDamageMod = activeMods.FirstOrDefault(m => m.StatToModify == StatToModify.MeleeDamage);
            if (powerAttackDamageMod != null)
            {
                float damageBonus = powerAttackDamageMod.ModifierValue;
                bool isTwoHanded = (weapon != null && weapon.Handedness == WeaponHandedness.TwoHanded);
                bool isPrimaryNatural1_5xStr = (naturalAttackData != null && naturalAttackData.IsPrimary && attacker.Template.MeleeAttacks.Count == 1);
                
                if (isTwoHanded || isPrimaryNatural1_5xStr) damageBonus *= 1.5f;
                else if (isOffHand) damageBonus *= 0.5f;
                
                totalBonus += Mathf.FloorToInt(damageBonus);
            }
        }

        // SANITIZED: Removed specific check for Sickened.
        return totalBonus;
    }

    private static bool ResolveRockCatchingAttempt(CreatureStats defender, CreatureStats attacker, Item_SO rock)
    {
        int dc = 15; // Small
        if (attacker.Template.Size == CreatureSize.Medium) dc = 20;
        else if (attacker.Template.Size >= CreatureSize.Large) dc = 25;

        int enhancementBonus = rock.Modifications.FirstOrDefault(m => m.BonusType == BonusType.Enhancement && m.StatToModify == StatToModify.AttackRoll)?.ModifierValue ?? 0;
        dc += enhancementBonus;
        
        int reflexSaveRoll = RollManager.Instance.MakeD20Roll(defender) + defender.GetReflexSave(attacker);

        GD.Print($"{defender.Name} attempts to catch the rock! Reflex Save: {reflexSaveRoll} vs DC {dc}.");

        if (reflexSaveRoll >= dc)
        {
            GD.PrintRich($"[color=cyan]Success! {defender.Name} catches the rock! The attack is negated.[/color]");
            defender.HasUsedRockCatchingThisRound = true;

            var inventory = defender.GetNodeOrNull<InventoryController>("InventoryController");
            if (inventory != null)
            {
                inventory.EquipItem(new ItemInstance(rock));
                GD.Print($"{defender.Name} equipped the caught rock in its free hand.");
            }
            return true;
        }
        else
        {
            GD.PrintRich($"[color=orange]Failure! {defender.Name} fails to catch the rock.[/color]");
            return false;
        }
    }
}
