using Godot;
// =================================================================================================
// FILE: TelekinesisController.cs (GODOT VERSION)
// PURPOSE: Manages the state of a sustained Telekinesis spell.
// ATTACH TO: A creature at runtime when they cast the sustained version of Telekinesis (Child Node).
// =================================================================================================
public partial class TelekinesisController : GridNode
{
public int DurationRounds { get; private set; }
public bool HasConcentratedThisTurn { get; set; } = true; // True for the turn it's cast

public void Initialize(CreatureStats caster)
{
    DurationRounds = caster.Template.CasterLevel;
    GD.Print($"{caster.Name} begins concentrating on Telekinesis for up to {DurationRounds} rounds.");
}

// Called by ActionManager.OnTurnStart
public void OnTurnStart()
{
    // At the start of the caster's next turn, check if they concentrated.
    if (!HasConcentratedThisTurn)
    {
        GD.Print($"{GetParent().Name} failed to concentrate. Telekinesis ends.");
        QueueFree();
        return;
    }
    
    DurationRounds--;
    if (DurationRounds <= 0)
    {
        GD.Print($"Telekinesis duration has expired for {GetParent().Name}.");
        QueueFree();
    }
    
    // Reset the flag for the new turn. The "Concentrate" action will set it back to true.
    HasConcentratedThisTurn = false;
}

public void RefreshConcentration()
{
    HasConcentratedThisTurn = true;
    GD.Print($"{GetParent().Name} successfully maintains concentration on Telekinesis.");
}
}