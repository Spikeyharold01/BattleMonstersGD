using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: WhirlwindController.cs
// PURPOSE: Manages movement interactions, trapping, debris clouds, and damage payloads for a Whirlwind.
// =================================================================================================
public partial class WhirlwindController : Node3D
{
    private CreatureStats caster;
    private Ability_SO sourceAbility;
    private float height;
    private DamageInfo baseDamage;
    private Godot.Collections.Array<Ability_SO> payloads;
    private int saveDC;
    private float originalFlySpeed;

    private Area3D overlapArea;
    private EnvironmentalZone debrisCloud;
    
    private HashSet<CreatureStats> alreadyHitThisTurn = new HashSet<CreatureStats>();
    private List<CreatureStats> trappedCreatures = new List<CreatureStats>();

    public void Initialize(CreatureStats caster, Ability_SO sourceAbility, float height, DamageInfo baseDamage, Godot.Collections.Array<Ability_SO> payloads)
    {
        this.caster = caster;
        this.sourceAbility = sourceAbility;
        this.height = Mathf.Max(10f, height);
        this.baseDamage = baseDamage;
        this.payloads = payloads;

        int hd = CreatureRulesUtility.GetHitDiceCount(caster);
        this.saveDC = 10 + (hd / 2) + caster.StrModifier;

        // 1. Setup Flight Speed
        originalFlySpeed = caster.Template.Speed_Fly;
        if (caster.Template.Speed_Fly <= 0)
        {
            caster.Template.Speed_Fly = caster.Template.Speed_Land;
            caster.Template.FlyManeuverability = FlyManeuverability.Average;
        }

        // 2. Setup Overlap Area (5ft base, tapering up)
        overlapArea = new Area3D();
        var collision = new CollisionShape3D();
        collision.Shape = new CylinderShape3D { Radius = 5f, Height = this.height };
        collision.Position = new Vector3(0, this.height / 2f, 0); // Cylinder is centered, lift it up
        overlapArea.AddChild(collision);
        AddChild(overlapArea);

        // We use physics process to manually check overlaps rather than relying entirely on BodyEntered signals
        // because we need to track if we move *through* them during a turn.

        // 3. Setup Debris Cloud (Hidden until grounded)
        debrisCloud = new EnvironmentalZone();
        debrisCloud.Tags.Add("DebrisCloud");
        debrisCloud.Tags.Add("Fog"); // Gives it the standard concealment mechanics automatically
        debrisCloud.Radius = this.height / 4f; // Radius is half of diameter (which is half of height)
        AddChild(debrisCloud);

        GD.PrintRich($"[color=cyan]{caster.Name} transforms into a raging Whirlwind! (DC {saveDC})[/color]");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (caster == null || !GodotObject.IsInstanceValid(caster) || caster.CurrentHP <= 0 || !caster.MyEffects.HasCondition(Condition.WhirlwindForm))
        {
            EjectAll();
            QueueFree();
            return;
        }

        ManageDebrisCloud();
        CheckOverlaps();
        UpdateTrappedCreatures();
    }

    // Called by TurnManager natively (simulated here for clarity)
    public void OnTurnStart()
    {
        alreadyHitThisTurn.Clear();

        // Trapped creatures take automatic damage at the start of the whirlwind's turn,
        // and flying ones get a save to escape.
        for (int i = trappedCreatures.Count - 1; i >= 0; i--)
        {
            var victim = trappedCreatures[i];
            
            // Allow escape if they can fly
            if (victim.Template.Speed_Fly > 0)
            {
                int reflex = Dice.Roll(1, 20) + victim.GetReflexSave(caster);
                if (reflex >= saveDC)
                {
                    GD.PrintRich($"[color=green]{victim.Name} escapes the whirlwind by flying away![/color]");
                    Eject(victim);
                    continue;
                }
            }

            ApplyWhirlwindDamage(victim);
        }
    }

    private void ManageDebrisCloud()
    {
        // If grounded (Y drop is 0), debris cloud is active.
        GridNode gridNode = GridManager.Instance?.NodeFromWorldPoint(caster.GlobalPosition);
        if (gridNode != null)
        {
            bool isGrounded = gridNode.heightOfDropBelow == 0;
            if (debrisCloud.IsInsideTree() != isGrounded)
            {
                if (isGrounded) AddChild(debrisCloud); // Activate
                else RemoveChild(debrisCloud);         // Deactivate
            }
        }
    }

    private void CheckOverlaps()
    {
        var bodies = overlapArea.GetOverlappingBodies();
        foreach (Node3D body in bodies)
        {
            var victim = body as CreatureStats ?? body.GetNodeOrNull<CreatureStats>("CreatureStats");
            if (victim == null || victim == caster || alreadyHitThisTurn.Contains(victim) || trappedCreatures.Contains(victim)) continue;

            if ((int)victim.Template.Size < (int)caster.Template.Size)
            {
                alreadyHitThisTurn.Add(victim);
                ProcessNewVictim(victim);
            }
        }
    }

    private void ProcessNewVictim(CreatureStats victim)
    {
        GD.Print($"{victim.Name} is caught in the path of the Whirlwind!");

        // First Save: Avoid Damage
        int dmgSave = Dice.Roll(1, 20) + victim.GetReflexSave(caster);
        if (dmgSave < saveDC)
        {
            GD.PrintRich($"[color=red]{victim.Name} fails 1st save and takes slam damage![/color]");
            ApplyWhirlwindDamage(victim);
        }
        else
        {
            GD.Print($"{victim.Name} dodges the physical battering of the winds.");
        }

        // Second Save: Avoid being picked up
        int pickupSave = Dice.Roll(1, 20) + victim.GetReflexSave(caster);
        if (pickupSave < saveDC)
        {
            GD.PrintRich($"[color=red]{victim.Name} fails 2nd save and is swept up into the Whirlwind![/color]");
            
            var trappedStatus = new StatusEffect_SO
            {
                EffectName = "Trapped in Whirlwind",
                ConditionApplied = Condition.TrappedInWhirlwind,
                DurationInRounds = 0 // Lasts until ejected or escapes
            };
            trappedStatus.Modifications.Add(new StatModification { StatToModify = StatToModify.Dexterity, ModifierValue = -4, IsPenalty = true });
            trappedStatus.Modifications.Add(new StatModification { StatToModify = StatToModify.AttackRoll, ModifierValue = -2, IsPenalty = true });

            victim.MyEffects.AddEffect(trappedStatus, caster);
            trappedCreatures.Add(victim);
        }
        else
        {
            GD.Print($"{victim.Name} anchors themselves and avoids being swept up.");
        }
    }

    private void ApplyWhirlwindDamage(CreatureStats victim)
    {
        if (baseDamage != null && baseDamage.DiceCount > 0)
        {
            int dmg = Dice.Roll(baseDamage.DiceCount, baseDamage.DieSides) + baseDamage.FlatBonus + caster.StrModifier;
            victim.TakeDamage(dmg, baseDamage.DamageType, caster);
        }

        // Apply Extra Payloads (e.g., Lightning's Kiss)
        if (payloads != null)
        {
            var context = new EffectContext { Caster = caster, PrimaryTarget = victim, AllTargetsInAoE = new Godot.Collections.Array<CreatureStats> { victim } };
            var emptySaves = new Dictionary<CreatureStats, bool>();

            foreach (var payload in payloads)
            {
                if (payload == null) continue;
                foreach (var component in payload.EffectComponents)
                {
                    component.ExecuteEffect(context, payload, emptySaves);
                }
            }
        }
    }

    private void UpdateTrappedCreatures()
    {
        foreach (var victim in trappedCreatures)
        {
            if (GodotObject.IsInstanceValid(victim))
            {
                // Suspend them inside the whirlwind (above the caster)
                victim.GlobalPosition = caster.GlobalPosition + new Vector3(0, height / 2f, 0);
            }
        }
    }

    private void Eject(CreatureStats victim)
    {
        trappedCreatures.Remove(victim);
        victim.MyEffects.RemoveEffect("Trapped in Whirlwind");
        
        // Deposit in caster's space (they will get pushed to an adjacent tile by standard overlap logic in Pathfinder, or they just drop).
        victim.GlobalPosition = caster.GlobalPosition;
    }

    public void EjectAll()
    {
        foreach (var victim in trappedCreatures.ToList())
        {
            if (GodotObject.IsInstanceValid(victim)) Eject(victim);
        }
    }

    public override void _ExitTree()
    {
        if (caster != null && GodotObject.IsInstanceValid(caster))
        {
            caster.Template.Speed_Fly = originalFlySpeed;
        }
        EjectAll();
    }
}