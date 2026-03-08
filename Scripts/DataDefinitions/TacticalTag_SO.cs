using Godot;

[GlobalClass]
public partial class TacticalTag_SO : Resource
{
    // Enums moved to CombatEnums.cs
    [Export] public TacticalRole Role;
    [Export] public float BaseValue = 30f;
    [Export] public Godot.Collections.Array<CounterStrategy> CounterStrategies = new();
}