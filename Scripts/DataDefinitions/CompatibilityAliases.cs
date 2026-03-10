using Godot;

public partial class CreatureTemplate_SO
{
    public string Name => CreatureName;
    public float SpeedSwim => Speed_Swim;
    public global::MovementType MovementType
    {
        get
        {
            if (Speed_Fly > 0) return global::MovementType.Flying;
            if (Speed_Swim > 0) return global::MovementType.Swimming;
            if (Speed_Burrow > 0) return global::MovementType.Burrowing;
            return global::MovementType.Ground;
        }
    }

    public PackedScene CreatureScene => CharacterPrefab;
}

public partial class Ability_SO
{
    [Export] public TacticalTag_SO AiTacticalTag;
    [Export] public int CasterLevel;
}

public partial class TacticalTag_SO
{
    [Export] public string AbilityName = string.Empty;
}
