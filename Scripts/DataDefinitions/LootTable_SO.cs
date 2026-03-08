using Godot;

[GlobalClass]
public partial class LootDrop : Resource
{
    [Export] public PackedScene ItemPrefab; // Replaces GameObject
    
    [Export(PropertyHint.Range, "0,1")]
    public float DropChance = 0.5f;
    
    [Export] public int MinQuantity = 1;
    [Export] public int MaxQuantity = 1;
}

[GlobalClass]
public partial class LootTable_SO : Resource
{
    [Export] public Godot.Collections.Array<LootDrop> LootDrops = new();

    // In Godot, you must provide the 'parent' node to add the spawned items to the scene tree.
    public void SpawnLoot(GridNode parent, Vector3 position)
    {
        foreach (var drop in LootDrops)
        {
            // GD.Randf() returns 0.0 to 1.0
            if (GD.Randf() <= drop.DropChance)
            {
                int quantity = GD.RandRange(drop.MinQuantity, drop.MaxQuantity);
                for (int i = 0; i < quantity; i++)
                {
                    if (drop.ItemPrefab == null) continue;

                    // Random point in circle logic for Godot (XZ plane)
                    float angle = GD.Randf() * Mathf.Tau;
                    float dist = Mathf.Sqrt(GD.Randf()) * 1.5f; // 1.5f radius
                    Vector3 offset = new Vector3(Mathf.Cos(angle) * dist, 0, Mathf.Sin(angle) * dist);
                    Vector3 spawnPos = position + offset;

                    Node3D newItem = drop.ItemPrefab.Instantiate<Node3D>();
                    parent.AddChild(newItem);
                    newItem.GlobalPosition = spawnPos;
                }
            }
        }
    }
}