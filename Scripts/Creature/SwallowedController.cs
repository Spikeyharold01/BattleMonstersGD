using Godot;

// =================================================================================================
// FILE: SwallowedController.cs
// PURPOSE: Manages the state of a creature that has been swallowed whole.
// ATTACH TO: Spawned dynamically on the victim.
// =================================================================================================
public partial class SwallowedController : GridNode
{
    public CreatureStats Swallower { get; private set; }
    private CreatureStats victim;
    private DamageInfo stomachDamage;
    private int damageDealtToStomach = 0;
    private int requiredCutDamage = 0;

    public void Initialize(CreatureStats victim, CreatureStats swallower, DamageInfo damagePerRound)
    {
        this.victim = victim;
        this.Swallower = swallower;
        this.stomachDamage = damagePerRound;
        this.requiredCutDamage = Mathf.Max(1, swallower.Template.MaxHP / 10);
        
        // Ensure victim stays with swallower
        victim.GlobalPosition = swallower.GlobalPosition;

        // Apply Grappled secretly to ensure restrictions without a GrappleState object
        var grappledEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Grappled_Effect.tres");
        if (grappledEffect != null) victim.MyEffects.AddEffect((StatusEffect_SO)grappledEffect.Duplicate(), swallower);
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(Swallower) || Swallower.IsDead)
        {
            GD.PrintRich($"[color=green]{Swallower?.Name ?? "The monster"} is dead! {victim.Name} climbs out of the stomach.[/color]");
            FreeVictim();
            return;
        }

        // Stick to the swallower
        victim.GlobalPosition = Swallower.GlobalPosition;
    }

    // Called by ActionManager.OnTurnStart
    public void OnTurnStart()
    {
        if (stomachDamage != null)
        {
            int dmg = Dice.Roll(stomachDamage.DiceCount, stomachDamage.DieSides) + stomachDamage.FlatBonus;
            GD.Print($"{victim.Name} takes {dmg} {stomachDamage.DamageType} damage from being inside {Swallower.Name}'s stomach!");
            victim.TakeDamage(dmg, stomachDamage.DamageType, Swallower);
        }
    }

    public void RecordCutDamage(int damage)
    {
        damageDealtToStomach += damage;
        GD.Print($"{victim.Name} cuts the stomach for {damage} damage! ({damageDealtToStomach}/{requiredCutDamage} to escape).");

        if (damageDealtToStomach >= requiredCutDamage)
        {
            GD.PrintRich($"[color=green]Success! {victim.Name} cuts their way out of {Swallower.Name}'s stomach![/color]");
            
            // Apply Ruptured Status to Swallower
            var rupturedStatus = new StatusEffect_SO
            {
                EffectName = "Stomach Ruptured",
                Description = "Cannot use Swallow Whole until healed.",
                DurationInRounds = 0
            };
            Swallower.MyEffects.AddEffect(rupturedStatus, null);

            var ruptureCtrl = new StomachRuptureController();
            ruptureCtrl.Name = "StomachRuptureController";
            Swallower.AddChild(ruptureCtrl);
            ruptureCtrl.Initialize(Swallower);

            FreeVictim();
        }
    }

    public void EscapeViaGrapple()
    {
        GD.PrintRich($"[color=green]{victim.Name} crawls back up the throat into the mouth![/color]");
        
        // Remove Swallowed, but re-initiate standard Grapple
        victim.MyEffects.RemoveEffect("Swallowed Whole");
        QueueFree();

        CombatManeuvers.ResolveGrapple(Swallower, victim, true, null); // Re-establish standard grapple
    }

    private void FreeVictim()
    {
        victim.MyEffects.RemoveEffect("Swallowed Whole");
        victim.MyEffects.RemoveEffect("Grappled Effect");

        // Move to adjacent valid square
        var swallowerNode = GridManager.Instance.NodeFromWorldPoint(Swallower.GlobalPosition);
        foreach (var neighbor in GridManager.Instance.GetNeighbours(swallowerNode))
        {
            if (neighbor.terrainType == TerrainType.Ground && !CombatCalculations.IsSquareOccupied(neighbor, victim))
            {
                victim.GlobalPosition = neighbor.worldPosition;
                break;
            }
        }

        QueueFree();
    }
}