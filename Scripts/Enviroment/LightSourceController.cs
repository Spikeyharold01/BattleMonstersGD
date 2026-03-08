using Godot;
// =================================================================================================
// FILE: LightSourceController.cs (GODOT VERSION)
// PURPOSE: A component that turns any object into a mobile source of magical light or darkness.
// ATTACH TO: Creatures or items that are targeted by a light/darkness spell (Child Node).
// =================================================================================================
public struct LightSourceInfo
{
public int SourceID;
public LightAndDarknessInfo Data;
public int SpellLevel;
public Vector3 WorldPosition;
public CreatureStats Caster;
public bool IsMythic;
}
public partial class LightSourceController : Node3D
{
public LightSourceInfo Info { get; private set; }
private float duration;
private static int nextSourceID = 1;

// Track position change manually since Node3D doesn't have 'hasChanged' flag
private Vector3 lastPosition;

 public void Initialize(LightAndDarknessInfo lightData, int spellLevel, CreatureStats caster, bool isMythic)
{
	AddToGroup("LightSources");
    // Pathfinder duration: 10 min/level = 100 rounds/level. 1 round = 6 seconds.
    duration = lightData.DurationInMinutes * caster.Template.CasterLevel * 100 * 6; 

    LightSourceInfo info = new LightSourceInfo
    {
        SourceID = nextSourceID++,
        Data = lightData,
        SpellLevel = spellLevel,
        WorldPosition = GlobalPosition,
        Caster = caster,
        IsMythic = isMythic // Store the flag
    };
    this.Info = info;
    lastPosition = GlobalPosition;
    
    GridManager.Instance?.AddLightSource(this);
}

public override void _Process(double delta)
{
    duration -= (float)delta;
    if (duration <= 0)
    {
        QueueFree();
        return;
    }

    // Check for position change (sqr magnitude for efficiency)
    if (GlobalPosition.DistanceSquaredTo(lastPosition) > 0.01f)
    {
        LightSourceInfo updatedInfo = Info;
        updatedInfo.WorldPosition = GlobalPosition;
        this.Info = updatedInfo;
        GridManager.Instance?.UpdateLightSourcePosition(this);
        lastPosition = GlobalPosition;
    }
}

public override void _ExitTree()
{
    GridManager.Instance?.RemoveLightSource(this);
    GD.Print("A magical light/darkness effect has ended.");
}
}