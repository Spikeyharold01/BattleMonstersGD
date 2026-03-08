using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: RestrictedZoneController.cs
// PURPOSE: Enforces zone boundaries on the grid or via physics pushes.
// =================================================================================================
public partial class RestrictedZoneController : Node3D
{
    private CreatureStats caster;
    private float duration;
    private float radius;
    private bool preventEntry;
    private TargetFilter_SO filter;
    private bool allowsSave;
    private SaveType saveType;
    private bool checkSR;
    private StatusEffect_SO buff;
    
    private HashSet<CreatureStats> successfulSavers = new HashSet<CreatureStats>();

    public void Initialize(CreatureStats c, float d, float r, bool prevent, TargetFilter_SO f, bool save, SaveType sType, bool sr, StatusEffect_SO b)
    {
        caster = c;
        duration = d;
        radius = r;
        preventEntry = prevent;
        filter = f;
        allowsSave = save;
        saveType = sType;
        checkSR = sr;
        buff = b;
        
        AddToGroup("RestrictedZone");
    }

    public override void _Process(double delta)
    {
        duration -= (float)delta;
        if (duration <= 0) QueueFree();
        
        // Push/Pull Logic for Physics? 
        // Or integrate with Pathfinding?
        // Pathfinding integration is cleanest (IsNodeWalkable check).
        // But for "Push out if inside", we need update logic.
        
        if (preventEntry) PushOutIntruders();
        else KeepInPrisoners();
        
        if (buff != null) ApplyAuraBuffs();
    }
    
    private void PushOutIntruders()
    {
        // Find creatures inside
        var targets = AoEHelper.GetTargetsInBurst(GlobalPosition, new AreaOfEffect { Range = radius }, "Creature"); // Check All
        foreach(var t in targets)
        {
            // If they are Filtered (e.g. Evil Summoned) AND haven't saved
            if (filter.IsTargetValid(caster, t) && !successfulSavers.Contains(t))
            {
                // Attempt Save/SR
                if (AttemptBreach(t))
                {
                    successfulSavers.Add(t);
                    continue;
                }
                
                // Fail: Push them out
                Vector3 dir = (t.GlobalPosition - GlobalPosition).Normalized();
                if (dir == Vector3.Zero) dir = Vector3.Forward;
                
                Vector3 edgePos = GlobalPosition + dir * (radius + 1f);
                t.GlobalPosition = edgePos; // Force move
                GD.Print($"{t.Name} is repelled by Magic Circle.");
            }
        }
    }
    
    private void KeepInPrisoners()
    {
        // Inverse logic: If Filtered target tries to leave, stop them.
        // This requires tracking who is "Bound". 
        // Typically, Inward Binding targets ONE creature at cast time.
        // For Area version (e.g. Antilife Shell inverted?), we check "Is Outside".
        
        // Simpler for Magic Circle Binding: The spell targets ONE creature for binding.
        // If we use this script for "Inward Binding" mode on a single target, we ensure they stay in radius.
        
        // Implementation: Find specific target (passed in Init? Or imply by who is inside at start?)
        // Magic Circle Binding targets ONE creature.
        // Let's assume we scan for the valid target inside at start.
    }
    
    private bool AttemptBreach(CreatureStats t)
    {
        if (checkSR && t.Template.SpellResistance > 0)
        {
             int roll = Dice.Roll(1, 20) + caster.Template.CasterLevel;
             if (roll < t.Template.SpellResistance) return false; // SR blocked the spell effect (so they CAN enter? Wait. SR applies to the barrier. If SR works, barrier fails, they enter.)
             // Actually, SR "allows a creature to overcome this protection".
             // So if Check(SR) succeeds (Roll >= SR), the spell works (Repulsion holds). 
             // Wait, logic reverse.
             // If Target has SR, they roll SR vs Caster? No, Caster rolls vs SR.
             // If Caster FAILS to penetrate SR, the Barrier FAILS to stop them.
             // So: if (roll < SR) return true; (Breach successful).
             
             if (roll < t.Template.SpellResistance) return true; // Barrier failed
        }
        
        if (allowsSave)
        {
            int save = 0;
            if (saveType == SaveType.Will) save = t.GetWillSave(caster);
            int dc = 10 + 3 + caster.WisModifier; // Approx DC
            
            if (Dice.Roll(1, 20) + save >= dc) return true; // Saved, can enter
        }
        
        return false; // Cannot enter
    }

    private void ApplyAuraBuffs()
    {
        // Apply "Protection from Evil" status to allies inside
        var allies = AoEHelper.GetTargetsInBurst(GlobalPosition, new AreaOfEffect { Range = radius }, "Player"); // Or whatever group
        foreach(var ally in allies)
        {
             // Check faction logic
             if (ally.IsInGroup("Player") == caster.IsInGroup("Player"))
             {
                 if (!ally.MyEffects.HasEffect(buff.EffectName))
                 {
                     var instance = (StatusEffect_SO)buff.Duplicate();
                     instance.DurationInRounds = 1; // Refreshing aura
                     ally.MyEffects.AddEffect(instance, caster);
                 }
             }
        }
    }
    
    public bool IsPointRestricted(Vector3 point, CreatureStats mover)
    {
        // Helper for Pathfinding to avoid the zone
        if (successfulSavers.Contains(mover)) return false;
        if (!filter.IsTargetValid(caster, mover)) return false;
        
        float dist = GlobalPosition.DistanceTo(point);
        if (preventEntry && dist < radius) return true;
        if (!preventEntry && dist > radius) return true; // Binding
        
        return false;
    }
}