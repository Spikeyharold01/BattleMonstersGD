using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: PersistentEffect_Whirlpool.cs (GODOT VERSION)
// PURPOSE: Manages Whirlpool/Vortex hazards. Handles pulling, damage, and penalties.
// ATTACH TO: Whirlpool Prefab (Node3D).
// =================================================================================================
public partial class PersistentEffect_Whirlpool : Node3D
{
    [Export] public float BaseRadius = 37.5f; 
    [Export] public float Depth = 35f;
    [Export] public int SaveDC = 25;
    [Export] public int DamageDiceCount = 2; 
    [Export] public int DamageDieSides = 8;
    
    [Export] public StatusEffect_SO TrappedEffect; 
    
    private CreatureStats caster;
    private float duration;
    private HashSet<CreatureStats> trappedCreatures = new HashSet<CreatureStats>();

    public void Initialize(CreatureStats source, float dur)
    {
        caster = source;
        duration = dur;
        AddToGroup("Whirlpool");
    }

    public override void _Process(double delta)
    {
        duration -= (float)delta;
        if (duration <= 0)
        {
            foreach(var c in trappedCreatures) RemoveTrappedStatus(c);
            QueueFree();
        }
    }

    // Called by TurnManager/EnvironmentManager start of turn logic
    public void ApplyTurnEffects(CreatureStats creature)
    {
        float dist = GlobalPosition.DistanceTo(creature.GlobalPosition);
        if (dist > BaseRadius) 
        {
            if(trappedCreatures.Contains(creature)) RemoveTrappedStatus(creature);
            return;
        }

        // 1. Initial Contact / Avoidance
        if (!trappedCreatures.Contains(creature))
        {
            GD.Print($"{creature.Name} contacts the whirlpool!");
            
            int reflex = Dice.Roll(1, 20) + creature.GetReflexSave(caster);
            int swim = Dice.Roll(1, 20) + creature.GetSkillBonus(SkillType.Swim);
            
            if (reflex >= SaveDC || swim >= SaveDC)
            {
                GD.Print($"{creature.Name} avoids being sucked in.");
                return;
            }
            
            GD.PrintRich($"[color=red]{creature.Name} is sucked into the whirlpool![/color]");
            AddTrappedStatus(creature);
        }

        // 2. Trapped Effects
        if (trappedCreatures.Contains(creature))
        {
            int dmg = Dice.Roll(DamageDiceCount, DamageDieSides);
            creature.TakeDamage(dmg, "Bludgeoning", caster);
            
            // Set context for Swim check
            var swimCtrl = creature.GetNodeOrNull<SwimController>("SwimController");
            if (swimCtrl != null) 
            {
                swimCtrl.HasAttemptedSwimCheck = false;
                swimCtrl.CurrentEnvironmentalDC = SaveDC;
            }
        }
    }

    // Called by TurnManager at END of creature turn
    public void OnCreatureTurnEnd(CreatureStats creature)
    {
        if (trappedCreatures.Contains(creature))
        {
            var swimCtrl = creature.GetNodeOrNull<SwimController>("SwimController");
            // Rule: "If an affected creature opts to not take a standard action... automatically pulled underwater."
            if (swimCtrl == null || !swimCtrl.HasAttemptedSwimCheck)
            {
                GD.PrintRich($"[color=red]{creature.Name} did not try to swim! Pulled underwater.[/color]");
                creature.GetNodeOrNull<SwimController>("SwimController")?.GoUnderwater();
            }
        }
    }

    private void AddTrappedStatus(CreatureStats c)
    {
        trappedCreatures.Add(c);
        if (TrappedEffect != null)
        {
            var instance = (StatusEffect_SO)TrappedEffect.Duplicate();
            instance.EffectName = "Caught in Whirlpool";
            instance.DurationInRounds = 0;
            c.MyEffects.AddEffect(instance, caster);
        }
    }

    private void RemoveTrappedStatus(CreatureStats c)
    {
        if (trappedCreatures.Remove(c))
        {
            c.MyEffects.RemoveEffect("Caught in Whirlpool");
        }
    }

    public void MoveWhirlpool(Vector3 targetPos)
    {
        GlobalPosition = targetPos;
    }
}