using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: UnhallowAuraController.cs
// PURPOSE: Runtime controller for the Unhallow 40ft area. Manages Magic Circle, Channel Mods, and Tied Spells.
// =================================================================================================
public partial class UnhallowAuraController : Node3D
{
	private CreatureStats caster;
	private Ability_SO sourceAbility;
	private Ability_SO tiedSpell;
	private float radius;
	private TiedSpellTargetMode targetMode;
	
	private StatusEffect_SO magicCircleEffect;
	private StatusEffect_SO channelModEffect;

	private HashSet<CreatureStats> creaturesInAura = new HashSet<CreatureStats>();
	private static int nextInstanceID = 1000;
	private int instanceID;

	public void Initialize(CreatureStats caster, Ability_SO sourceAbility, Ability_SO tiedSpell, float radius, TiedSpellTargetMode targetMode, StatusEffect_SO magicCircle, StatusEffect_SO channelMod)
	{
		this.caster = caster;
		this.sourceAbility = sourceAbility;
		this.tiedSpell = tiedSpell;
		this.radius = radius;
		this.targetMode = targetMode;
		this.magicCircleEffect = magicCircle;
		this.channelModEffect = channelMod;
		this.instanceID = nextInstanceID++;

		AddToGroup("UnhallowAuras");
	}

	public override void _Process(double delta)
	{
		if (!GodotObject.IsInstanceValid(caster) || caster.CurrentHP <= -caster.Template.Constitution)
		{
			RemoveEffectsFromAll();
			QueueFree();
			return;
		}

		TickAuraCheck();
	}

private void TickAuraCheck()
	{
		var creaturesNowInArea = new HashSet<CreatureStats>();
		var spaceState = GetWorld3D().DirectSpaceState;
		var query = new PhysicsShapeQueryParameters3D 
		{ 
			Shape = new SphereShape3D { Radius = radius }, 
			Transform = new Transform3D(Basis.Identity, GlobalPosition), 
			CollisionMask = 2 // Layer 2 is the Creature layer in your architecture
		};

		var hits = spaceState.IntersectShape(query);
		foreach (var dict in hits)
		{
			var hitNode = (Node3D)dict["collider"];
			var creature = hitNode as CreatureStats ?? hitNode.GetNodeOrNull<CreatureStats>("CreatureStats");
			if (creature != null) creaturesNowInArea.Add(creature);
		}
		
		
		foreach (var creature in creaturesNowInArea.Except(creaturesInAura))
		{
			OnCreatureEnter(creature);
		}

		foreach (var creature in creaturesInAura.Except(creaturesNowInArea))
		{
			OnCreatureExit(creature);
		}

		creaturesInAura = creaturesNowInArea;

		// Repel Good Summoned Creatures (Magic Circle physical barrier logic)
		foreach (var creature in creaturesInAura)
		{
			if (creature.IsSummoned && creature.Template.Alignment.Contains("Good"))
			{
				// Attempt SR check to breach
				int breachCheck = Dice.Roll(1, 20) + caster.Template.CasterLevel;
				if (breachCheck < creature.Template.SpellResistance) continue; // They breached the circle

				Vector3 pushDir = (creature.GlobalPosition - GlobalPosition).Normalized();
				if (pushDir == Vector3.Zero) pushDir = Vector3.Forward;
				
				creature.GlobalPosition = GlobalPosition + pushDir * (radius + 1f);
				GD.Print($"{creature.Name} is repelled by the Magic Circle against Good.");
			}
		}
	}

	private void OnCreatureEnter(CreatureStats creature)
	{
		// 1. Apply Magic Circle Buffs
		if (magicCircleEffect != null)
		{
			ApplyTrackedStatus(creature, magicCircleEffect);
		}

		// 2. Apply Channel Modifiers
		if (channelModEffect != null)
		{
			ApplyTrackedStatus(creature, channelModEffect);
		}

		// 3. Execute Tied Spell
		if (tiedSpell != null && IsTiedSpellTargetValid(creature))
		{
			var context = new EffectContext
			{
				Caster = caster,
				PrimaryTarget = creature,
				AllTargetsInAoE = new Godot.Collections.Array<CreatureStats> { creature }
			};

			bool saved = false;
			if (tiedSpell.SavingThrow != null && tiedSpell.SavingThrow.SaveType != SaveType.None)
			{
				int saveDC = tiedSpell.SavingThrow.BaseDC; // Real implementation uses dynamic DC logic
				int saveRoll = RollManager.Instance.MakeD20Roll(creature) + creature.GetWillSave(caster, tiedSpell); // Simplified to Will for generic
				if (saveRoll >= saveDC) saved = true;
			}

			var saveResults = new Dictionary<CreatureStats, bool> { { creature, saved } };

			foreach (var component in tiedSpell.EffectComponents)
			{
				if (component is ApplyStatusEffect applyStatusEffect)
				{
					if (!saved) ApplyTrackedStatus(creature, applyStatusEffect.EffectToApply);
				}
				else
				{
					// Instantaneous effects (like Dispel Magic) hit them right as they enter
					component.ExecuteEffect(context, tiedSpell, saveResults);
				}
			}
		}
	}

	private void OnCreatureExit(CreatureStats creature)
	{
		creature.MyEffects.RemoveEffectsFromSource(instanceID);
	}

	private void RemoveEffectsFromAll()
	{
		foreach (var creature in creaturesInAura)
		{
			if (GodotObject.IsInstanceValid(creature))
			{
				creature.MyEffects.RemoveEffectsFromSource(instanceID);
			}
		}
	}

	private void ApplyTrackedStatus(CreatureStats target, StatusEffect_SO effectData)
	{
		var activeEffect = new ActiveStatusEffect(effectData, caster)
		{
			SourcePersistentEffectID = this.instanceID,
			SourceSpellLevel = sourceAbility.SpellLevel
		};
		target.MyEffects.ActiveEffects.Add(activeEffect);
	}

	private bool IsTiedSpellTargetValid(CreatureStats target)
	{
		// Rule: Construct always benefits from the tied spell.
		if (target == caster) return true;

		switch (targetMode)
		{
			case TiedSpellTargetMode.AllCreatures:
				return true;
			case TiedSpellTargetMode.SameAlignment:
				return target.Template.Alignment == caster.Template.Alignment;
			case TiedSpellTargetMode.OpposedAlignment:
				return target.Template.Alignment != caster.Template.Alignment;
			default:
				return false;
		}
	}
}
