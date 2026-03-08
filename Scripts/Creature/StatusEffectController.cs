using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: StatusEffectController.cs (GODOT VERSION - PART 1)
// PURPOSE: Manages buffs and debuffs on a creature.
// ATTACH TO: Creature Root Node (as child or main script component).
// =================================================================================================

public partial class StatusEffectController : Godot.Node
{
    /// ARCHITECTURAL NOTICE:
    /// Traditional level loss / negative levels are replaced with
    /// Corruption Stacks in this system.
    /// This game does not use character levels.
    /// All former level-drain mechanics must use Corruption Stacks instead.

    // The core list that holds all temporary status effects currently affecting this creature.
    public List<ActiveStatusEffect> ActiveEffects = new List<ActiveStatusEffect>();
    public List<CorruptionStackInstance> ActiveCorruptions = new List<CorruptionStackInstance>();

    [Export]
    [Tooltip("A flag for the Perception skill. Set to true if the creature is actively engaged in a non-Perception task.")]
    public bool IsDistracted = false;

    private CreatureStats myStats;

    public override void _Ready()
    {
        myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
		 CreatureStats.OnAnyCreatureDamaged += OnGlobalDamageTaken;
    }
	public override void _ExitTree()
    {
        // INSERTION START
        CreatureStats.OnAnyCreatureDamaged -= OnGlobalDamageTaken;
        // INSERTION END
    }

    /// <summary>
    /// Adds a new status effect to this creature, optionally specifying the source.
    /// </summary>
    public void AddEffect(StatusEffect_SO newEffect, CreatureStats source = null, Ability_SO sourceAbility = null, int saveDC = 0)
    {
		 if (myStats.MyEffects.HasCondition(Condition.FreedomOfMovement))
        {
            if (newEffect.ConditionApplied == Condition.Paralyzed ||
                newEffect.ConditionApplied == Condition.Entangled ||
                newEffect.ConditionApplied == Condition.Impeded || // Slow/Web
                newEffect.ConditionApplied == Condition.Staggered) // Slow
            {
                GD.Print($"{myStats.Name} ignores {newEffect.EffectName} due to Freedom of Movement.");
                return;
            }
        }
        // --- IMMUNITY CHECKS ---
        if (myStats.HasImmunity(ImmunityType.MindAffecting) && newEffect.IsMindControlEffect) { GD.Print($"{myStats.Name} is immune to the mind-affecting effect '{newEffect.EffectName}'."); return; }
        if (myStats.HasImmunity(ImmunityType.Poison) && newEffect.EffectName.Contains("Poison")) { GD.Print($"{myStats.Name} is immune to the poison effect '{newEffect.EffectName}'."); return; }
        if (myStats.HasImmunity(ImmunityType.Disease) && newEffect.EffectName.Contains("Disease")) { GD.Print($"{myStats.Name} is immune to the disease effect '{newEffect.EffectName}'."); return; }
        if (myStats.HasImmunity(ImmunityType.Paralysis) && newEffect.ConditionApplied == Condition.Helpless) { GD.Print($"{myStats.Name} is immune to the paralysis effect '{newEffect.EffectName}'."); return; }
        if (myStats.HasImmunity(ImmunityType.Sleep) && newEffect.EffectName.Contains("Sleep")) { GD.Print($"{myStats.Name} is immune to the sleep effect '{newEffect.EffectName}'."); return; }
        if (myStats.HasImmunity(ImmunityType.Stun) && newEffect.ConditionApplied == Condition.Stunned) { GD.Print($"{myStats.Name} is immune to the stun effect '{newEffect.EffectName}'."); return; }
 if (myStats.HasImmunity(ImmunityType.Polymorph) && newEffect.ConditionApplied == Condition.Polymorphed) { GD.Print($"{myStats.Name} is immune to the polymorph effect '{newEffect.EffectName}'."); return; }
 
        var activeEffectInstance = new ActiveStatusEffect(newEffect, source);
        activeEffectInstance.SaveDC = saveDC;

        // Build the absorption pool when the effect begins so combat resolution only has to subtract values.
        // Output expected: typed protection effects start with a finite budget and self-remove when spent.
        int absorptionPool = Mathf.Max(0, newEffect.AbsorptionPoolBase);
        if (newEffect.AbsorptionPoolScalesWithCasterLevel)
        {
            int sourceCasterLevel = source?.Template?.CasterLevel ?? 0;
            absorptionPool += Mathf.Max(0, sourceCasterLevel * newEffect.AbsorptionPointsPerCasterLevel);
        }

        if (newEffect.AbsorptionPoolMax > 0)
        {
            absorptionPool = Mathf.Min(absorptionPool, newEffect.AbsorptionPoolMax);
        }

        activeEffectInstance.RemainingAbsorptionPool = absorptionPool;
		// Calculate Temp HP
        int tempHP = newEffect.TemporaryHPBase;
        if (newEffect.TemporaryHPDiceCount > 0) tempHP += Dice.Roll(newEffect.TemporaryHPDiceCount, newEffect.TemporaryHPDiceSides);

        if (newEffect.TempHPScalesWithLevel && source != null)
        {
            int levelBonus = source.Template.CasterLevel;
            if (newEffect.TempHPMaxBonus > 0) levelBonus = Mathf.Min(levelBonus, newEffect.TempHPMaxBonus);
            tempHP += levelBonus;
        }

        if (tempHP > 0)
        {
            // Add to stats
            myStats.AddTemporaryHP(tempHP);
            // Store locally so we know how much this specific effect contributed (simplified logic: we assume non-stacking or simple pool)
            // activeEffectInstance.GrantedTempHP = tempHP; // Need to add this field to ActiveStatusEffect class if we want to remove exactly this amount later.
            // For now, simple addition.
        }
        ActiveEffects.Add(activeEffectInstance);

        if (newEffect.RemoveConditionsOnApply != null && newEffect.RemoveConditionsOnApply.Count > 0)
        {
            foreach (var conditionToRemove in newEffect.RemoveConditionsOnApply)
            {
                ActiveEffects.RemoveAll(e => e != activeEffectInstance && e.EffectData.ConditionApplied == conditionToRemove);
            }
        }
		if (newEffect.ConditionApplied == Condition.Panicked || newEffect.ConditionApplied == Condition.Stunned)
        {
            var inv = GetParent().GetNodeOrNull<InventoryController>("InventoryController");
            if (inv != null)
            {
                GD.PrintRich($"[color=red]{GetParent().Name} is PANICKED and drops everything![/color]");
                // Drop Main Hand, Off Hand, Shield
                var body = GetParent<Node3D>();
                inv.DropItemFromSlot(EquipmentSlot.MainHand, body.GlobalPosition);
                inv.DropItemFromSlot(EquipmentSlot.OffHand, body.GlobalPosition);
                inv.DropItemFromSlot(EquipmentSlot.Shield, body.GlobalPosition);
            }
        }

        if (newEffect.ConditionApplied == Condition.SoulBound)
        {
            CombatMemory.RecordSoulBound(myStats);
        }
        GD.Print($"{GetParent().Name} is now affected by {newEffect.EffectName} from {source?.Name ?? "an unknown source"}.");

        // If this effect is magical (i.e., it came from an ability), add its signature to the AuraController
        if (sourceAbility != null && source != null)
        {
            var auraController = GetNodeOrNull<AuraController>("AuraController");
            // In Godot, adding components dynamically is adding Child Nodes.
            if (auraController == null)
            {
                auraController = new AuraController();
                auraController.Name = "AuraController";
                GetParent().AddChild(auraController);
            }

            auraController.Auras.Add(new MagicAura {
                SourceAbility = sourceAbility,
                SourceName = newEffect.EffectName,
                School = sourceAbility.School,
                CasterLevel = source.Template.CasterLevel,
                SpellLevel = sourceAbility.SpellLevel
            });
        }

		if (newEffect.OffersDisadvantageCure)
        {
            var otc = GetParent().GetNodeOrNull<OneTimeEffectController>("OneTimeEffectController");
            // If missing, add it (Dynamic Component pattern)
            if (otc == null)
            {
                otc = new OneTimeEffectController();
                otc.Name = "OneTimeEffectController";
                GetParent().AddChild(otc);
            }
            otc.AddDisadvantageOffer(newEffect.EffectName);
        }

        // Special Rule: Protection from Evil grants an immediate re-save vs. mind control from evil sources.
        if (newEffect.SpecialDefenses.Contains(SpecialDefense.BlockContact_EvilSummoned))
        {
            for (int i = ActiveEffects.Count - 1; i >= 0; i--)
            {
                var existingEffect = ActiveEffects[i];
                if (existingEffect.EffectData.IsMindControlEffect && existingEffect.SourceCreature?.Template.Alignment.Contains("Evil") == true)
                {
                    GD.Print($"{GetParent().Name} gets a new save against {existingEffect.EffectData.EffectName} due to Protection from Evil.");
                    int existingSaveDC = 15;
                    int saveRoll = Dice.Roll(1, 20) + myStats.GetWillSave(existingEffect.SourceCreature) + 2;

                    if (saveRoll >= existingSaveDC)
                    {
                        GD.PrintRich($"[color=green]Save successful! {existingEffect.EffectData.EffectName} is suppressed.[/color]");
                        existingEffect.IsSuppressed = true;
                    }
                    else
                    {
                        GD.PrintRich($"[color=red]Save failed. The effect continues.[/color]");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Removes all instances of a status effect with a specific name.
    /// </summary>
    public void RemoveEffect(string effectName)
    {
        int removedCount = ActiveEffects.RemoveAll(e => e.EffectData.EffectName == effectName);
        if (removedCount > 0)
        {
            GD.Print($"{effectName} was removed from {GetParent().Name}");
        }
    }

    /// <summary>
    /// Removes all status effects that were applied by a specific persistent effect instance.
    /// </summary>
    public void RemoveEffectsFromSource(int persistentEffectID)
    {
        if (persistentEffectID == 0) return;
        int removedCount = ActiveEffects.RemoveAll(e => e.SourcePersistentEffectID == persistentEffectID);
        if (removedCount > 0)
        {
            GD.Print($"{removedCount} effect(s) from source ID {persistentEffectID} were removed from {GetParent().Name}.");
        }
    }

	    private IEnumerable<ActiveStatusEffect> GetUnsuppressedEffects()
    {
        return ActiveEffects.Where(e => !e.IsSuppressed);
    }

    private HashSet<Condition> GetSuppressedConditions()
    {
        var suppressed = new HashSet<Condition>();
        foreach (var effect in GetUnsuppressedEffects())
        {
            if (effect.EffectData.SuppressConditions == null) continue;
            foreach (var condition in effect.EffectData.SuppressConditions)
            {
                suppressed.Add(condition);
            }
        }
        return suppressed;
    }

    private HashSet<EffectTag> GetSuppressedTags()
    {
        var suppressed = new HashSet<EffectTag>();
        foreach (var effect in GetUnsuppressedEffects())
        {
            if (effect.EffectData.SuppressEffectTags == null) continue;
            foreach (var tag in effect.EffectData.SuppressEffectTags)
            {
                suppressed.Add(tag);
            }
        }
        return suppressed;
    }

    private HashSet<BonusType> GetSuppressedBonusTypes()
    {
        var suppressed = new HashSet<BonusType>();
        foreach (var effect in GetUnsuppressedEffects())
        {
            if (effect.EffectData.SuppressBonusTypes == null) continue;
            foreach (var bonusType in effect.EffectData.SuppressBonusTypes)
            {
                suppressed.Add(bonusType);
            }
        }
        return suppressed;
    }

    private static bool IsSuppressedByAura(ActiveStatusEffect effect, HashSet<Condition> suppressedConditions, HashSet<EffectTag> suppressedTags)
    {
        return suppressedConditions.Contains(effect.EffectData.ConditionApplied) || suppressedTags.Contains(effect.EffectData.Tag);
    }

    public bool HasEffect(string effectName)
    {
        return GetUnsuppressedEffects().Any(e => e.EffectData.EffectName == effectName);
    }

    /// <summary>
    /// Answers whether any active, unsuppressed status currently grants a temporary immunity.
    /// Output expected: true when at least one effect explicitly grants the requested immunity.
    /// </summary>
    public bool HasGrantedImmunity(ImmunityType immunity)
    {
        foreach (var effect in GetUnsuppressedEffects())
        {
            if (effect.EffectData.GrantedImmunities == null) continue;
            if (effect.EffectData.GrantedImmunities.Contains(immunity)) return true;
        }
        return false;
    }

    /// <summary>
    /// Indicates whether corruption resistance penalties should be ignored right now.
    /// Output expected: true when an active protective effect asks to pause these penalties.
    /// </summary>
    public bool ShouldSuppressCorruptionResistancePenalty()
    {
        foreach (var effect in GetUnsuppressedEffects())
        {
            if (effect.EffectData.SuppressCorruptionResistancePenalty) return true;
        }
        return false;
    }

    /// <summary>
    /// Communicates whether any active protection currently blocks environmental heat hazards.
    /// Output expected: true when a status effect explicitly grants climate heat protection.
    /// </summary>
    public bool HasEnvironmentalHeatProtection()
    {
        foreach (var effect in GetUnsuppressedEffects())
        {
            if (effect.EffectData.ProtectsFromEnvironmentalHeat) return true;
        }

        return false;
    }

    /// <summary>
    /// Communicates whether any active protection currently blocks environmental cold hazards.
    /// Output expected: true when a status effect explicitly grants climate cold protection.
    /// </summary>
    public bool HasEnvironmentalColdProtection()
    {
        foreach (var effect in GetUnsuppressedEffects())
        {
            if (effect.EffectData.ProtectsFromEnvironmentalCold) return true;
        }

        return false;
    }

    /// <summary>
    /// Indicates whether movement over frozen ground should skip slip risk checks this turn.
    /// Output expected: true when an active effect says snow and ice movement penalties are ignored.
    /// </summary>
    public bool IgnoresSnowAndIceMovementPenalty()
    {
        foreach (var effect in GetUnsuppressedEffects())
        {
            if (effect.EffectData.IgnoreSnowAndIceMovementPenalty) return true;
        }

        return false;
    }

    /// <summary>
    /// Indicates whether weather penalties on sighting and ranged attacks should be skipped.
    /// Output expected: true when a protection effect neutralizes precipitation combat penalties.
    /// </summary>
    public bool IgnoresPrecipitationCombatPenalties()
    {
        foreach (var effect in GetUnsuppressedEffects())
        {
            if (effect.EffectData.IgnorePrecipitationCombatPenalties) return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves how many wind categories should be softened while active effects remain in place.
    /// Output expected: the highest single reduction value among active effects.
    /// </summary>
    public int GetWindSeverityReductionSteps()
    {
        int bestReduction = 0;

        foreach (var effect in GetUnsuppressedEffects())
        {
            bestReduction = Mathf.Max(bestReduction, effect.EffectData.WindSeverityReductionSteps);
        }

        return bestReduction;
    }

    public bool HasCondition(Condition condition)
    {
        var suppressedConditions = GetSuppressedConditions();
        if (suppressedConditions.Contains(condition)) return false;

        if (condition == Condition.Distracted)
        {
            return IsDistracted || GetUnsuppressedEffects().Any(e => e.EffectData.ConditionApplied == condition);
        }

        var suppressedTags = GetSuppressedTags();
        return GetUnsuppressedEffects().Any(e =>
            e.EffectData.ConditionApplied == condition &&
            !suppressedTags.Contains(e.EffectData.Tag));
    }

    public bool HasConditionStr(string conditionName)
    {
        // Helper for string based checks if enum doesn't cover it or using custom strings
        return GetUnsuppressedEffects().Any(e => e.EffectData.EffectName == conditionName);
    }

    public bool HasSpecialDefense(SpecialDefense defense)
    {
        return GetUnsuppressedEffects().Any(e => e.EffectData.SpecialDefenses.Contains(defense));
    }

    public int GetSanctuaryDC()
    {
        var sanctuary = GetUnsuppressedEffects().FirstOrDefault(e => e.EffectData.ConditionApplied == Condition.Sanctuary);
        if (sanctuary != null)
        {
            return sanctuary.SaveDC;
        }
        return 0;
    }

    public int GetTotalModifier(StatToModify stat, CreatureStats source = null, string weaponName = "")
    {
        var allBonuses = new List<StatModification>();
        var allPassiveEffects = myStats.GetPassiveFeatEffects();
        var suppressedConditions = GetSuppressedConditions();
        var suppressedTags = GetSuppressedTags();
        var suppressedBonusTypes = GetSuppressedBonusTypes();

        foreach (var activeEffect in GetUnsuppressedEffects())
        {
            if (IsSuppressedByAura(activeEffect, suppressedConditions, suppressedTags)) continue;

            foreach (var mod in activeEffect.EffectData.Modifications)
            {
                if (mod.StatToModify != stat) continue;
                if (suppressedBonusTypes.Contains(mod.BonusType)) continue;
				// Undead immunity to physical stat penalties
                if ((mod.IsPenalty || mod.ModifierValue < 0) && myStats.HasImmunity(ImmunityType.PhysicalAbilityDamage))
                {
                    if (stat == StatToModify.Strength || stat == StatToModify.Dexterity || stat == StatToModify.Constitution) continue;
                }

                // 1. Check source filter
                if (mod.SourceFilter != null && (source == null || !mod.SourceFilter.IsTargetValid(myStats, source)))
                {
                    continue;
                }

                // 2. Check weapon filter
                if (mod.WeaponFilter != null && (string.IsNullOrEmpty(weaponName) || !mod.WeaponFilter.IsWeaponValid(weaponName)))
                {
                    continue;
                }

                allBonuses.Add(mod);
            }
        }

        foreach (var mod in allPassiveEffects.SelectMany(e => e.Modifications).Where(m => m.StatToModify == stat))
        {
            if (suppressedBonusTypes.Contains(mod.BonusType)) continue;
            allBonuses.Add(mod);
        }

        int total = 0;
        var bonusGroups = allBonuses.GroupBy(b => b.BonusType);

        foreach (var group in bonusGroups)
        {
            if (group.Key == BonusType.Untyped || group.Key == BonusType.Dodge)
            {
                total += group.Sum(m => m.ModifierValue);
            }
            else
            {
                int bestBonus = 0;
                int totalPenalty = 0;
                foreach (var mod in group)
                {
                    if (mod.ModifierValue > 0) bestBonus = Mathf.Max(bestBonus, mod.ModifierValue);
                    else totalPenalty += mod.ModifierValue;
                }
                total += bestBonus + totalPenalty;
            }
        }
        return total;
    }

    public int GetTotalModifier(StatToModify stat, CreatureStats source = null)
    {
        return GetTotalModifier(stat, source, "");
    }

    public int GetConditionalSaveBonus(SaveCondition condition)
    {
        var allBonuses = new List<ConditionalSaveBonus>();
        var suppressedConditions = GetSuppressedConditions();
        var suppressedTags = GetSuppressedTags();
        var suppressedBonusTypes = GetSuppressedBonusTypes();

        foreach (var effect in GetUnsuppressedEffects())
        {
            if (IsSuppressedByAura(effect, suppressedConditions, suppressedTags)) continue;

            foreach (var saveBonus in effect.EffectData.ConditionalSaves.Where(cs => cs.Condition == condition))
            {
                if (suppressedBonusTypes.Contains(saveBonus.BonusType)) continue;
                allBonuses.Add(saveBonus);
            }
        }

        int total = 0;
        var bonusGroups = allBonuses.GroupBy(b => b.BonusType);
        foreach (var group in bonusGroups)
        {
            if (group.Key == BonusType.Untyped || group.Key == BonusType.Dodge)
            {
                total += group.Sum(m => m.ModifierValue);
            }
            else
            {
                int bestBonus = 0;
                int totalPenalty = 0;
                 foreach (var mod in group)
                {
                    if (mod.ModifierValue > 0) bestBonus = Mathf.Max(bestBonus, mod.ModifierValue);
                    else totalPenalty += mod.ModifierValue;
                }
                total += bestBonus + totalPenalty;
            }
        }
        return total;
    }
	public void OnTurnStart_ResolveRecurringSaves()
    {
        for (int i = ActiveEffects.Count - 1; i >= 0; i--)
        {
            var activeEffect = ActiveEffects[i];

            // Handle new recurring save system if effect component exists
            // Since EffectComponents are Resources in Godot, we check list
            // Assuming Effect_RecurringSave is a type of AbilityEffectComponent
            var recurringSaveEffect = activeEffect.EffectData.EffectComponents.OfType<Effect_RecurringSave>().FirstOrDefault();

            if (recurringSaveEffect != null)
            {
                if (recurringSaveEffect.AttemptRecurringSave(myStats, activeEffect.SourceCreature))
                {
                    GD.PrintRich($"[color=green]Success! The effect of '{activeEffect.EffectData.EffectName}' ends.[/color]");

                    if (activeEffect.EffectData.EffectName == "Wrenching Spasms")
                    {
                        int durationInRounds = 10 * 60 * 24;
                        ApplyImmunityEffect(activeEffect.SourceCreature, "Immunity to Wrenching Spasms", durationInRounds);
                    }

                    ActiveEffects.RemoveAt(i);
                    continue;
                }
            }

            if (activeEffect.IsSuppressed || !activeEffect.EffectData.AllowsRecurringSave) continue;

            if (activeEffect.EffectData.RecurringSaveRequiresIntelligence && myStats.Template.Intelligence < 3)
            {
                continue;
            }

            GD.Print($"{GetParent().Name} gets a new save against {activeEffect.EffectData.EffectName} (DC {activeEffect.SaveDC}).");

            int saveRoll = Dice.Roll(1, 20);
            int saveBonus = 0;
            switch(activeEffect.EffectData.RecurringSaveType)
            {
                case SaveType.Fortitude: saveBonus = myStats.GetFortitudeSave(activeEffect.SourceCreature); break;
                case SaveType.Reflex:    saveBonus = myStats.GetReflexSave(activeEffect.SourceCreature); break;
                case SaveType.Will:      saveBonus = myStats.GetWillSave(activeEffect.SourceCreature); break;
            }

            if (saveRoll + saveBonus >= activeEffect.SaveDC)
            {
                GD.PrintRich($"[color=green]Save successful! (Rolled {saveRoll+saveBonus}). {activeEffect.EffectData.EffectName} ends.[/color]");
				if (activeEffect.EffectData.RecurringSaveConsumesTurn)
                {
                    var am = GetParent().GetNodeOrNull<ActionManager>("ActionManager");
                    // Consumes Standard + Move
                    am?.UseAction(ActionType.Standard);
                    am?.UseAction(ActionType.Move);
                    GD.Print("Breaking the effect consumed the turn.");
                }
                ActiveEffects.RemoveAt(i);
            }
            else
            {
                GD.PrintRich($"[color=red]Save failed. (Rolled {saveRoll+saveBonus}). The effect continues.[/color]");
            }
        }
    }

    public void OnTurnStart_EmitOngoingStatusSounds()
    {
        foreach (var effect in GetUnsuppressedEffects())
        {
            if (!effect.EffectData.EmitsSoundEachTurn) continue;

            float intensity = Mathf.Max(0f, effect.EffectData.OngoingSoundIntensity);
            if (intensity <= 0f) continue;

            SoundSystem.EmitSound(
                myStats,
                myStats.GlobalPosition,
                intensity,
                Mathf.Max(0.1f, effect.EffectData.OngoingSoundDurationSeconds),
                effect.EffectData.OngoingSoundType,
                effect.EffectData.OngoingSoundIsIllusion);
        }
    }

    /// <summary>
    /// Attempts to absorb incoming typed damage through active protective effects.
    /// Output expected: returns how much damage was absorbed and updates the passed-in remaining damage.
    /// </summary>
    public int AbsorbIncomingDamage(string damageType, ref int remainingDamage)
    {
        if (remainingDamage <= 0 || string.IsNullOrWhiteSpace(damageType)) return 0;

        int absorbedTotal = 0;

        for (int i = ActiveEffects.Count - 1; i >= 0; i--)
        {
            var activeEffect = ActiveEffects[i];
            if (activeEffect == null || activeEffect.IsSuppressed) continue;
            if (activeEffect.RemainingAbsorptionPool <= 0) continue;

            var absorbTypes = activeEffect.EffectData.AbsorbDamageTypes;
            if (absorbTypes == null || absorbTypes.Count == 0) continue;

            bool supportsType = absorbTypes.Any(t => t != null && t.Equals(damageType, System.StringComparison.OrdinalIgnoreCase));
            if (!supportsType) continue;

            int absorbedNow = Mathf.Min(remainingDamage, activeEffect.RemainingAbsorptionPool);
            if (absorbedNow <= 0) continue;

            activeEffect.RemainingAbsorptionPool -= absorbedNow;
            remainingDamage -= absorbedNow;
            absorbedTotal += absorbedNow;

            GD.PrintRich($"[color=cyan]{myStats.Name}'s {activeEffect.EffectData.EffectName} absorbs {absorbedNow} {damageType} damage. ({activeEffect.RemainingAbsorptionPool} protection remaining).[/color]");

            if (activeEffect.RemainingAbsorptionPool <= 0)
            {
                GD.PrintRich($"[color=orange]{myStats.Name}'s {activeEffect.EffectData.EffectName} is fully discharged.[/color]");
                ActiveEffects.RemoveAt(i);
            }

            if (remainingDamage <= 0) break;
        }

        return absorbedTotal;
    }

    /// <summary>
    /// Looks across active effects and returns the best matching resistance value for the incoming damage type.
    /// Output expected: the largest valid resistance amount from unsuppressed effects, or 0 when none apply.
    /// </summary>
    public int GetDamageResistanceFromEffects(string damageType)
    {
        if (string.IsNullOrWhiteSpace(damageType)) return 0;

        int highestResistance = 0;

        foreach (var activeEffect in ActiveEffects)
        {
            // We only consider effects that are currently active and actually provide resistance.
            if (activeEffect == null || activeEffect.IsSuppressed) continue;
            if (activeEffect.EffectData == null) continue;
            if (activeEffect.EffectData.DamageResistanceAmount <= 0) continue;

            var resistedTypes = activeEffect.EffectData.ResistDamageTypes;
            if (resistedTypes == null || resistedTypes.Count == 0) continue;

            bool supportsType = resistedTypes.Any(t => t != null && t.Equals(damageType, System.StringComparison.OrdinalIgnoreCase));
            if (!supportsType) continue;

            // Resistance effects in this ruleset overlap rather than stack,
            // so we keep only the strongest value that applies.
            highestResistance = Mathf.Max(highestResistance, activeEffect.EffectData.DamageResistanceAmount);
        }

        return highestResistance;
    }

    public StatModification ConsumeDischargeableBonus()
    {
        ActiveStatusEffect effectToDischarge = ActiveEffects.FirstOrDefault(e => !e.IsSuppressed && e.EffectData.IsDischargeable && e.RemainingCharges > 0);

        if (effectToDischarge != null)
        {
            effectToDischarge.RemainingCharges--;
            GD.PrintRich($"[color=green]{GetParent().Name} consumes a charge of {effectToDischarge.EffectData.EffectName}. ({effectToDischarge.RemainingCharges} charges left).[/color]");

            if (effectToDischarge.RemainingCharges <= 0)
            {
                GD.Print($"{effectToDischarge.EffectData.EffectName} is fully discharged and has been removed.");
                ActiveEffects.Remove(effectToDischarge);
            }

            return effectToDischarge.EffectData.Modifications.FirstOrDefault();
        }

        return null;
    }

    public void OnTurnStart_HandleEnvironmentalEffects()
    {
        var body = GetParent<Node3D>();
        GridNode currentNode = GridManager.Instance.NodeFromWorldPoint(body.GlobalPosition);

        // -- On Fire Check --
        var onFireEffect = ActiveEffects.FirstOrDefault(e => e.EffectData.EffectName == "On Fire" || e.EffectData.EffectName == "Mythic Fire");
        if (onFireEffect != null)
        {
            if (currentNode.terrainType == TerrainType.Water)
            {
                GD.PrintRich($"[color=cyan]{myStats.Name} enters water and the fire is extinguished![/color]");
                RemoveEffect("On Fire");
                RemoveEffect("Mythic Fire"); // Check both names
                return;
            }

                        // UPDATE START: Use stored DC if available (Mythic rule), else default 15
            int extinguishDC = onFireEffect.SaveDC > 0 ? onFireEffect.SaveDC : 15;

            int reflexSave = Dice.Roll(1, 20) + myStats.GetReflexSave(onFireEffect.SourceCreature);
            if (myStats.MyEffects.HasCondition(Condition.Prone))
            {
                reflexSave += 4;
            }

            if (reflexSave >= extinguishDC)
            // UPDATE END
            {
                GD.PrintRich($"[color=green]{myStats.Name} succeeds on Reflex save and extinguishes the flames![/color]");
                RemoveEffect("On Fire");
				RemoveEffect("Mythic Fire");
            }
            else
            {
                GD.PrintRich($"[color=red]{myStats.Name} is still on fire and takes 1d6 damage![/color]");
                myStats.TakeDamage(Dice.Roll(1, 6), "Fire");
            }
        }
        else
        {
            if (FireManager.Instance != null && FireManager.Instance.IsPositionOnFire(body.GlobalPosition))
            {
                CatchOnFire(null);
            }
        }

        // -- Smoke Inhalation Check --
        if (FireManager.Instance != null && FireManager.Instance.IsPositionSmoky(body.GlobalPosition))
        {
            HandleSmokeInhalation();
        }
    }

    public void CatchOnFire(CreatureStats source)
    {
        CatchOnFire(source, 15);
    }

    public void CatchOnFire(CreatureStats source, int dc)
    {
        if (myStats.Template.Immunities != null && myStats.Template.Immunities.Any(i => i.Equals("Fire", System.StringComparison.OrdinalIgnoreCase)))
        {
            GD.Print($"{myStats.Name} is immune to fire and cannot catch on fire.");
            return;
        }

        int reflexSave = Dice.Roll(1, 20) + myStats.GetReflexSave(source);
        if (reflexSave >= dc)
        {
            GD.Print($"{myStats.Name} makes a Reflex save (vs DC {dc}) to avoid catching on fire.");
            return;
        }

        GD.PrintRich($"[color=red]{myStats.Name} catches on fire![/color]");
        myStats.TakeDamage(Dice.Roll(1, 6), "Fire");

        if (!HasEffect("On Fire"))
        {
            var onFireStatus = new StatusEffect_SO();
            onFireStatus.EffectName = "On Fire";
            onFireStatus.DurationInRounds = 0;
            AddEffect(onFireStatus, source);
        }
    }

private void HandleSmokeInhalation()
    {
        if (myStats.HasSpecialRule("No Breath")) return;

        var chokingEffect = ActiveEffects.FirstOrDefault(e => e.EffectData.EffectName == "Choking");
        int dc = 15;
        if (chokingEffect != null)
        {
            dc += chokingEffect.RemainingCharges;
        }

        int fortSave = Dice.Roll(1, 20) + myStats.GetFortitudeSave(null);
        if (fortSave < dc)
        {
            GD.PrintRich($"[color=orange]{myStats.Name} fails Fortitude save vs smoke (DC {dc}) and is choking![/color]");
            if (chokingEffect == null)
            {
                var newChokingEffect = new StatusEffect_SO();
                newChokingEffect.EffectName = "Choking";
                newChokingEffect.DurationInRounds = 0;
                newChokingEffect.Charges = 1;
                AddEffect(newChokingEffect, null);
            }
            else
            {
                chokingEffect.RemainingCharges++;
                if (chokingEffect.RemainingCharges >= 2)
                {
                    myStats.TakeNonlethalDamage(Dice.Roll(1, 6));
                }
            }
        }
        else
        {
            RemoveEffect("Choking");
        }
    }

    private void ApplyImmunityEffect(CreatureStats source, string effectName, int durationInRounds)
    {
        var immunityEffect = new StatusEffect_SO();
        immunityEffect.EffectName = $"{effectName} ({source.Name})";
        immunityEffect.DurationInRounds = durationInRounds;
        AddEffect(immunityEffect, source);
    }

    public void TickDownEffects()
    {
        if (myStats.Template.FastHealing > 0 && myStats.CurrentHP > 0 && myStats.CurrentHP < myStats.Template.MaxHP)
        {
            bool conditionMet = false;
            string condition = myStats.Template.FastHealingCondition.ToLower();

            if (string.IsNullOrEmpty(condition) || condition == "0")
            {
                conditionMet = true;
            }
            else
            {
                var body = GetParent<Node3D>();
                GridNode currentNode = GridManager.Instance.NodeFromWorldPoint(body.GlobalPosition);
                if (condition.Contains("fire") && FireManager.Instance != null && FireManager.Instance.IsPositionOnFire(body.GlobalPosition))
                {
                    conditionMet = true;
                }
                else if (condition.Contains("water") && currentNode.terrainType == TerrainType.Water)
                {
                    conditionMet = true;
                }
                else if ((condition.Contains("snow") || condition.Contains("ice")) && currentNode.terrainType == TerrainType.Ice)
                {
                    conditionMet = true;
                }
            }

            if (conditionMet)
            {
                myStats.HealDamage(myStats.Template.FastHealing);
            }
        }

        for (int i = ActiveEffects.Count - 1; i >= 0; i--)
        {
            var effect = ActiveEffects[i];
			// INSERTION START: Generic Remove-When-Healed Logic (Fate Drain)
            if (effect.EffectData.RemoveWhenHealed)
            {
                int currentDmg = 0;
                switch(effect.EffectData.StatToWatchForHealing)
                {
                    case AbilityScore.Strength: currentDmg = myStats.StrDamage; break;
                    case AbilityScore.Dexterity: currentDmg = myStats.DexDamage; break;
                    case AbilityScore.Constitution: currentDmg = myStats.ConDamage; break;
                    case AbilityScore.Intelligence: currentDmg = myStats.IntDamage; break;
                    case AbilityScore.Wisdom: currentDmg = myStats.WisDamage; break;
                    case AbilityScore.Charisma: currentDmg = myStats.ChaDamage; break;
                }

                if (currentDmg == 0)
                {
                     GD.Print($"{effect.EffectData.EffectName} removed (Stat healed).");
                     ActiveEffects.RemoveAt(i);
                     continue;
                }
            }

            if (!effect.IsSuppressed && effect.EffectData.ConditionApplied == Condition.Bleed)
            {
                if (effect.EffectData.DamagePerRound > 0)
                {
                    GD.Print($"{myStats.Name} takes {effect.EffectData.DamagePerRound} damage from Bleed.");
                    myStats.TakeDamage(effect.EffectData.DamagePerRound, "Bleed");
                }
            }

            if (effect.EffectData.DurationInRounds > 0)
            {
                int turnSeconds = TurnManager.Instance?.CurrentTurnLengthSeconds ?? TravelScaleDefinitions.CombatTurnSeconds;
                effect.RemainingDurationSeconds -= turnSeconds;
                effect.RemainingDuration = Mathf.CeilToInt(Mathf.Max(0f, effect.RemainingDurationSeconds) / TravelScaleDefinitions.CombatTurnSeconds);
            }

            if (!effect.IsSuppressed)
            {
                if(effect.EffectData.DamagePerRound > 0)
                {
                    myStats.TakeDamage(effect.EffectData.DamagePerRound, effect.EffectData.DamageType);
                }
            }

            if (effect.EffectData.DurationInRounds > 0 && effect.RemainingDurationSeconds <= 0f)
            {
                GD.Print($"{effect.EffectData.EffectName} has worn off of {GetParent().Name}.");
                ActiveEffects.RemoveAt(i);
            }
        }

        TickDownCorruptionStacks();
    }

    /// <summary>
    /// Adds corruption stacks using source-authored rules.
    ///
    /// Expected output:
    /// - Each origin keeps isolated stacks so overlapping effects never overwrite each other.
    /// - HP is clamped when max-HP corruption rises to avoid invalid overheal states.
    /// </summary>
    public int ApplyCorruption(CorruptionEffectDefinition definition, CreatureStats source, int requestedStacks)
    {
        if (definition == null || requestedStacks <= 0)
        {
            return 0;
        }

        long sourceId = source?.GetInstanceId() ?? 0;
        CorruptionStackInstance existing = ActiveCorruptions.FirstOrDefault(c => c.SourceID == sourceId && c.CorruptionId == definition.CorruptionId);
        if (existing == null)
        {
            existing = new CorruptionStackInstance
            {
                SourceID = sourceId,
                CorruptionId = definition.CorruptionId,
                DurationType = definition.DurationRule,
                RemainingDuration = definition.DurationRounds,
                RemainingDurationSeconds = definition.DurationRounds * TravelScaleDefinitions.CombatTurnSeconds,
                IsHealable = definition.IsHealable,
                HealConditions = definition.HealRules?.ToList() ?? new List<string>(),
                Modifiers = definition.Modifiers,
                StackDecayRule = definition.StackDecayRule,
                StackCount = 0
            };

            ActiveCorruptions.Add(existing);
        }

        int maxStacks = Mathf.Max(0, definition.MaxStacks);
        int before = existing.StackCount;
        existing.StackCount = maxStacks > 0
            ? Mathf.Clamp(existing.StackCount + requestedStacks, 0, maxStacks)
            : existing.StackCount + requestedStacks;

        int applied = Mathf.Max(0, existing.StackCount - before);
        if (applied > 0)
        {
            myStats.ClampCurrentHpToEffectiveMax();
        }

        return applied;
    }

    public bool AttemptRemoveCorruption(long sourceID, int amount)
    {
        if (amount <= 0) return false;

        CorruptionStackInstance entry = ActiveCorruptions.FirstOrDefault(c => c.SourceID == sourceID);
        if (entry == null || !entry.IsHealable) return false;

        entry.StackCount = Mathf.Max(0, entry.StackCount - amount);
        if (entry.StackCount == 0)
        {
            ActiveCorruptions.Remove(entry);
        }

        myStats.ClampCurrentHpToEffectiveMax();
        return true;
    }

    public void ClearEncounterCorruption()
    {
        ActiveCorruptions.RemoveAll(c => c.DurationType == DurationType.Encounter);
        myStats.ClampCurrentHpToEffectiveMax();
    }

    private void TickDownCorruptionStacks()
    {
        for (int i = ActiveCorruptions.Count - 1; i >= 0; i--)
        {
            CorruptionStackInstance corruption = ActiveCorruptions[i];
            if (corruption.DurationType == DurationType.Rounds && corruption.RemainingDurationSeconds > 0f)
            {
                int turnSeconds = TurnManager.Instance?.CurrentTurnLengthSeconds ?? TravelScaleDefinitions.CombatTurnSeconds;
                corruption.RemainingDurationSeconds -= turnSeconds;
                corruption.RemainingDuration = Mathf.CeilToInt(Mathf.Max(0f, corruption.RemainingDurationSeconds) / TravelScaleDefinitions.CombatTurnSeconds);
                if (corruption.RemainingDurationSeconds <= 0f)
                {
                    ActiveCorruptions.RemoveAt(i);
                }
            }
        }

        myStats.ClampCurrentHpToEffectiveMax();
    }

    public CorruptionMetrics GetCorruptionMetrics()
    {
        CorruptionMetrics metrics = new CorruptionMetrics();
        foreach (CorruptionStackInstance corruption in ActiveCorruptions)
        {
            metrics.TotalCorruptionStacks += corruption.StackCount;
            metrics.CorruptionSources.Add(corruption.SourceID);
            metrics.CorruptionSeverityScore +=
                corruption.GetMaxHpPercentPenalty() * 0.35f +
                corruption.GetDamagePercentPenalty() * 0.25f +
                corruption.GetMoraleStabilityPenalty() * 0.2f +
                (corruption.GetAwarenessPenalty() * 0.05f) +
                (corruption.GetResistancePenalty() * 0.05f);
        }

        return metrics;
    }

    public float GetCorruptionMaxHpPenalty() => Mathf.Clamp(ActiveCorruptions.Sum(c => c.GetMaxHpPercentPenalty()), 0f, 0.95f);
    public float GetCorruptionDamagePenalty() => Mathf.Clamp(ActiveCorruptions.Sum(c => c.GetDamagePercentPenalty()), 0f, 0.95f);
    public float GetCorruptionMoralePenalty() => Mathf.Clamp(ActiveCorruptions.Sum(c => c.GetMoraleStabilityPenalty()), 0f, 0.95f);
    public int GetCorruptionAwarenessPenalty() => Mathf.Max(0, ActiveCorruptions.Sum(c => c.GetAwarenessPenalty()));
    /// <summary>
    /// Returns the current corruption resistance penalty from active corruption stacks.
    /// Output expected: a non-negative number, or zero when a protective status suppresses this burden.
    /// </summary>
    public int GetCorruptionResistancePenalty()
    {
        if (ShouldSuppressCorruptionResistancePenalty())
        {
            return 0;
        }

        return Mathf.Max(0, ActiveCorruptions.Sum(c => c.GetResistancePenalty()));
    }

	public void AdvanceTime(float deltaSeconds)
    {
        for (int i = ActiveEffects.Count - 1; i >= 0; i--)
        {
            var effect = ActiveEffects[i];
            var data = effect.EffectData;

            if (data.IsAffliction)
            {
// 1. Handle Death / Reanimation
                if (myStats.IsDead && (data.SpawnTemplateOnDeath != null || data.ApplyTemplateOnDeath != null))
                {
                    if (effect.ReanimationTimerSeconds > 0)
                    {
                        effect.ReanimationTimerSeconds -= deltaSeconds;
                        if (effect.ReanimationTimerSeconds <= 0)
                        {
                            if (data.SpawnTemplateOnDeath != null)
                            {
                                GD.PrintRich($"[color=purple]Macabre Reanimation! {myStats.Name}'s corpse rises as a {data.SpawnTemplateOnDeath.CreatureName}.[/color]");
                                var newCreature = data.SpawnTemplateOnDeath.CharacterPrefab.Instantiate<Node3D>();
                                myStats.GetTree().CurrentScene.AddChild(newCreature);
                                newCreature.GlobalPosition = myStats.GlobalPosition;

                                var newStats = newCreature as CreatureStats ?? newCreature.GetNodeOrNull<CreatureStats>("CreatureStats");
                                if (newStats != null) { newStats.AddToGroup("Enemy"); newStats.RemoveFromGroup("Player"); }

                                if (TurnManager.Instance != null && TurnManager.Instance.GetAllCombatants().Any()) TurnManager.Instance.ReviveCombatant(newStats);
                                myStats.QueueFree();
                            }
                            else if (data.ApplyTemplateOnDeath != null)
                            {
                                GD.PrintRich($"[color=purple]Macabre Reanimation! {myStats.Name}'s corpse rises as an Undead![/color]");
                                myStats.ApplyTemplateModifier(data.ApplyTemplateOnDeath);

                                myStats.HealDamage(9999);
                                myStats.SetPhysicsProcess(true);
                                myStats.SetProcess(true);
                                var col = myStats.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
                                if (col != null) col.Disabled = false;

                                myStats.AddToGroup("Enemy");
                                myStats.RemoveFromGroup("Player");

                                if (TurnManager.Instance != null && TurnManager.Instance.GetAllCombatants().Any()) TurnManager.Instance.ReviveCombatant(myStats);
                            }
                            ActiveEffects.RemoveAt(i);
                        }
                    }
                    continue; // Skip disease ticks if dead
                }
                if (myStats.IsDead) continue;

                // 2. Handle Disease Ticks
                effect.SecondsUntilNextTick -= deltaSeconds;
                if (effect.SecondsUntilNextTick <= 0)
                {
                    if (effect.IsInOnset)
                    {
                        GD.Print($"{myStats.Name}'s {data.EffectName} onset period ends. The disease takes hold.");
                        effect.IsInOnset = false;
                        effect.SecondsUntilNextTick = data.FrequencySeconds;
                        ApplyAfflictionDamage(effect);
                    }
                    else
                    {
                        int saveRoll = Dice.Roll(1, 20) + myStats.GetFortitudeSave(effect.SourceCreature);
                        int dc = effect.SaveDC > 0 ? effect.SaveDC : 15; // Fallback

                        if (saveRoll >= dc)
                        {
                            effect.SuccessfulSaves++;
                            GD.PrintRich($"[color=green]{myStats.Name} succeeded Fortitude save vs {data.EffectName} ({effect.SuccessfulSaves}/{data.ConsecutiveSavesToCure}).[/color]");
                            if (effect.SuccessfulSaves >= data.ConsecutiveSavesToCure)
                            {
                                GD.PrintRich($"[color=cyan]{myStats.Name} is cured of {data.EffectName}![/color]");
                                ActiveEffects.RemoveAt(i);
                                continue;
                            }
                            effect.SecondsUntilNextTick = data.FrequencySeconds;
                        }
                        else
                        {
                            effect.SuccessfulSaves = 0;
                            GD.PrintRich($"[color=red]{myStats.Name} failed Fortitude save vs {data.EffectName}![/color]");
                            ApplyAfflictionDamage(effect);
                            effect.SecondsUntilNextTick = data.FrequencySeconds;
                        }
                    }
                }
            }
        }
    }

    private void ApplyAfflictionDamage(ActiveStatusEffect effect)
    {
        var data = effect.EffectData;

        if (data.AfflictionAbilityDamage != null)
        {
            foreach (var dmg in data.AfflictionAbilityDamage)
            {
                int amount = Dice.Roll(dmg.DiceCount, dmg.DieSides);
                GD.Print($"{myStats.Name} takes {amount} {dmg.StatToDamage} damage from {data.EffectName}.");
                myStats.TakeAbilityDamage(dmg.StatToDamage, amount);
            }
        }

        if (data.AfflictionCondition != null)
        {
            if (myStats.MyEffects.HasCondition(data.AfflictionCondition.ConditionApplied) && data.AfflictionEscalatedCondition != null)
            {
                GD.PrintRich($"[color=purple]{myStats.Name}'s condition escalates to {data.AfflictionEscalatedCondition.EffectName}![/color]");
                var instance = (StatusEffect_SO)data.AfflictionEscalatedCondition.Duplicate();
                myStats.MyEffects.AddEffect(instance, effect.SourceCreature);
            }
            else
            {
                var instance = (StatusEffect_SO)data.AfflictionCondition.Duplicate();
                myStats.MyEffects.AddEffect(instance, effect.SourceCreature);
            }
        }
    }

    public List<StatModification> GetAllModifiersForStat(StatToModify stat)
    {
        var list = new List<StatModification>();

        // 1. Active Effects
        var suppressedConditions = GetSuppressedConditions();
        var suppressedTags = GetSuppressedTags();
        var suppressedBonusTypes = GetSuppressedBonusTypes();

        foreach (var effect in ActiveEffects)
        {
            if (effect.IsSuppressed) continue;
            if (IsSuppressedByAura(effect, suppressedConditions, suppressedTags)) continue;
            foreach (var mod in effect.EffectData.Modifications)
            {
                if (mod.StatToModify == stat && !suppressedBonusTypes.Contains(mod.BonusType)) list.Add(mod);
            }
        }

        // 2. Passive Feats
        if (myStats != null)
        {
            var feats = myStats.GetPassiveFeatEffects();
            foreach (var featEffect in feats)
            {
                foreach (var mod in featEffect.Modifications)
                {
                    if (mod.StatToModify == stat) list.Add(mod);
                }
            }
        }

        return list;
    }
 private void OnGlobalDamageTaken(CreatureStats victim, CreatureStats attacker)
    {
        // If I am the one damaged, standard logic (handled in TakeDamage) usually clears Fascinated.
        // This handles the "Ally Damaged" rule.
        if (victim == myStats) return;

        // Iterate backwards to remove safely
        for (int i = ActiveEffects.Count - 1; i >= 0; i--)
        {
            var effect = ActiveEffects[i];
            if (effect.EffectData.BreaksOnAllyDamage)
            {
                // Check if the victim is an ally of THIS creature
                // (Enthrall breaks if "any member of the audience" is attacked).
                // We assume the audience are allies of each other.
                if (victim.IsInGroup("Player") == myStats.IsInGroup("Player"))
                {
                    GD.PrintRich($"[color=orange]{myStats.Name}'s {effect.EffectData.EffectName} is broken because their ally {victim.Name} was attacked![/color]");
                    ActiveEffects.RemoveAt(i);
                }
            }
        }
    }
    // INSERTION END
}
