using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: PersistentAuraController.cs (GODOT VERSION)
// PURPOSE: Generic Aura logic (Fear, Stench, Fire).
// ATTACH TO: Creature (Child Node).
// =================================================================================================
public partial class PersistentAuraController : Godot.Node
{
	[Export] public string AuraName = "Fear Aura";
	[Export] public float Radius = 30f;
	[Export] public bool IsActive = true;
	[Export] public Ability_SO SourceAbility; // To get DC/SaveType
	
	// Config
	[Export] public StatusEffect_SO EffectOnFail;
	[Export] public StatusEffect_SO ImmunityOnSave; // e.g. "Immune to Bishop Aura"
	[Export] public bool CheckLineOfSight = true; // Fear usually requires sight? "appearance evokes feelings" -> Yes.
	
	private CreatureStats owner;

	public override void _Ready()
	{
		owner = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
		AddToGroup("PersistentAura"); // For TurnManager to find
	}

	// Called by TurnManager when ANY creature starts their turn
	public void CheckAuraExposure(CreatureStats victim)
	{
		if (!IsActive || owner.CurrentHP <= 0) return;
		if (victim == owner) return;
		if (SourceAbility == null) return;

		// 1. Check Range
		float dist = owner.GlobalPosition.DistanceTo(victim.GlobalPosition);
		if (dist > Radius) return;

		// 2. Check LoS
		if (CheckLineOfSight)
		{
			if (!LineOfSightManager.GetVisibility(victim, owner).HasLineOfSight) return;
		}

		// 3. Check Immunity
		// Check for specific immunity effect (from previous save)
		if (ImmunityOnSave != null && victim.MyEffects.HasEffect(ImmunityOnSave.EffectName)) return;
		
		// 4. Resolve (Save)
		// Use Ability_SO logic if available, or manual roll
		int dc = CalculateSaveDC();

		GD.Print($"{victim.Name} is exposed to {AuraName}!");
		
		int saveBonus = 0;
		switch(SourceAbility.SavingThrow.SaveType)
		{
			case SaveType.Will: saveBonus = victim.GetWillSave(owner, SourceAbility); break;
			case SaveType.Fortitude: saveBonus = victim.GetFortitudeSave(owner, SourceAbility); break;
			case SaveType.Reflex: saveBonus = victim.GetReflexSave(owner, SourceAbility); break;
		}

		int roll = Dice.Roll(1, 20) + saveBonus;
		if (roll >= dc)
		{
			GD.PrintRich($"[color=green]{victim.Name} resists {AuraName}. Immune for 24 hours.[/color]");
			if (ImmunityOnSave != null)
			{
				var immunity = (StatusEffect_SO)ImmunityOnSave.Duplicate();
				victim.MyEffects.AddEffect(immunity, owner);
			}
		}
		else
		{
			GD.PrintRich($"[color=red]{victim.Name} succumbs to {AuraName}![/color]");
			if (EffectOnFail == null) return;

			var effect = (StatusEffect_SO)EffectOnFail.Duplicate();
			
			victim.MyEffects.AddEffect(effect, owner, SourceAbility);
		}
	}

	private int CalculateSaveDC()
	{
		var savingThrow = SourceAbility.SavingThrow;
		if (savingThrow == null) return 10;

		if (!savingThrow.IsDynamicDC && !savingThrow.IsSpecialAbilityDC)
		{
			return savingThrow.BaseDC;
		}

		int statMod = GetAbilityModifierForDC(savingThrow.DynamicDCStat);
		if (savingThrow.IsSpecialAbilityDC)
		{
			int hitDiceCount = 0;
			if (!string.IsNullOrEmpty(owner.Template?.HitDice))
			{
				var match = System.Text.RegularExpressions.Regex.Match(owner.Template.HitDice, @"^\d+");
				if (match.Success) int.TryParse(match.Value, out hitDiceCount);
			}

			// Special ability DC: 10 + 1/2 HD + ability modifier.
			return 10 + Mathf.FloorToInt(hitDiceCount / 2f) + statMod + savingThrow.DynamicDCBonus;
		}

		// Spell-like dynamic DC: 10 + 1/2 caster level + ability modifier.
		return 10 + Mathf.FloorToInt(owner.Template.CasterLevel / 2f) + statMod + savingThrow.DynamicDCBonus;
	}

	private int GetAbilityModifierForDC(AbilityScore score)
	{
		return score switch
		{
			AbilityScore.Strength => owner.StrModifier,
			AbilityScore.Dexterity => owner.DexModifier,
			AbilityScore.Constitution => owner.ConModifier,
			AbilityScore.Intelligence => owner.IntModifier,
			AbilityScore.Wisdom => owner.WisModifier,
			AbilityScore.Charisma => owner.ChaModifier,
			_ => 0
		};
	}

	public void Toggle(bool state)
	{
		IsActive = state;
		GD.Print($"{AuraName} is now {(IsActive ? "Active" : "Inactive")}.");
	}
}
