using Godot;
// =================================================================================================
// FILE: ObjectDurability.cs (GODOT VERSION)
// PURPOSE: A component to give environmental objects HP and Hardness for destruction/collision.
// ATTACH TO: Prefabs for walls, trees, boulders, etc. (Node3D).
// =================================================================================================
public enum SubstanceType { Wood, Stone, Steel, Adamantine, Other }
public partial class ObjectDurability : Node3D
{
[Export] public SubstanceType Material;
[Export]
[Tooltip("The thickness of the object in inches. Used to calculate HP.")]
public float ThicknessInInches = 12f;
[ExportGroup("Destruction Effects")]
[Export]
[Tooltip("Is this object flammable?")]
public bool IsFlammable = false;
[Export]
[Tooltip("The loot table to use when this object is destroyed by non-fire means.")]
public LootTable_SO DebrisLootTable;

public int Hardness { get; private set; }
public int MaxHP { get; private set; }
public int CurrentHP { get; private set; }
public void Initialize(int maxHP, int hardness)
  {
        MaxHP = maxHP;
        CurrentHP = MaxHP;
        Hardness = hardness;
    }

public override void _Ready()
{
    CalculateDurability();
    CurrentHP = MaxHP;
    AddToGroup("ObjectDurability"); // Register for AI finding
}

private void CalculateDurability()
{
    switch (Material)
    {
        case SubstanceType.Wood:
            Hardness = 5;
            MaxHP = Mathf.RoundToInt(10 * ThicknessInInches);
            break;
        case SubstanceType.Stone:
            Hardness = 8;
            MaxHP = Mathf.RoundToInt(15 * ThicknessInInches);
            break;
        case SubstanceType.Steel:
            Hardness = 10;
            MaxHP = Mathf.RoundToInt(30 * ThicknessInInches);
            break;
        case SubstanceType.Adamantine:
            Hardness = 20;
            MaxHP = Mathf.RoundToInt(40 * ThicknessInInches);
            break;
        case SubstanceType.Other:
        default:
            Hardness = 5; // Generic default
            MaxHP = 100;
            break;
    }
}

  public void TakeDamage(int damage, string damageType)
{
    // Handle vulnerabilities and special damage interactions
    int effectiveHardness = Hardness;
    int effectiveDamage = damage;

    if (IsFlammable && damageType == "Fire")
    {
        // Flammable objects often have low hardness vs. fire
        effectiveHardness = 0; 
        // Fire damage might be amplified
        effectiveDamage *= 2;
    }

    int damageToTake = Mathf.Max(0, effectiveDamage - effectiveHardness);
    if (damageToTake > 0)
    {
        CurrentHP -= damageToTake;
        GD.Print($"{Name} takes {damageToTake} {damageType} damage. ({CurrentHP}/{MaxHP} HP remaining)");
        if (CurrentHP <= 0)
        {
            DestroyObject(damageType);
        }
    }
    
    // If hit by fire and flammable, start burning
    // Note: Adding component dynamically in Godot means adding a Child Node
    if (IsFlammable && damageType == "Fire" && GetNodeOrNull<BurningObjectController>("BurningObjectController") == null)
    {
        var burningComp = new BurningObjectController();
        burningComp.Name = "BurningObjectController";
        AddChild(burningComp);
    }
}

private void DestroyObject(string finalDamageType)
{
    // Don't drop debris if destroyed by fire (it burned up)
    if (finalDamageType != "Fire" && DebrisLootTable != null)
    {
        // LootTable_SO.SpawnLoot takes a Parent node. We spawn in the scene root usually.
        DebrisLootTable.SpawnLoot(GetTree().CurrentScene, GlobalPosition);
    }
    
    GD.Print($"{Name} has been destroyed!");
    QueueFree();
}
}