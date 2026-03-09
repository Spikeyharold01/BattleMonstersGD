using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: DetectMagicController.cs (GODOT VERSION)
// PURPOSE: Manages the state of an active Detect Magic spell on a creature.
// ATTACH TO: A creature at runtime when they cast Detect Magic (Child Node).
// =================================================================================================
public partial class DetectMagicController : GridNode
{
private CreatureStats caster;
private float remainingDuration;
private int roundsConcentrated = 0;

// Optional visualizer for the cone could be a Child Node3D, handled externally or by scene setup

public void Initialize(CreatureStats source)
{
    this.caster = source;
    // Duration: 1 min/level = 10 rounds/level
    // In this system, duration is tracked in rounds usually, but here float time is used
    this.remainingDuration = caster.Template.CasterLevel * 10f * 6f; // 6 seconds per round
}

public override void _Process(double delta)
{
    remainingDuration -= (float)delta;
    if (remainingDuration <= 0)
    {
        GD.Print($"{caster.Name}'s Detect Magic has expired.");
        QueueFree();
    }
}

// Called by the new Concentrate action logic (likely via AI or Player Controller)
public void Concentrate()
{
    roundsConcentrated++;
    GD.Print($"{caster.Name} concentrates on Detect Magic (Round {roundsConcentrated})...");
    
    // Find all aura controllers within the cone
    var aurasInCone = FindAurasInCone();

    if (roundsConcentrated == 1)
    {
        if (aurasInCone.Any())
            GD.Print("Result: Presence of magical auras detected.");
        else
            GD.Print("Result: Absence of magical auras.");
    }
    else if (roundsConcentrated == 2)
    {
        if (aurasInCone.Any())
        {
            int totalAuras = aurasInCone.Sum(ac => ac.Auras.Count);
            var mostPotent = aurasInCone.Max(ac => ac.GetMostPotentAuraStrength());
            GD.Print($"Result: {totalAuras} auras detected. Most potent is {mostPotent}.");
            
            // Record findings for the AI
            foreach(var auraController in aurasInCone)
            {
                // AuraController is usually a child of the CreatureStats root or an item
                var creature = auraController.GetParent() as CreatureStats; 
                if (creature == null) creature = auraController.GetParent().GetNodeOrNull<CreatureStats>("CreatureStats");

                if (creature != null)
                {
                    CombatMemory.RecordMagicalAura(creature, auraController.GetMostPotentAuraStrength());
                }
            }
        }
        else
        {
             GD.Print("Result: Absence of magical auras.");
        }
    }
    else // Round 3+
    {
        if (aurasInCone.Any())
        {
            GD.Print("Result: Strengths and locations:");
            foreach (var auraController in aurasInCone)
            {
                foreach (var aura in auraController.Auras)
                {
                      var sourceName = auraController.GetParent()?.Name ?? "Unknown";
                      GD.Print($"-- Aura of {aura.Strength} strength on {sourceName} from '{aura.SourceName}'.");
                }
                
                var creature = auraController.GetParent() as CreatureStats;
                if (creature == null) creature = auraController.GetParent().GetNodeOrNull<CreatureStats>("CreatureStats");

                if (creature != null)
                {
                    CombatMemory.RecordMagicalAura(creature, auraController.GetMostPotentAuraStrength());
                }
            }
        }
         else
        {
             GD.Print("Result: Absence of magical auras.");
        }
    }
}

private List<AuraController> FindAurasInCone()
{
    var foundAuras = new List<AuraController>();
    
    // Find all AuraController nodes in the scene group "AuraControllers"
    // This requires AuraController.cs to add itself to this group in _Ready ideally.
    // Assuming AuraController adds itself to "AuraControllers" group or we traverse scene.
    // For efficiency in Godot, using Groups is standard.
    var allAuraNodes = GetTree().GetNodesInGroup("AuraControllers");

    // The caster's body (parent of this node)
    var casterBody = GetParent<Node3D>(); 
    Vector3 casterPos = casterBody.GlobalPosition;
    Vector3 casterForward = -casterBody.GlobalTransform.Basis.Z; // Forward is -Z in Godot

    foreach (GridNode node in allAuraNodes)
    {
        if (node is AuraController aura)
        {
            var auraParent = aura.GetParent<Node3D>();
            if (auraParent == null) continue;

            Vector3 targetPos = auraParent.GlobalPosition;

            // Basic range check (60ft)
            if (casterPos.DistanceTo(targetPos) > 60f) continue;

            // Cone angle check
            Vector3 toTarget = (targetPos - casterPos).Normalized();
            
            // AngleTo returns radians
            if (Mathf.RadToDeg(casterForward.AngleTo(toTarget)) > 30f) continue; // Cone is 60 degrees total (30 half-angle)

            // Barrier penetration check
            Vector3 origin = casterPos + Vector3.Up * 1f; // Approx eye level
            
            // Raycast for Line of Sight/Effect
            // Assuming "Unwalkable" corresponds to Layer 1 (Walls)
            if (!LineOfSightManager.HasLineOfEffect(casterBody, origin, targetPos))
            {
                // This uses LoS Manager which checks collision mask 1
                continue;
            }
            
            foundAuras.Add(aura);
        }
    }
    return foundAuras;
}
}