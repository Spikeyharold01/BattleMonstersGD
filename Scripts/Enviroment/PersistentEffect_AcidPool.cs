using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// =================================================================================================
// FILE: PersistentEffect_AcidPool.cs (GODOT VERSION)
// PURPOSE: Manages the unique damage and fume effects of an acid pool hazard.
// ATTACH TO: Prefabs representing pools or vats of acid (Node3D).
// =================================================================================================
public partial class PersistentEffect_AcidPool : Node3D
{
[Export]
[Tooltip("The radius of the acid pool in feet.")]
public float PoolRadius = 10f;
[Export]
[Tooltip("The depth of the acid at the pool's absolute center, in feet.")]
public float MaxDepth = 10f;

private HashSet<CreatureStats> creaturesInPool = new HashSet<CreatureStats>();
private HashSet<CreatureStats> creaturesInFumes = new HashSet<CreatureStats>();

private Ability_SO acidFumesAbility; 

public override void _Ready()
{
    // For visual representation
    Scale = new Vector3(PoolRadius * 2, 1, PoolRadius * 2);
    
    // Add to group for AI finding
    AddToGroup("AcidPool");
    
    // Load the fume ability asset
    if (ResourceLoader.Exists("res://Data/Abilities/Effects/Env_AcidFumes.tres"))
        acidFumesAbility = GD.Load<Ability_SO>("res://Data/Abilities/Effects/Env_AcidFumes.tres");
}

public override void _Process(double delta)
{
    UpdateAffectedCreatures();
}

private void UpdateAffectedCreatures()
{
    var allCombatants = TurnManager.Instance.GetAllCombatants();
    if (allCombatants == null) return;

    var currentCreaturesInPool = new HashSet<CreatureStats>();
    var currentCreaturesInFumes = new HashSet<CreatureStats>();

    foreach (var creature in allCombatants)
    {
        // Use only XZ plane for distance
        Vector3 creaturePos2D = new Vector3(creature.GlobalPosition.X, GlobalPosition.Y, creature.GlobalPosition.Z);
        float distance = GlobalPosition.DistanceTo(creaturePos2D);
        
        // Check for immersion (within the pool's radius and at the same height)
        if (distance <= PoolRadius && Mathf.Abs(creature.GlobalPosition.Y - GlobalPosition.Y) < 2f)
        {
            currentCreaturesInPool.Add(creature);
        }
        // Check for adjacency (within 5 feet of the edge of the pool)
        else if (distance <= PoolRadius + 5f)
        {
            currentCreaturesInFumes.Add(creature);
        }
    }

    // --- Process Pool Effects ---
    foreach (var creature in currentCreaturesInPool.Except(creaturesInPool))
    {
        OnCreatureEnterPool(creature);
    }
    foreach (var creature in creaturesInPool.Except(currentCreaturesInPool))
    {
        OnCreatureExitPool(creature);
    }
    creaturesInPool = currentCreaturesInPool;

    // --- Process Fume Effects ---
    foreach (var creature in currentCreaturesInFumes.Except(creaturesInFumes))
    {
        OnCreatureEnterFumes(creature);
    }
    foreach (var creature in creaturesInFumes.Except(currentCreaturesInFumes))
    {
        OnCreatureExitFumes(creature);
    }
    creaturesInFumes = currentCreaturesInFumes;
}

private void OnCreatureEnterPool(CreatureStats creature)
{
    GD.PrintRich($"[color=green]{creature.Name} enters the acid pool![/color]");
}

private void OnCreatureExitPool(CreatureStats creature)
{
    GD.Print($"{creature.Name} leaves the acid pool.");
}

/// <summary>
/// Called by the TurnManager at the start of a creature's turn to apply acid damage.
/// Should be manually called by TurnManager logic if not automated.
/// </summary>
public void ApplyTurnStartEffects(CreatureStats creature)
{
    if (!creaturesInPool.Contains(creature)) return;

    float distanceFromCenter = GlobalPosition.DistanceTo(creature.GlobalPosition);
     float depthAtPosition = Mathf.Lerp(MaxDepth, 0, distanceFromCenter / PoolRadius);
    
    bool isTotallyImmersed = depthAtPosition >= creature.Template.VerticalReach || creature.MyEffects.HasCondition(Condition.Prone);
    
    if (creature.MyEffects.HasCondition(Condition.WaterWalking) && !creature.MyEffects.HasCondition(Condition.Prone))
    {
        isTotallyImmersed = false; // They float on top of the acid
        depthAtPosition = 0f; // Only their feet touch
    }

    int damageDice = isTotallyImmersed ? 10 : 1;
    int damageRoll = Dice.Roll(damageDice, 6);

    GD.Print($"{creature.Name} is {(isTotallyImmersed ? "totally immersed" : "partially exposed")} in acid (Depth: {depthAtPosition:F1}ft). Taking {damageDice}d6 damage.");

    // Use callback for AI learning
    creature.TakeDamage(damageRoll, "Acid", null, null, null, (finalDamage) => {
        if (finalDamage == 0 && damageRoll > 0)
        {
            CombatMemory.RecordWeaponEffectivenessFeedback(creature, GetAcidPseudoWeapon(), 0, damageRoll);
            CombatMemory.RecordIdentifiedTrait(creature, "Immunity:Acid");
        }
    });
}

private Item_SO GetAcidPseudoWeapon()
{
    var pseudoWeapon = new Item_SO();
    pseudoWeapon.DamageInfo.Add(new DamageInfo { DamageType = "Acid" });
    return pseudoWeapon;
}

private void OnCreatureEnterFumes(CreatureStats creature)
{
    if (acidFumesAbility != null)
    {
        GD.PrintRich($"[color=olive]{creature.Name} enters the acid fumes.</color>");
        // Fire and forget ability resolution
        _ = CombatManager.ResolveAbility(null, creature, creature, creature.GlobalPosition, acidFumesAbility, false);
    }
}

private void OnCreatureExitFumes(CreatureStats creature)
{
    // Placeholder
}
}