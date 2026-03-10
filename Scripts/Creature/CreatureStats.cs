using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: CreatureStats.cs (GODOT VERSION - SANITIZED)
// PURPOSE: The "character sheet" component.
// CHANGES: Removed hardcoded attribute penalties (Fatigued/Exhausted/Entangled).
//          Now relies purely on GetTotalModifier from the StatusEffectController.
//          Retained 'Paralyzed' override because it sets effective score to 0 (Mod -5), which is a state override, not a linear penalty.
// ATTACH TO: All creature scenes (Root Node, likely CharacterBody3D).
// =================================================================================================

public class GrappleState
{
	public CreatureStats Controller;
	public CreatureStats Target;
	public int RoundsMaintained;
	public bool IsHoldingWithBodyPartOnly; 
	public NaturalAttack InitiatingAttack;
	public int StartRound;

	public GrappleState(CreatureStats controller, CreatureStats target)
	{
		this.Controller = controller;
		this.Target = target;
		this.RoundsMaintained = 1;
		this.IsHoldingWithBodyPartOnly = false;
		this.StartRound = (TurnManager.Instance != null) ? TurnManager.Instance.GetCurrentRound() : 0;
	}
}

[GlobalClass]
public partial class CreatureStats : CharacterBody3D 
{
	[Export] public CreatureTemplate_SO Template;

	[ExportGroup("Live Data")]
	public int CurrentHP { get; private set; }
	public int CurrentNonlethalDamage { get; private set; }
	 public int TemporaryHP { get; private set; }

	[ExportGroup("Ability Score Damage")]
	public int StrDamage;
	public int DexDamage;
	public int ConDamage;
	public int IntDamage;
	public int WisDamage;
	public int ChaDamage;

	public bool IsFlatFooted { get; set; }
	public int CurrentMythicPower { get; private set; }
	public bool IsStable { get; private set; } = true;

	[ExportGroup("Grapple State")]
	public GrappleState CurrentGrappleState;

	[ExportGroup("Mounted Combat State")]
	public CreatureStats MyMount { get; private set; }
	public CreatureStats MyRider { get; private set; } 
	
	public VeilController MyVeil { get; set; } 
	public DominateController MyDomination { get; set; }
	
	public bool IsMounted => MyMount != null;
	public bool IsMount => MyRider != null;

	[ExportGroup("Creature State")]
	[Export]
	[Tooltip("Is this creature a summoned creature? Used for spells like Protection from Evil.")]
	public bool IsSummoned = false;

	[Export]
	[Tooltip("Is this creature a Projected Image? If so, its spells originate from here but are cast by its Caster.")]
	public bool IsProjectedImage = false;

	[Tooltip("If this is a projected image, this is a reference to the original caster.")]
	public CreatureStats Caster { get; set; }

	public bool HasCombatReflexes { get; private set; } = false;
	public int AoOsMadeThisRound { get; set; } = 0;
	public bool HasUsedRockCatchingThisRound { get; set; } = false;

	[Export]
	[Tooltip("If true, this creature was killed by a Death effect and cannot be raised by Raise Dead.")]
	public bool WasKilledByDeathEffect { get; set; } = false;
	public bool WasDisintegrated { get; set; } = false;

	public bool IsDead => CurrentHP <= -Template.Constitution;

	[ExportGroup("Adaptive Resistance State")]
	[Tooltip("The damage type this creature is currently resisting with its adaptive ability (e.g., 'Fire', 'Cold'). Blank if none.")]
	public string CurrentAdaptiveResistanceType { get; private set; } = "";

	private string lastEnergyDamageType = "";
	private int energyDamageHitCount = 0;

	[ExportGroup("Inaction State")]
	public int ConsecutiveInactionTurns { get; private set; } = 0;
	public int TotalInactionTurns { get; private set; } = 0;

	[ExportGroup("Runtime State")]
	public CreatureStats LastDamageSource;
	public int LastDamageAmount;
	public bool IsUsingVitalStrike = false; 
	public Ability_SO RuntimeTiedSpell { get; set; } // Added to host randomly assigned tied-spells

	// Component References
	public InventoryController MyInventory { get; private set; }
	public StatusEffectController MyEffects { get; private set; }
	public AbilityUsageController MyUsage { get; private set; }
	public MountedCombatController MyMountedCombat { get; private set; }
	public SwimController MySwimController { get; private set; }

	[ExportGroup("Runtime Data")]
	public List<Ability_SO> AvailableSkillActions { get; private set; } = new List<Ability_SO>();
	public List<Language> LearnedLanguages { get; private set; } = new List<Language>();
	
	[Export] public SkillActionDatabase SkillActionDB; 

	// Events
	public event Action<CreatureStats, Ability_SO> OnTargetedBySpell;
	public event Action<int, int> OnHealthChanged;
	public event Action<int, string, CreatureStats, Item_SO, NaturalAttack> OnTakeDamageDetailed;
	public static event Action<CreatureStats> OnCreatureDied; 
/// <summary>Fired whenever ANY creature takes damage. Used for Enthrall breaking.</summary>
	public static event Action<CreatureStats, CreatureStats> OnAnyCreatureDamaged; 
	public event Action<CreatureStats> OnForcedMovement;
	 /// <summary>Fired when ANY creature is forcefully moved. Args: Victim, Pusher.</summary>
	public static event Action<CreatureStats, CreatureStats> OnAnyCreatureForcedMoved;

	public void TriggerForcedMovement(CreatureStats pusher)
	{
		OnForcedMovement?.Invoke(pusher); // Keep instance event if needed locally
		OnAnyCreatureForcedMoved?.Invoke(this, pusher); // Fire static global event
	}
	private List<StatusEffect_SO> passiveFeatEffects = new List<StatusEffect_SO>();
	private Dictionary<DamageReduction, int> damageAbsorbedByDR = new Dictionary<DamageReduction, int>();

	public override void _Ready()
	{
		MyInventory = GetNodeOrNull<InventoryController>("InventoryController");
		MyEffects = GetNodeOrNull<StatusEffectController>("StatusEffectController");
		MyUsage = GetNodeOrNull<AbilityUsageController>("AbilityUsageController");
		MyVeil = GetNodeOrNull<VeilController>("VeilController");
		MyDomination = GetNodeOrNull<DominateController>("DominateController");
		MyMountedCombat = GetNodeOrNull<MountedCombatController>("MountedCombatController");
		MySwimController = GetNodeOrNull<SwimController>("SwimController");
		
		// Dynamically attach Soul Engine mechanics
		if (HasSpecialRule("Soul Engine") && GetNodeOrNull<SoulEngineController>("SoulEngineController") == null)
		{
			var soulEngine = new SoulEngineController();
			soulEngine.Name = "SoulEngineController";
			AddChild(soulEngine);
		}

		if (Template != null) ApplyTemplate();
	}

	public void NotifyTargetedBySpell(CreatureStats caster, Ability_SO spell)
	{
		OnTargetedBySpell?.Invoke(caster, spell);
	}

	// --- SANITIZED ABILITY SCORE CALCULATORS ---
	// Rule: Modifiers (Fatigued, Exhausted, Entangled) are now pulled from MyEffects.GetTotalModifier.
	// Paralyzed Exception: "Effective Strength and Dexterity score of 0" means a modifier of -5. 
	// This is a state override, not a linear penalty, so the check remains.

	public int StrModifier => (MyEffects?.HasCondition(Condition.Paralyzed) == true) ? -5 : CalculateModifier(
		Template.Strength 
		- StrDamage 
		+ (MyEffects?.GetTotalModifier(StatToModify.Strength) ?? 0)
		+ (MyInventory?.GetTotalStatModifierFromEquipment(StatToModify.Strength) ?? 0)
	);

	public int DexModifier => (MyEffects?.HasCondition(Condition.Paralyzed) == true) ? -5 : CalculateModifier(
		Template.Dexterity
		- DexDamage
		+ (MyEffects?.GetTotalModifier(StatToModify.Dexterity) ?? 0)
		+ (MyInventory?.GetTotalStatModifierFromEquipment(StatToModify.Dexterity) ?? 0)
	);

	public int ConModifier => CalculateModifier(
		Template.Constitution
		- ConDamage
		+ (MyEffects?.GetTotalModifier(StatToModify.Constitution) ?? 0)
		+ (MyInventory?.GetTotalStatModifierFromEquipment(StatToModify.Constitution) ?? 0)
	);

	public int IntModifier => CalculateModifier(
		Template.Intelligence
		- IntDamage
		+ (MyEffects?.GetTotalModifier(StatToModify.Intelligence) ?? 0)
		+ (MyInventory?.GetTotalStatModifierFromEquipment(StatToModify.Intelligence) ?? 0)
	);

	public int WisModifier => CalculateModifier(
		Template.Wisdom
		- WisDamage
		+ (MyEffects?.GetTotalModifier(StatToModify.Wisdom) ?? 0)
		+ (MyInventory?.GetTotalStatModifierFromEquipment(StatToModify.Wisdom) ?? 0)
	);

	public int ChaModifier => CalculateModifier(
		Template.Charisma
		- ChaDamage
		+ (MyEffects?.GetTotalModifier(StatToModify.Charisma) ?? 0)
		+ (MyInventory?.GetTotalStatModifierFromEquipment(StatToModify.Charisma) ?? 0)
	);
	
	public int GetAbilityScore(StatToModify stat)
	{
		switch (stat)
		{
			case StatToModify.Strength: return Template.Strength - StrDamage; // Simplistic current score
			case StatToModify.Dexterity: return Template.Dexterity - DexDamage;
			case StatToModify.Constitution: return Template.Constitution - ConDamage;
			case StatToModify.Intelligence: return Template.Intelligence - IntDamage;
			case StatToModify.Wisdom: return Template.Wisdom - WisDamage;
			case StatToModify.Charisma: return Template.Charisma - ChaDamage;
			default: return 10;
		}
	}

	public bool IsTwoWeaponFighting 
	{
		get 
		{
			if (MyInventory == null) return false;
			var mainHand = MyInventory.GetEquippedItem(EquipmentSlot.MainHand);
			if (mainHand != null && mainHand.IsDoubleWeapon) return true; 
			var offHand = MyInventory.GetEquippedItem(EquipmentSlot.OffHand);
			return mainHand != null && offHand != null && mainHand.Handedness != WeaponHandedness.TwoHanded;
		}
	}

	public void ApplyTemplate()
	{
		Name = Template.CreatureName;
		CurrentHP = GetEffectiveMaxHP();
		CurrentMythicPower = Template.MythicPower; 
		IsFlatFooted = false;
		
		ProcessSkillActions();
		
		damageAbsorbedByDR.Clear();
		if (Template.DamageReductions != null)
		{
			foreach (var dr in Template.DamageReductions)
			{
				if (dr.MaxAbsorbed > 0) damageAbsorbedByDR[dr] = 0;
			}
		}
		
ProcessFeats();

		// Instantiate Randomized Tied Spells (Unhallow/Hallow)
		if (Template.KnownAbilities != null)
		{
			foreach (var ability in Template.KnownAbilities)
			{
				var unhallow = ability.EffectComponents.OfType<Effect_UnhallowAura>().FirstOrDefault();
				if (unhallow != null && unhallow.AllowedSpellsList != null && unhallow.AllowedSpellsList.AllowedSpells.Count > 0 && RuntimeTiedSpell == null)
				{
					var rng = new RandomNumberGenerator();
					rng.Seed = (ulong)GetInstanceId(); // Seeded by instance ID for consistency across phases
					RuntimeTiedSpell = unhallow.AllowedSpellsList.AllowedSpells[rng.RandiRange(0, unhallow.AllowedSpellsList.AllowedSpells.Count - 1)];
				}
			}
		}

if (Template.PassiveEffects != null)
		{
			foreach (var passiveEffect in Template.PassiveEffects)
			{
				MyEffects.AddEffect(passiveEffect, null);
			}
		}

		OnHealthChanged?.Invoke(CurrentHP, GetEffectiveMaxHP());
	}

	public void ApplyTemplateModifier(CreatureTemplateModifier_SO modifier)
	{
		// Shallow copy the template so we don't permanently modify the global asset
		Template = (CreatureTemplate_SO)Template.Duplicate();
		
		// Deep copy the arrays we intend to modify
		Template.MeleeAttacks = new Godot.Collections.Array<NaturalAttack>(Template.MeleeAttacks);
		Template.SpecialQualities = new Godot.Collections.Array<string>(Template.SpecialQualities);
		Template.SpecialAttacks = new Godot.Collections.Array<Ability_SO>(Template.SpecialAttacks);

		// Apply modifications
		if (modifier.ChangeTypeTo != CreatureType.None) Template.Type = modifier.ChangeTypeTo;
		Template.Speed_Land += modifier.BonusLandSpeed;

		foreach (var atk in modifier.AddMeleeAttacks) Template.MeleeAttacks.Add((NaturalAttack)atk.Duplicate());
		foreach (var sq in modifier.AddSpecialQualities) Template.SpecialQualities.Add(sq);
		foreach (var sa in modifier.AddSpecialAttacks) Template.SpecialAttacks.Add(sa);

		// Update identity
		Name = $"{Template.CreatureName} ({modifier.ResourceName ?? "Modified"})";
		Template.CreatureName = Name;
		
		// Re-process the new data
		ApplyTemplate();
	}

	public bool HasMythicPower() => CurrentMythicPower > 0;

	public void ConsumeMythicPower()
	{
		if (CurrentMythicPower > 0)
		{
			CurrentMythicPower--;
			GD.Print($"{Name} used 1 Mythic Power. ({CurrentMythicPower} remaining).");
		}
	}

	private int CalculateModifier(int score) => Mathf.FloorToInt((score - 10) / 2f);

	/// <summary>
	/// Returns max HP after all active corruption penalties are combined.
	/// Expected output: HP penalties remain proportional and never flatten to fixed damage.
	/// </summary>
	public int GetEffectiveMaxHP()
	{
		float penalty = MyEffects?.GetCorruptionMaxHpPenalty() ?? 0f;
		return Mathf.Max(1, Mathf.CeilToInt(Template.MaxHP * (1f - penalty)));
	}

	public float GetCorruptionDamageMultiplier()
	{
		float penalty = MyEffects?.GetCorruptionDamagePenalty() ?? 0f;
		return Mathf.Clamp(1f - penalty, 0.05f, 1f);
	}

	public CorruptionMetrics GetCorruptionMetrics() => MyEffects?.GetCorruptionMetrics() ?? new CorruptionMetrics();

	public int TotalCorruptionStacks => GetCorruptionMetrics().TotalCorruptionStacks;
	public float CorruptionSeverityScore => GetCorruptionMetrics().CorruptionSeverityScore;
	public List<long> CorruptionSources => GetCorruptionMetrics().CorruptionSources;

	public float GetCorruptionMoralePenalty() => MyEffects?.GetCorruptionMoralePenalty() ?? 0f;

	public void ClampCurrentHpToEffectiveMax()
	{
		int effectiveMaxHp = GetEffectiveMaxHP();
		if (CurrentHP > effectiveMaxHp)
		{
			CurrentHP = effectiveMaxHp;
			OnHealthChanged?.Invoke(CurrentHP, effectiveMaxHp);
		}
	}

 public void TakeDamage(int incomingDamage, string damageType, CreatureStats attacker = null, Item_SO weaponUsed = null, NaturalAttack naturalAttackUsed = null, Action<int> onDamageResolved = null, bool bypassResistances = false)
	{
		if (HasImmunity(ImmunityType.DeathEffects) && damageType == "Death")
		{
			onDamageResolved?.Invoke(0);
			return;
		}

		string logReason = "";

		if (Template.SubTypes != null && Template.SubTypes.Contains("Swarm"))
		{
			if (weaponUsed != null)
			{
				if (Template.Size <= CreatureSize.Diminutive) 
				{
					incomingDamage = 0;
					logReason += " (Immune to Weapon Damage)";
				}
				else if (Template.Size == CreatureSize.Tiny)
				{
					if (damageType == "Slashing" || damageType == "Piercing")
					{
						incomingDamage /= 2;
						logReason += " (Resistant to Weapon)";
					}
				}
			}
			
			if (weaponUsed == null && naturalAttackUsed == null)
			{
				 incomingDamage = Mathf.FloorToInt(incomingDamage * 1.5f);
				 logReason += " (Vulnerable to AoE)";
			}
		}

		if (MyEffects.HasCondition(Condition.Incorporeal))
		{
			bool isMagicalWeapon = (weaponUsed != null && weaponUsed.Modifications.Any(m => m.BonusType == BonusType.Enhancement && m.ModifierValue > 0));
			bool isForceEffect = (damageType == "Force");
			bool isSpellOrAbility = (weaponUsed == null && naturalAttackUsed == null);

			bool isAttackerIncorporeal = attacker != null && attacker.MyEffects != null && attacker.MyEffects.HasCondition(Condition.Incorporeal);

			if (!isMagicalWeapon && !isSpellOrAbility && !isForceEffect && !isAttackerIncorporeal)
			{
				GD.Print($"{Name} is Incorporeal and immune to the nonmagical attack.");
				onDamageResolved?.Invoke(0);
				return;
			}

			if (!isForceEffect && !isAttackerIncorporeal)
			{
				incomingDamage = Mathf.Max(1, Mathf.FloorToInt(incomingDamage / 2f));
				logReason += $" (halved by Incorporeal)";
			}
		}

		if (CurrentHP <= -Template.Constitution)
		{
			onDamageResolved?.Invoke(0);
			return;
		}

		int finalDamage = incomingDamage;

		if (MyEffects.HasCondition(Condition.Gaseous))
		{
			bool isMythicGaseous = MyEffects.ActiveEffects.Any(e => e.EffectData.ConditionApplied == Condition.Gaseous && e.EffectData.EffectName.Contains("Mythic"));
			int drAmount = 10;
			string bypass = isMythicGaseous ? "Epic and Magic" : "Magic";
			
			if (!CheckDrBypass(bypass, attacker, weaponUsed, naturalAttackUsed))
			{
				int reduced = Mathf.Min(finalDamage, drAmount);
				finalDamage -= reduced;
				logReason += $" (-{reduced} Gaseous DR)";
			}
		}

		if (Template.DamageReductions != null && Template.DamageReductions.Any())
		{
			DamageReduction bestDr = null;
			int highestDrAmount = 0;

			foreach (var dr in Template.DamageReductions)
			{
				if (damageAbsorbedByDR.ContainsKey(dr) && damageAbsorbedByDR[dr] >= dr.MaxAbsorbed) continue; 

				if (!CheckDrBypass(dr.Bypass, attacker, weaponUsed, naturalAttackUsed) && dr.Amount > highestDrAmount)
				{
					highestDrAmount = dr.Amount;
					bestDr = dr;
				}
			}

			if (bestDr != null)
			{
				int amountToReduce = bestDr.Amount;

				if (damageAbsorbedByDR.ContainsKey(bestDr))
				{
					int remainingCapacity = bestDr.MaxAbsorbed - damageAbsorbedByDR[bestDr];
					amountToReduce = Mathf.Min(amountToReduce, remainingCapacity);
				}
				
				int actualAmountReduced = Mathf.Min(finalDamage, amountToReduce);
				finalDamage -= actualAmountReduced;
				logReason += $" (-{actualAmountReduced} from DR)";

				if (damageAbsorbedByDR.ContainsKey(bestDr))
				{
					damageAbsorbedByDR[bestDr] += actualAmountReduced;
				}
			}

			if (Template.Traits != null && finalDamage > 0)
			{
				foreach(var trait in Template.Traits)
				{
					foreach(var conv in trait.Conversions)
					{
						if (conv.IncomingType.Equals(damageType, StringComparison.OrdinalIgnoreCase))
						{
							 if (conv.ConvertedType == "Nonlethal")
							 {
								 GD.Print($"{Name} converts {finalDamage} {damageType} to Nonlethal (Trait: {trait.TraitName}).");
								 TakeNonlethalDamage(finalDamage);
								 finalDamage = 0;
								 goto DamageCalculationEnd; 
							 }
							 else if (conv.ConvertedType == "Heal")
							 {
								 GD.Print($"{Name} absorbs {finalDamage} {damageType} as Healing (Trait: {trait.TraitName}).");
								 HealDamage(finalDamage); 
								 finalDamage = 0;
								 goto DamageCalculationEnd;
							 }
							 else
							 {
								 GD.Print($"{Name} converts {damageType} to {conv.ConvertedType}.");
								 damageType = conv.ConvertedType;
							 }
						}
					}
				}
			}
			DamageCalculationEnd:;
		}
		
		 if (!string.IsNullOrEmpty(damageType) && damageType != "Untyped" && !bypassResistances)
		{
			if (Template.Immunities != null && Template.Immunities.Contains(damageType, StringComparer.OrdinalIgnoreCase))
			{
				finalDamage = 0;
				logReason = $" (Immune to {damageType})";
			}
			else
			{
				string[] energyTypes = { "Fire", "Cold", "Electricity", "Acid", "Sonic" };

				if (Template.AdaptiveResistanceAmount > 0 && energyTypes.Contains(damageType))
				{
					if (damageType != lastEnergyDamageType)
					{
						lastEnergyDamageType = damageType;
						energyDamageHitCount = 1;
					}
					else 
					{
						energyDamageHitCount++;
					}

					if (energyDamageHitCount == 2 && CurrentAdaptiveResistanceType != damageType)
					{
						CurrentAdaptiveResistanceType = damageType;
						CombatMemory.RecordAdaptiveResistanceState(this, CurrentAdaptiveResistanceType); 
					}
				}
				
				if (MyEffects != null && finalDamage > 0)
				{
					int absorbedByEffects = MyEffects.AbsorbIncomingDamage(damageType, ref finalDamage);
					if (absorbedByEffects > 0)
					{
						logReason += $" (-{absorbedByEffects} absorbed by active protection)";
					}
				}

				if (MyEffects != null && finalDamage > 0)
				{
					int effectResistance = MyEffects.GetDamageResistanceFromEffects(damageType);
					if (effectResistance > 0)
					{
						int amountResisted = Mathf.Min(finalDamage, effectResistance);
						finalDamage -= amountResisted;
						logReason += $" (-{amountResisted} from active {damageType} resistance)";
					}
				}

				if (Template.Resistances != null)
				{
					var applicableResistance = Template.Resistances.FirstOrDefault(r => r.DamageTypes.Any(t => t.Equals(damageType, StringComparison.OrdinalIgnoreCase)));
					if (applicableResistance != null)
					{
						int amountResisted = Mathf.Min(finalDamage, applicableResistance.Amount);
						finalDamage -= amountResisted;
						logReason += $" (-{amountResisted} from Resistance to {damageType})";
					}
				}

				if (Template.AdaptiveResistanceAmount > 0 && damageType == CurrentAdaptiveResistanceType)
				{
					int amountResisted = Mathf.Min(finalDamage, Template.AdaptiveResistanceAmount);
					finalDamage -= amountResisted;
					logReason += $" (-{amountResisted} from Adaptive Resistance)";
				}

				if (Template.Weaknesses != null)
				{
					var vulnerability = Template.Weaknesses.FirstOrDefault(v => v.DamageTypes.Contains(damageType, StringComparer.OrdinalIgnoreCase));
					if (vulnerability != null)
					{
						int extraDamage = Mathf.FloorToInt(incomingDamage * 0.5f);
						finalDamage += extraDamage;
						logReason += $" (+{extraDamage} from Vulnerability)";
					}
				}
			}
		}

		var stateController = GetNodeOrNull<CombatStateController>("CombatStateController");
		if (MyEffects.HasCondition(Condition.Blinded) && attacker != null && stateController != null && finalDamage > 0)
		{
			float attackDistance = GlobalPosition.DistanceTo(attacker.GlobalPosition);
			var (attackerMinReach, attackerMaxReach) = attacker.GetEffectiveReach((Item_SO)null); 

			bool isMeleeAttack = (weaponUsed != null && weaponUsed.WeaponType == WeaponType.Melee) || naturalAttackUsed != null;
			
			if (isMeleeAttack)
			{
				if (attackDistance <= attackerMaxReach)
				{
					if (attackerMaxReach <= 5f) stateController.UpdateEnemyLocation(attacker, LocationStatus.Pinpointed);
					else stateController.UpdateEnemyLocation(attacker, LocationStatus.KnownSquare);
				}
			}
			else 
			{
				stateController.UpdateEnemyLocation(attacker, LocationStatus.KnownDirection);
			}
		}

		finalDamage = Mathf.Max(0, finalDamage);
		if (finalDamage > 0 && MyEffects.HasCondition(Condition.Fascinated))
		{
			MyEffects.RemoveEffect("Fascinated"); 
		}
		
		if (finalDamage > 0 && MyEffects != null)
		{
			for (int i = MyEffects.ActiveEffects.Count - 1; i >= 0; i--)
			{
				var activeEffect = MyEffects.ActiveEffects[i];
				if (activeEffect.IsSuppressed) continue;
				if (!activeEffect.EffectData.BreaksOnDamageTaken) continue;

				GD.PrintRich($"[color=orange]{Name}'s {activeEffect.EffectData.EffectName} ends when they take damage.[/color]");
				MyEffects.ActiveEffects.RemoveAt(i);
			}
		}

		if (TemporaryHP > 0 && finalDamage > 0)
		{
			int absorbed = Mathf.Min(finalDamage, TemporaryHP);
			TemporaryHP -= absorbed;
			finalDamage -= absorbed;
			GD.Print($"{Name}'s Temp HP absorbed {absorbed} damage. ({TemporaryHP} remaining).");
		}

		GD.Print($"{Name} takes {incomingDamage} {damageType} damage -> {finalDamage} final damage.{logReason}");
		OnTakeDamageDetailed?.Invoke(finalDamage, damageType, attacker, weaponUsed, naturalAttackUsed);
		
		if (finalDamage > 0)
		{
			OnAnyCreatureDamaged?.Invoke(this, attacker);
		}
		
		int hpBeforeDamage = CurrentHP;
		CurrentHP -= finalDamage;
		
		if (finalDamage > 0) 
		{
			IsStable = false;
			MyEffects.RemoveEffect("Stabilized"); 
		}

		if (IsMounted && finalDamage > 0)
		{
			int rideCheck = Dice.Roll(1, 20) + GetSkillBonus(SkillType.Ride);
			if (rideCheck < 5)
			{
				Dismount(); 
			}
		}
		
		onDamageResolved?.Invoke(finalDamage);
		OnHealthChanged?.Invoke(CurrentHP, GetEffectiveMaxHP());

		if (Template.Speed_Fly > 0 && finalDamage > 0)
		{
			GetNodeOrNull<CreatureMover>("CreatureMover")?.HandleDamageWhileFlying(Template.HasWings);
		}

		CheckNonlethalState();

		if (CurrentHP <= -Template.Constitution)
		{
			Die();
			return;
		}
		
		if (CurrentHP < 0 && hpBeforeDamage >= 0)
		{
			if (HasFeat("Diehard"))
			{
				IsStable = true;
				GD.PrintRich($"[color=cyan]{Name} uses Diehard! Stabilizes instantly.[/color]");
				
				var stagEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Staggered_Effect.tres");
				if(stagEffect != null) MyEffects.AddEffect((StatusEffect_SO)stagEffect.Duplicate(), this);
			}
			else
			{
				IsStable = false;
				MyEffects.AddEffect(GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Unconscious_Effect.tres"), this, null);
				GD.PrintRich($"[color=red]{Name} is Dying![/color]");
			}
		}
		if (CurrentHP == 0 && hpBeforeDamage > 0)
		{
			MyEffects.AddEffect(GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Staggered_Effect.tres"), this, null);
			GD.PrintRich($"[color=orange]{Name} is Disabled![/color]");
		}

		this.LastDamageSource = attacker;
		this.LastDamageAmount = finalDamage;
	}

	public void HealDamage(int amount)
	{
		int hpBeforeHeal = CurrentHP;
		CurrentHP = Mathf.Min(GetEffectiveMaxHP(), CurrentHP + amount);

		if (hpBeforeHeal < 0 && CurrentHP >= 0)
		{
			IsStable = true;
			MyEffects.RemoveEffect("Unconscious Effect");
			if (CurrentHP == 0)
			{
				MyEffects.AddEffect(GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Staggered_Effect.tres"), this, null);
			}
		}
		
		if (hpBeforeHeal == 0 && CurrentHP > 0)
		{
			MyEffects.RemoveEffect("Staggered Effect");
		}

		OnHealthChanged?.Invoke(CurrentHP, GetEffectiveMaxHP());
	}

	public void TakeNonlethalDamage(int amount)
	{
		if (HasImmunity(ImmunityType.NonlethalDamage)) return;
		if (CurrentHP <= -Template.Constitution) return;

		CurrentNonlethalDamage += amount;
		CheckNonlethalState();
	}

	public void ProcessInactionTurn()
	{
		ConsecutiveInactionTurns++;
		TotalInactionTurns++;
		int penaltyAmount = Mathf.CeilToInt(GetEffectiveMaxHP() * 0.33f);

		if (ConsecutiveInactionTurns == 10 && TotalInactionTurns == 10)
		{
			TakeDamage(penaltyAmount, "Inaction");
		}
		else if (ConsecutiveInactionTurns == 15 && TotalInactionTurns >= 15)
		{
			TakeDamage(penaltyAmount, "Inaction");
		}
		else if (ConsecutiveInactionTurns >= 20 && TotalInactionTurns >= 20)
		{
			Die();
		}
	}

	public void ResetConsecutiveInaction()
	{
		if (TotalInactionTurns >= 15) ConsecutiveInactionTurns = 15;
		else if (TotalInactionTurns >= 10) ConsecutiveInactionTurns = 10;
		else ConsecutiveInactionTurns = 0;
	}

	private void CheckNonlethalState()
	{
		if (CurrentNonlethalDamage > 0 && CurrentHP > 0)
		{
			if (CurrentNonlethalDamage >= CurrentHP)
			{
				if (MyEffects.HasCondition(Condition.Unconscious)) return;

				if (CurrentNonlethalDamage > CurrentHP)
				{
					MyEffects.AddEffect(GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Unconscious_Effect.tres"), this, null);
					MyEffects.RemoveEffect("Staggered from Nonlethal");
				}
				else if (!MyEffects.HasEffect("Staggered from Nonlethal"))
				{
					var staggeredEffect = new StatusEffect_SO();
					staggeredEffect.EffectName = "Staggered from Nonlethal";
					staggeredEffect.ConditionApplied = Condition.Staggered;
					staggeredEffect.DurationInRounds = 0;
					MyEffects.AddEffect(staggeredEffect, this, null);
				}
			}
			else
			{
				MyEffects.RemoveEffect("Staggered from Nonlethal");
			}
		}
	}

	public void OnTurnStart_DyingCheck()
	{
		if (CurrentHP >= 0) return;
		if (HasFeat("Diehard")) return;
		// Check for Stable condition from spells/effects
		if (MyEffects.HasCondition(Condition.Stable)) 
		{
			IsStable = true;
		}
		if (IsStable) return;
		
		int dc = 10;
		int penalty = Mathf.Abs(CurrentHP);
		int roll = Dice.Roll(1, 20);
		
		if (roll + ConModifier - penalty >= dc)
		{
			Stabilize();
		}
		else
		{
			TakeDamage(1, "Untyped"); 
		}
	}

	public void Stabilize()
	{
		IsStable = true;
	}

	private void Die()
	{
		GD.PrintRich($"[color=black]{Name} is Dead at {CurrentHP} HP.[/color]");
		CurrentHP = -Template.Constitution;
		OnHealthChanged?.Invoke(CurrentHP, GetEffectiveMaxHP());
		if (CurrentGrappleState != null) CombatManeuvers.BreakGrapple(this);
		if (IsMounted) Dismount();
		if (IsMount) MyRider.Dismount();
		OnCreatureDied?.Invoke(this);

		// Start Reanimation Timers for Afflictions
		if (MyEffects != null)
		{
			foreach(var effect in MyEffects.ActiveEffects)
			{
				if (effect.EffectData.IsAffliction && effect.EffectData.SpawnTemplateOnDeath != null)
				{
					// Convert hours from dice roll into seconds
					effect.ReanimationTimerSeconds = Dice.Roll(effect.EffectData.SpawnDelayHoursDice) * 3600f;
					GD.Print($"{Name} is dead, but a parasite remains. Will reanimate in {effect.ReanimationTimerSeconds / 3600f:F1} hours.");
				}
			}
		}
		
		
		// DISABLE VISUALS/COLLISION INSTEAD OF QUEUEFREE
		// We keep the node for Raise Dead targeting.
		SetPhysicsProcess(false);
		SetProcess(false);
		var collider = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		if (collider != null) collider.Disabled = true;
	}

	private void ProcessSkillActions()
	{
		AvailableSkillActions.Clear();
		if (SkillActionDB == null || Template.SkillRanks == null) return;

		foreach (var skillAction in SkillActionDB.AllSkillActions)
		{
			var matchingSkillRank = Template.SkillRanks.FirstOrDefault(s => s.Skill == skillAction.RequiredSkill);
			if (matchingSkillRank != null && matchingSkillRank.Ranks >= skillAction.MinSkillRanksRequired)
			{
				AvailableSkillActions.Add(skillAction);
			}
		}
	}

	private void ProcessFeats()
	{
		passiveFeatEffects.Clear();
		HasCombatReflexes = false;
		if (Template.Feats == null) return;
		
		foreach (var featInstance in Template.Feats)
		{
			var feat = featInstance.Feat;
			if (feat == null) continue;

			if (feat.FeatName == "Combat Reflexes") HasCombatReflexes = true;
			
			if (feat.Type == FeatType.Passive_StatBonus)
			{
				StatusEffect_SO featEffect = new StatusEffect_SO();
				featEffect.EffectName = feat.FeatName;
				if (!string.IsNullOrEmpty(featInstance.TargetName))
				{
					featEffect.EffectName += $" ({featInstance.TargetName})";
				}
				featEffect.DurationInRounds = 0;
				
				foreach(var modTemplate in feat.Modifications)
				{
					var newMod = new StatModification {
						StatToModify = modTemplate.StatToModify,
						ModifierValue = modTemplate.ModifierValue,
						BonusType = modTemplate.BonusType
					};
					
					if (modTemplate.StatToModify == StatToModify.AttackRoll && !string.IsNullOrEmpty(featInstance.TargetName))
					{
						var filter = new WeaponNameFilter_SO();
						filter.RequiredWeaponName = featInstance.TargetName;
						newMod.WeaponFilter = filter;
					}
					featEffect.Modifications.Add(newMod);
				}
				passiveFeatEffects.Add(featEffect);
			}
			if (feat.Type == FeatType.Activated_Action && feat.AssociatedAbility != null)
			{
				if (Template.KnownAbilities == null) Template.KnownAbilities = new Godot.Collections.Array<Ability_SO>();
				if (!Template.KnownAbilities.Contains(feat.AssociatedAbility))
					Template.KnownAbilities.Add(feat.AssociatedAbility);
			}
		}
	}

	public bool Mount(CreatureStats targetMount, bool isCheckOnly = false)
	{
		if (IsMounted || targetMount.IsMount || !targetMount.Template.CanBeMounted) return false;
		if (this.Template.Intelligence < 3) return false;
		if ((int)this.Template.Size >= (int)targetMount.Template.Size) return false;
		if (this.Template.VerticalReach < targetMount.Template.Space / 2) return false;
		
		if (isCheckOnly) return true;

		int rideDC = 5;
		int rideBonus = GetSkillBonus(SkillType.Ride);
		
		if (Dice.Roll(1, 20) + rideBonus < rideDC) return false;
		
		MyMount = targetMount;
		targetMount.MyRider = this;
		GetParent().RemoveChild(this);
		targetMount.AddChild(this);
		Position = new Vector3(0, targetMount.Template.Space * 0.5f, 0); 
		return true;
	}

	public void Dismount()
	{
		if (!IsMounted) return;
		MyMount.MyRider = null;
		MyMount = null;
		
		var mainScene = GetTree().CurrentScene;
		var oldGlobalPos = GlobalPosition;
		GetParent().RemoveChild(this);
		mainScene.AddChild(this);
		GlobalPosition = oldGlobalPos;

		HandleFallFromMount();
	}

	public void HandleFallFromMount()
	{
		if ((Template.SkillRanks?.Find(s => s.Skill == SkillType.Ride)?.Ranks ?? 0) > 0)
		{
			int rideCheck = Dice.Roll(1, 20) + GetSkillBonus(SkillType.Ride);
			if (rideCheck >= 15)
			{
				var proneEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Prone_Effect.tres");
				if(proneEffect != null) MyEffects.AddEffect((StatusEffect_SO)proneEffect.Duplicate(), this);
				return;
			}
		}
		
		TakeDamage(Dice.Roll(1, 6), "Falling");
		var proneEffect2 = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Prone_Effect.tres");
		if(proneEffect2 != null) MyEffects.AddEffect((StatusEffect_SO)proneEffect2.Duplicate(), this);
	}

	public float GetMaxReach()
	{
		if (MyEffects.HasConditionStr("FarReachingStance")) return 2000f;
		
		float maxReach = Template.Reach;
		if (Template.AttackReachOverrides != null)
		{
			foreach (var anOverride in Template.AttackReachOverrides)
			{
				if (anOverride.Reach > maxReach) maxReach = anOverride.Reach;
			}
		}
		if (MyInventory != null)
		{
			var weapon = MyInventory.GetEquippedItem(EquipmentSlot.MainHand);
			if (weapon != null && weapon.WeaponReach > maxReach) maxReach = weapon.WeaponReach;
		}
		return maxReach;
	}

	public (float min, float max) GetEffectiveReach(Item_SO weapon)
	{
		if (weapon == null) return GetEffectiveReach((NaturalAttack)null);

		float baseReach = Template.Reach;

		if (Template.AttackReachOverrides != null)
		{
			var anOverride = Template.AttackReachOverrides.FirstOrDefault(o => 
				o.AttackNames.Any(name => weapon.ItemName.ToLower().Contains(name)));
			if (anOverride != null) baseReach = anOverride.Reach;
		}

		if (weapon.WeaponReach > 5f)
		{
			baseReach = weapon.WeaponReach;
			if (Template.Size >= CreatureSize.Large) return (Template.Reach, baseReach * 2);
			return (weapon.CreatesDeadZone ? baseReach : 5f, baseReach);
		}

		return (baseReach > 0 ? 5f : 0f, baseReach);
	}

	public (float min, float max) GetEffectiveReach(NaturalAttack naturalAttack)
	{
		if (MyEffects.HasConditionStr("FarReachingStance"))
		{
			if (naturalAttack != null && naturalAttack.AttackName.ToLower().Contains("tentacle")) return (0f, 2000f);
		}
		float baseReach = Template.Reach;

		if (naturalAttack != null && Template.AttackReachOverrides != null)
		{
			var anOverride = Template.AttackReachOverrides.FirstOrDefault(o => 
				o.AttackNames.Any(name => naturalAttack.AttackName.ToLower().Contains(name)));
			if (anOverride != null) baseReach = anOverride.Reach;
		}
		
		return (baseReach > 0 ? 5f : 0f, baseReach);
	}

	public int GetMaxAoOsPerRound()
	{
		return HasCombatReflexes ? Mathf.Max(1, 1 + DexModifier) : 1;
	}

	public void ResetAoOsForNewRound()
	{
		AoOsMadeThisRound = 0;
		HasUsedRockCatchingThisRound = false;
	}

	public int GetConcentrationBonus()
	{
		int ranks = Template.SkillRanks?.Find(s => s.Skill == SkillType.Concentration)?.Ranks ?? 0;
		int abilityMod = 0;
		switch (Template.PrimaryCastingStat)
		{
			case AbilityScore.Charisma: abilityMod = ChaModifier; break;
			case AbilityScore.Wisdom: abilityMod = WisModifier; break;
			case AbilityScore.Intelligence: abilityMod = IntModifier; break;
			default: abilityMod = ConModifier; break;
		}
		
		int effectBonus = MyEffects?.GetTotalModifier(StatToModify.ConcentrationCheck) ?? 0;
		return ranks + abilityMod + effectBonus;
	}

	public int GetCMB()
	{
		 if (MyEffects != null && MyEffects.HasCondition(Condition.Incorporeal))
		{
			return Template.BaseAttackBonus + DexModifier + GetSizeModifier();
		}
		int abilityMod = (Template.Size <= CreatureSize.Tiny) ? DexModifier : StrModifier;
		return Template.BaseAttackBonus + abilityMod + GetSizeModifier();
	}

	public int GetCMB(ManeuverType maneuver, bool isMaintainCheck = false)
	{
		int baseCMB = GetCMB(); 
		int highestApplicableBonus = -1;
		bool specialBonusFound = false;

		if (Template.ConditionalManeuverBonuses != null)
		{
			foreach (var mod in Template.ConditionalManeuverBonuses)
			{
				if (!mod.Maneuvers.Contains(maneuver) && !mod.Maneuvers.Contains(ManeuverType.Any)) continue;

				bool conditionMet = false;
				switch (mod.Condition)
				{
					case ManeuverCondition.None: conditionMet = true; break;
					case ManeuverCondition.ToMaintainGrapple: if (isMaintainCheck) conditionMet = true; break;
					case ManeuverCondition.OnACharge: if (MyEffects != null && MyEffects.HasCondition(Condition.Charging)) conditionMet = true; break;
				}

				if (conditionMet)
				{
					specialBonusFound = true;
					if (mod.Bonus > highestApplicableBonus) highestApplicableBonus = mod.Bonus;
				}
			}
		}

		int result = specialBonusFound ? highestApplicableBonus : baseCMB;

		if (maneuver == ManeuverType.Grapple)
		{
			var adhesive = GetNodeOrNull<PassiveAdhesiveController>("PassiveAdhesiveController");
			if (adhesive != null && adhesive.IsAdhesiveActive)
			{
				result += adhesive.GrappleBonus;
			}
		}

		var optionsController = GetNodeOrNull<CombatOptionsController>("CombatOptionsController");
		if (optionsController != null && optionsController.IsOptionActive("Power Attack"))
		{
			int penalty = -1 * (1 + Mathf.FloorToInt((Template.BaseAttackBonus - 1) / 4f));
			result += penalty;
		}

		return result;
	}

	public int GetCMD()
	{
		int dexMod = IsFlatFooted ? 0 : DexModifier;
		int grappleMod = (CurrentGrappleState != null && CurrentGrappleState.Target == this) ? 5 : 0;
		return 10 + Template.BaseAttackBonus + StrModifier + dexMod + GetSizeModifier() + grappleMod;
	}

	public int GetCMD(ManeuverType maneuver)
	{
		if (Template.ConditionalCmdBonuses != null)
		{
			var specialCmd = Template.ConditionalCmdBonuses.FirstOrDefault(b => b.Maneuvers.Contains(maneuver));
			if (specialCmd != null) return specialCmd.CmdValue;
		}
		return GetCMD();
	}

	public int GetSizeModifier()
	{
		switch (Template.Size)
		{
			case CreatureSize.Colossal: return -8;
			case CreatureSize.Gargantuan: return -4;
			case CreatureSize.Huge: return -2;
			case CreatureSize.Large: return -1;
			case CreatureSize.Medium: return 0;
			case CreatureSize.Small: return 1;
			case CreatureSize.Tiny: return 2;
			case CreatureSize.Diminutive: return 4;
			case CreatureSize.Fine: return 8;
			default: return 0;
		}
	}

	public int GetDiplomacyBonus() => (Template.SkillRanks?.Find(s => s.Skill == SkillType.Diplomacy)?.Ranks ?? 0) + ChaModifier;
	public int GetHealBonus() => (Template.SkillRanks?.Find(s => s.Skill == SkillType.Heal)?.Ranks ?? 0) + WisModifier;

	public int GetPerceptionBonus()
	{
		int ranks = Template.SkillRanks?.Find(s => s.Skill == SkillType.Perception)?.Ranks ?? 0;
		int totalBonus = ranks + WisModifier;

		if (HasFeat("Alertness")) totalBonus += (ranks >= 10) ? 4 : 2;

		if (Template.SubTypes != null)
		{
			if (Template.SubTypes.Any(s => s.Equals("Elf", StringComparison.OrdinalIgnoreCase)) ||
				Template.SubTypes.Any(s => s.Equals("Half-Elf", StringComparison.OrdinalIgnoreCase)) ||
				Template.Type == CreatureType.Fey)
			{
				totalBonus += 2;
			}
		}

		if (Template.HasScent) totalBonus += 8;
		if (Template.HasTremorsense) totalBonus += 8;

		totalBonus -= MyEffects?.GetCorruptionAwarenessPenalty() ?? 0;

		// Weather penalties can be bypassed by data-driven protective effects (for example, mythic Endure Elements).
		if (WeatherManager.Instance != null && WeatherManager.Instance.CurrentWeather != null)
		{
			bool ignoreWeatherPenalty = MyEffects != null && MyEffects.IgnoresPrecipitationCombatPenalties();
			if (!ignoreWeatherPenalty)
			{
				totalBonus += WeatherManager.Instance.CurrentWeather.PerceptionPenalty;
			}
		}

		return totalBonus;
	}

	public int GetIntimidateBonus(CreatureStats target)
	{
		int ranks = Template.SkillRanks?.Find(s => s.Skill == SkillType.Intimidate)?.Ranks ?? 0;
		int totalBonus = ranks + ChaModifier;

		if ((int)Template.Size > (int)target.Template.Size) totalBonus += 4;
		else if ((int)Template.Size < (int)target.Template.Size) totalBonus -= 4;

		if (HasFeat("Persuasive")) totalBonus += (ranks >= 10) ? 4 : 2;
		if (Template.SubTypes != null && Template.SubTypes.Any(s => s.Equals("Half-Orc", StringComparison.OrdinalIgnoreCase))) totalBonus += 2;
		
		return totalBonus;
	}

	public int GetSkillBonus(SkillType skill)
	{
		if (skill == SkillType.Fly) return GetFlyBonus();

		int rank = Template.SkillRanks?.Find(s => s.Skill == skill)?.Ranks ?? 0;
		int abilityMod = 0;
		
		switch (skill)
		{
			case SkillType.Acrobatics:    abilityMod = DexModifier; break;
			case SkillType.EscapeArtist:  abilityMod = DexModifier; break;
			case SkillType.Stealth:       abilityMod = DexModifier; break;
			case SkillType.Ride:          abilityMod = DexModifier; break;
			case SkillType.SleightOfHand: abilityMod = DexModifier; break;
			case SkillType.Climb:         abilityMod = StrModifier; break;
			case SkillType.Swim:          abilityMod = StrModifier; break;
			case SkillType.KnowledgeArcana:
			case SkillType.KnowledgeDungeoneering:
			case SkillType.KnowledgeEngineering:
			case SkillType.KnowledgeGeography:
			case SkillType.KnowledgeHistory:
			case SkillType.KnowledgeLocal:
			case SkillType.KnowledgeNature:
			case SkillType.KnowledgeNobility:
			case SkillType.KnowledgePlanes:
			case SkillType.KnowledgeReligion:
			case SkillType.Linguistics: 
			case SkillType.Spellcraft:    abilityMod = IntModifier; break;
			case SkillType.Heal:        abilityMod = WisModifier; break;
			case SkillType.Perception:  abilityMod = WisModifier; break;
			case SkillType.SenseMotive: abilityMod = WisModifier; break;
			case SkillType.Bluff:       abilityMod = ChaModifier; break;
			case SkillType.Diplomacy:   abilityMod = ChaModifier; break;
			case SkillType.Intimidate:  abilityMod = ChaModifier; break;
		}

		int featBonus = 0;
		if (skill == SkillType.Acrobatics && HasFeat("Run")) featBonus += 4;

		if (Template.Feats != null)
		{
			foreach (var featInstance in Template.Feats)
			{
				var feat = featInstance.Feat;
				if (feat.FeatName == "Magical Aptitude" && skill == SkillType.Spellcraft) featBonus += (rank >= 10) ? 4 : 2;
				if (feat.FeatName == "Alertness" && (skill == SkillType.Perception || skill == SkillType.SenseMotive)) featBonus += (rank >= 10) ? 4 : 2;
				if (feat.FeatName.Equals("Skill Focus", StringComparison.OrdinalIgnoreCase))
				{
					if (Enum.TryParse<SkillType>(featInstance.TargetName, true, out SkillType focusedSkill) && focusedSkill == skill)
					{
						featBonus += (rank >= 10) ? 6 : 3;
					}
				}
				if (feat.FeatName == "Acrobatic" && skill == SkillType.Acrobatics) featBonus += (rank >= 10) ? 4 : 2;
				else if (feat.FeatName == "Stealthy" && (skill == SkillType.EscapeArtist || skill == SkillType.Stealth)) featBonus += (rank >= 10) ? 4 : 2;
				else if (feat.FeatName == "Athletic" && (skill == SkillType.Climb || skill == SkillType.Swim)) featBonus += (rank >= 10) ? 4 : 2;
				else if (feat.FeatName == "Self-Sufficient" && (skill == SkillType.Heal || skill == SkillType.Survival)) featBonus += (rank >= 10) ? 4 : 2;
			}
		}

		int sizeModifier = 0;
		if (skill == SkillType.Stealth)
		{
			switch (Template.Size)
			{
				case CreatureSize.Fine: sizeModifier = 16; break;
				case CreatureSize.Diminutive: sizeModifier = 12; break;
				case CreatureSize.Tiny: sizeModifier = 8; break;
				case CreatureSize.Small: sizeModifier = 4; break;
				case CreatureSize.Large: sizeModifier = -4; break;
				case CreatureSize.Huge: sizeModifier = -8; break;
				case CreatureSize.Gargantuan: sizeModifier = -12; break;
				case CreatureSize.Colossal: sizeModifier = -16; break;
			}
		}
		
		int totalSkillBonus = rank + abilityMod + featBonus + sizeModifier;

		if (skill == SkillType.Perception && WeatherManager.Instance != null && WeatherManager.Instance.CurrentWeather != null)
		{
			bool ignoreWeatherPenalty = MyEffects != null && MyEffects.IgnoresPrecipitationCombatPenalties();
			
			string wName = WeatherManager.Instance.CurrentWeather.WeatherName.ToLower();
			if ((wName.Contains("snow") || wName.Contains("blizzard")) && (Template.HasSnowsight || (MyEffects != null && MyEffects.HasCondition(Condition.Snowsight))))
			{
				ignoreWeatherPenalty = true;
			}

			if (!ignoreWeatherPenalty)
			{
				totalSkillBonus += WeatherManager.Instance.CurrentWeather.PerceptionPenalty;
			}
		}

		return totalSkillBonus;
	}

	public int GetStealthBonus(bool isMoving)
	{
		int totalBonus = GetSkillBonus(SkillType.Stealth);
		var armor = MyInventory?.GetEquippedItem(EquipmentSlot.Armor);
		if (armor != null) totalBonus += armor.Modifications.FirstOrDefault(m => m.StatToModify == StatToModify.ArmorCheckPenalty)?.ModifierValue ?? 0;
		var shield = MyInventory?.GetEquippedItem(EquipmentSlot.Shield);
		if (shield != null) totalBonus += shield.Modifications.FirstOrDefault(m => m.StatToModify == StatToModify.ArmorCheckPenalty)?.ModifierValue ?? 0;

		if (MyEffects.HasCondition(Condition.Invisible)) totalBonus += isMoving ? 20 : 40;
		return totalBonus;
	}

	private int GetFlyBonus()
	{
		if (Template.Speed_Fly <= 0) return -99;
		int ranks = Template.SkillRanks?.Find(s => s.Skill == SkillType.Fly)?.Ranks ?? 0;
		int totalBonus = ranks + DexModifier;

		var armor = MyInventory?.GetEquippedItem(EquipmentSlot.Armor);
		if (armor != null) totalBonus += armor.Modifications.FirstOrDefault(m => m.StatToModify == StatToModify.ArmorCheckPenalty)?.ModifierValue ?? 0;
		var shield = MyInventory?.GetEquippedItem(EquipmentSlot.Shield);
		if (shield != null) totalBonus += shield.Modifications.FirstOrDefault(m => m.StatToModify == StatToModify.ArmorCheckPenalty)?.ModifierValue ?? 0;
		
		switch (Template.FlyManeuverability)
		{
			case FlyManeuverability.Clumsy: totalBonus -= 8; break;
			case FlyManeuverability.Poor: totalBonus -= 4; break;
			case FlyManeuverability.Average: totalBonus += 0; break;
			case FlyManeuverability.Good: totalBonus += 4; break;
			case FlyManeuverability.Perfect: totalBonus += 8; break;
		}

		switch (Template.Size)
		{
			case CreatureSize.Colossal: totalBonus -= 8; break;
			case CreatureSize.Gargantuan: totalBonus -= 6; break;
			case CreatureSize.Huge: totalBonus -= 4; break;
			case CreatureSize.Large: totalBonus -= 2; break;
			case CreatureSize.Small: totalBonus += 2; break;
			case CreatureSize.Tiny: totalBonus += 4; break;
			case CreatureSize.Diminutive: totalBonus += 6; break;
			case CreatureSize.Fine: totalBonus += 8; break;
		}

		if (HasFeat("Acrobatic")) totalBonus += (ranks >= 10) ? 4 : 2;

		if (WeatherManager.Instance != null && WeatherManager.Instance.CurrentWeather != null)
		{
			totalBonus += WeatherManager.Instance.CurrentWeather.FlyPenalty;
		}

		return totalBonus;
	}

	public int GetUnarmedDamageDieSides()
	{
		switch (Template.Size)
		{
			case CreatureSize.Fine:       return 1; 
			case CreatureSize.Diminutive: return 2; 
			case CreatureSize.Tiny:       return 3; 
			case CreatureSize.Small:      return 4; 
			case CreatureSize.Medium:     return 6; 
			case CreatureSize.Large:      return 8; 
			case CreatureSize.Huge:       return 12; 
			case CreatureSize.Gargantuan:  return 12; 
			case CreatureSize.Colossal:   return 12; 
			default: return 6; 
		}
	}

	public List<StatusEffect_SO> GetPassiveFeatEffects() => passiveFeatEffects;
	
	public bool HasFeat(string featName, string targetName = "")
	{
		if (Template.Feats == null) return false;
		foreach (var featInstance in Template.Feats)
		{
			if (featInstance.Feat.FeatName.Equals(featName, StringComparison.OrdinalIgnoreCase))
			{
				if (string.IsNullOrEmpty(targetName)) return true;
				if (featInstance.TargetName.Equals(targetName, StringComparison.OrdinalIgnoreCase)) return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Checks passive creature data for a named special rule.
	/// Supports both string-based SpecialQualities and Ability_SO names so content can stay data-driven.
	/// </summary>
	public bool HasSpecialRule(string ruleName)
	{
		if (string.IsNullOrWhiteSpace(ruleName) || Template == null) return false;

		if (Template.SpecialQualities != null &&
			Template.SpecialQualities.Any(q => q.Equals(ruleName, StringComparison.OrdinalIgnoreCase)))
		{
			return true;
		}

		if (Template.KnownAbilities != null &&
			Template.KnownAbilities.Any(a => a != null && a.AbilityName.Equals(ruleName, StringComparison.OrdinalIgnoreCase)))
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// Checks whether the creature is currently immune to a specific effect category.
	/// Output expected: true when immunity comes from either permanent traits or active temporary protections.
	/// </summary>
	public bool HasImmunity(ImmunityType immunity)
	{
		if (immunity == ImmunityType.Poison && MyEffects != null && MyEffects.HasCondition(Condition.Gaseous))
		{
			return true;
		}

		if (MyEffects != null && MyEffects.HasGrantedImmunity(immunity))
		{
			return true;
		}

		if (Template.Traits == null) return false;
		foreach (var trait in Template.Traits)
		{
			if (trait.Immunities.Contains(immunity)) return true;
		}
		return false;
	}

	/// <summary>
	/// Resolves extra save bonuses that depend on what kind of hostile magic is being resisted.
	/// Output expected: total bonus from matching conditional save clauses on active effects.
	/// </summary>
	private int GetSourceConditionalSaveBonus(Ability_SO sourceAbility)
	{
		if (sourceAbility == null || MyEffects == null)
		{
			return 0;
		}

		int bonus = 0;

		// Death-effect routing: currently represented by necromancy school abilities.
		if (sourceAbility.School == MagicSchool.Necromancy)
		{
			bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstDeathEffects);
		}

		// Negative-energy routing: generic match against known draining or negative damage components.
		bool isNegativeEnergySource = sourceAbility.EffectComponents != null && sourceAbility.EffectComponents.Any(component =>
			component is EnergyDrainEffect ||
			(component is DamageEffect damageEffect &&
			 damageEffect.Damage != null &&
			 !string.IsNullOrWhiteSpace(damageEffect.Damage.DamageType) &&
			 damageEffect.Damage.DamageType.ToLowerInvariant().Contains("negative"))
		);

		if (isNegativeEnergySource)
		{
			bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstNegativeEnergy);
		}
				bool isChanneling = sourceAbility.AbilityName.Contains("Channel", StringComparison.OrdinalIgnoreCase) && sourceAbility.AbilityName.Contains("Energy", StringComparison.OrdinalIgnoreCase);
		if (isChanneling)
		{
			bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstChanneling);
		}

		return bonus;
	}
	private int GetVulnerabilitySavePenalty(Ability_SO sourceAbility)
	{
		if (sourceAbility == null || Template.Weaknesses == null) return 0;
		
		int penalty = 0;
		string abilityName = sourceAbility.AbilityName.ToLowerInvariant();
		string abilityDesc = sourceAbility.DescriptionForTooltip?.ToLowerInvariant() ?? "";
		
		foreach (var weakness in Template.Weaknesses)
		{
			foreach (var type in weakness.DamageTypes)
			{
				string w = type.ToLowerInvariant();
				if (w == "fire" || w == "cold" || w == "electricity" || w == "acid" || w == "sonic") continue;
				
				if (abilityName.Contains(w) || abilityDesc.Contains(w)) 
				{
					penalty -= 4;
					break; 
				}
			}
		}
		return penalty;
	}

	public int GetFortitudeSave(CreatureStats sourceOfEffect, Ability_SO sourceAbility = null)
	{
		int bonus = Template.FortitudeSave_Base + ConModifier + MyEffects.GetTotalModifier(StatToModify.FortitudeSave, sourceOfEffect);
		bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstPoison);
		bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstDisease);
		bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstNonmagicalDisease);
		
		 if (sourceOfEffect != null)
		{
			string align = sourceOfEffect.Template.Alignment;
			if (align.Contains("Evil") || align.Contains("LE") || align.Contains("NE") || align.Contains("CE")) 
				bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstEvil);
			if (align.Contains("Good") || align.Contains("LG") || align.Contains("NG") || align.Contains("CG")) 
				bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstGood);
			if (align.Contains("Law") || align.Contains("LG") || align.Contains("LN") || align.Contains("LE")) 
				bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstLaw);
			if (align.Contains("Chaos") || align.Contains("CG") || align.Contains("CN") || align.Contains("CE")) 
				bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstChaos);
		}

		bonus += GetSourceConditionalSaveBonus(sourceAbility);

		bonus -= MyEffects?.GetCorruptionResistancePenalty() ?? 0;
		bonus += GetVulnerabilitySavePenalty(sourceAbility);
		return bonus;
	}

	public int GetReflexSave(CreatureStats sourceOfEffect, Ability_SO sourceAbility = null)
	{
		int bonus = Template.ReflexSave_Base + DexModifier + MyEffects.GetTotalModifier(StatToModify.ReflexSave, sourceOfEffect);
		bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstTraps); 
		bonus += GetSourceConditionalSaveBonus(sourceAbility);
		if (sourceOfEffect != null)
		{
			string align = sourceOfEffect.Template.Alignment;
			if (align.Contains("Evil") || align.Contains("LE") || align.Contains("NE") || align.Contains("CE")) 
				bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstEvil);
			if (align.Contains("Good") || align.Contains("LG") || align.Contains("NG") || align.Contains("CG")) 
				bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstGood);
			if (align.Contains("Law") || align.Contains("LG") || align.Contains("LN") || align.Contains("LE")) 
				bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstLaw);
			if (align.Contains("Chaos") || align.Contains("CG") || align.Contains("CN") || align.Contains("CE")) 
				bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstChaos);
		}
		 bonus += GetVulnerabilitySavePenalty(sourceAbility);
		return bonus;
	}

	public int GetWillSave(CreatureStats sourceOfEffect, Ability_SO sourceAbility = null)
	{
		int bonus = Template.WillSave_Base + WisModifier + MyEffects.GetTotalModifier(StatToModify.WillSave, sourceOfEffect);
		
		bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstFear);
		bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstCompulsion);
		bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstCharm);
		bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstMindAffecting);
		bonus += GetSourceConditionalSaveBonus(sourceAbility);
		 if (sourceOfEffect != null)
		{
			string align = sourceOfEffect.Template.Alignment;
			if (align.Contains("Evil") || align.Contains("LE") || align.Contains("NE") || align.Contains("CE")) 
				bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstEvil);
			if (align.Contains("Good") || align.Contains("LG") || align.Contains("NG") || align.Contains("CG")) 
				bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstGood);
			if (align.Contains("Law") || align.Contains("LG") || align.Contains("LN") || align.Contains("LE")) 
				bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstLaw);
			if (align.Contains("Chaos") || align.Contains("CG") || align.Contains("CN") || align.Contains("CE")) 
				bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstChaos);
		}

		if (sourceAbility != null)
		{
			if (sourceAbility.School == MagicSchool.Enchantment) bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstEnchantments);
			if (sourceAbility.School == MagicSchool.Illusion) bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstIllusions);
			
			if (sourceAbility.Category == AbilityCategory.Spell && sourceAbility.SpecialAbilityType != SpecialAbilityType.Su)
			{
				 bonus += MyEffects.GetConditionalSaveBonus(SaveCondition.AgainstArcane);
			}
		}
		bonus += GetVulnerabilitySavePenalty(sourceAbility);
		return bonus;
	}

	public bool SharesLanguageWith(CreatureStats otherCreature)
	{
		if (otherCreature == null) return false;
		var myLanguages = new HashSet<Language>(Template.RacialLanguages);
		myLanguages.UnionWith(LearnedLanguages);

		var otherLanguages = new HashSet<Language>(otherCreature.Template.RacialLanguages);
		otherLanguages.UnionWith(otherCreature.LearnedLanguages);

		if (myLanguages.Count == 0 || otherLanguages.Count == 0) return false;
		return myLanguages.Intersect(otherLanguages).Any();
	}

	public bool CheckDrBypass(string bypass, CreatureStats attacker, Item_SO weaponUsed, NaturalAttack naturalAttackUsed)
	{
		if (string.IsNullOrEmpty(bypass) || bypass == "-") return false;

		string cleanBypass = bypass.Split(';')[0].Split(new[] { "Immune" }, StringSplitOptions.None)[0].Trim().ToLower();
		bool isAndCondition = cleanBypass.Contains(" and ") || (cleanBypass.Contains(",") && !cleanBypass.Contains(" or "));
		
		string[] conditions = cleanBypass.Split(new[] { " or ", " and ", "," }, StringSplitOptions.RemoveEmptyEntries);

		foreach (var condition in conditions)
		{
			string cleanCondition = condition.Trim();
			bool conditionMet = false;

			switch (cleanCondition)
			{
				case "adamantine":
				case "cold iron":
				case "silver":
				case "wood":
					if (weaponUsed != null && weaponUsed.Material.ToString().ToLower() == cleanCondition) conditionMet = true;
					break;
				case "magic":
					if (weaponUsed != null && weaponUsed.Modifications.Any(m => m.StatToModify == StatToModify.AttackRoll && m.BonusType == BonusType.Enhancement && m.ModifierValue > 0)) conditionMet = true;
					break;
				case "epic":
					if (weaponUsed != null && weaponUsed.Modifications.Any(m => m.StatToModify == StatToModify.AttackRoll && m.BonusType == BonusType.Enhancement && m.ModifierValue >= 6)) conditionMet = true;
					break;
				case "good":
				case "evil":
				case "lawful":
				case "chaotic":
					if (attacker != null && attacker.Template.Alignment.ToLower().Contains(cleanCondition)) conditionMet = true;
					break;
				case "aligned":
					if (attacker != null && (attacker.Template.Alignment.ToLower().Contains("good") || attacker.Template.Alignment.ToLower().Contains("evil") || attacker.Template.Alignment.ToLower().Contains("lawful") || attacker.Template.Alignment.ToLower().Contains("chaotic"))) conditionMet = true;
					break;
				case "slashing":
				case "bludgeoning":
				case "piercing":
					if (weaponUsed != null && weaponUsed.DamageInfo.Any(d => d.DamageType.ToLower() == cleanCondition)) conditionMet = true;
					if (naturalAttackUsed != null && naturalAttackUsed.DamageInfo.Any(d => d.DamageType.ToLower() == cleanCondition)) conditionMet = true;
					break;
				case "vorpal":
					 if (weaponUsed != null && weaponUsed.HasVorpalProperty) conditionMet = true;
					break;
			}

			if (isAndCondition && !conditionMet) return false;
			if (!isAndCondition && conditionMet) return true;
		}
		return isAndCondition;
	}

	public void TakeAbilityDamage(AbilityScore score, int amount, bool isDrain = false)
	{
		if (HasImmunity(ImmunityType.AbilityDamage) && !isDrain) return;
		if (HasImmunity(ImmunityType.AbilityDrain) && isDrain) return;
		 if (HasImmunity(ImmunityType.PhysicalAbilityDamage))
		{
			if (score == AbilityScore.Strength || score == AbilityScore.Dexterity || score == AbilityScore.Constitution) return;
		}

		switch (score)
		{
			case AbilityScore.Strength: StrDamage += amount; break;
			case AbilityScore.Dexterity: DexDamage += amount; break;
			case AbilityScore.Constitution:
				ConDamage += amount;
				int newMaxHP = (Template.MaxHP / (Template.Constitution - (ConDamage - amount))) * (Template.Constitution - ConDamage);
				CurrentHP = Mathf.Min(CurrentHP, newMaxHP);
				OnHealthChanged?.Invoke(CurrentHP, newMaxHP);
				break;
			case AbilityScore.Intelligence: IntDamage += amount; break;
			case AbilityScore.Wisdom: WisDamage += amount; break;
			case AbilityScore.Charisma: ChaDamage += amount; break;
		}

		if ((Template.Strength - StrDamage) <= 0 || (Template.Dexterity - DexDamage) <= 0 ||
			(Template.Intelligence - IntDamage) <= 0 || (Template.Wisdom - WisDamage) <= 0 ||
			(Template.Charisma - ChaDamage) <= 0)
		{
			if(!MyEffects.HasCondition(Condition.Helpless))
			{
				var helplessEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Helpless_Effect.tres"); 
				if(helplessEffect != null) MyEffects.AddEffect((StatusEffect_SO)helplessEffect.Duplicate(), null);
			}
		}
		
		if ((Template.Constitution - ConDamage) <= 0)
		{
			Die();
		}
	}

	public void HealAbilityDamage(AbilityScore score, int amount)
	{
		if (score == AbilityScore.None)
		{
			for (int i = 0; i < amount; i++)
			{
				if (ConDamage > 0) ConDamage--;
				else if (StrDamage > 0) StrDamage--;
				else if (DexDamage > 0) DexDamage--;
				else if (WisDamage > 0) WisDamage--;
				else if (IntDamage > 0) IntDamage--;
				else if (ChaDamage > 0) ChaDamage--;
			}
			return;
		}

		switch (score)
		{
			case AbilityScore.Strength: StrDamage = Mathf.Max(0, StrDamage - amount); break;
			case AbilityScore.Dexterity: DexDamage = Mathf.Max(0, DexDamage - amount); break;
			case AbilityScore.Constitution: ConDamage = Mathf.Max(0, ConDamage - amount); break;
			case AbilityScore.Intelligence: IntDamage = Mathf.Max(0, IntDamage - amount); break;
			case AbilityScore.Wisdom: WisDamage = Mathf.Max(0, WisDamage - amount); break;
			case AbilityScore.Charisma: ChaDamage = Mathf.Max(0, ChaDamage - amount); break;
		}
	}
	 public void AddTemporaryHP(int amount)
	{
		TemporaryHP += amount; 
		GD.Print($"{Name} gained {amount} Temporary HP.");
	}

   public int GetInitiativeBonus()
	{
		int bonus = Template.TotalInitiativeModifier + DexModifier;
		if (Template.HasSpiritsense) bonus += 2; 
		bonus += MyEffects?.GetTotalModifier(StatToModify.Initiative) ?? 0;
		return bonus;
	}
	 public bool IsImmuneToCriticalHits()
	{
		// 1. Check Explicit Immunity List
		if (Template.Immunities != null && Template.Immunities.Contains("Critical Hits", StringComparer.OrdinalIgnoreCase)) return true;

		// 2. Check Types
		if (Template.Type == CreatureType.Elemental) return true;
		if (Template.Type == CreatureType.Ooze) return true;
		
		// 3. Check Subtypes
		if (Template.SubTypes != null)
		{
			if (Template.SubTypes.Contains("Aeon")) return true;
			if (Template.SubTypes.Contains("Incorporeal")) return true; // Spells usually don't have Ghost Touch
			if (Template.SubTypes.Contains("Swarm")) return true;
			if (Template.SubTypes.Contains("Protean")) return true;
		}
		
		// 4. Check Effects
		if (MyEffects.HasCondition(Condition.Gaseous)) return true;

		return false;
	}
}
