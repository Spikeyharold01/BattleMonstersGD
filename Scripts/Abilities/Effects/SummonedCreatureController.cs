using Godot;

// =================================================================================================
// FILE: SummonedCreatureController.cs
// PURPOSE: Tracks summoned creature lifecycle and expires summon duration.
// ATTACH TO: Added dynamically to summoned units.
// =================================================================================================
public partial class SummonedCreatureController : Godot.Node
{
    private int durationRounds;
    private CreatureStats caster;

    public string RestrictionText { get; private set; } = "";
    public bool IsCelestialTemplateApplied { get; private set; }
    public bool IsFiendishTemplateApplied { get; private set; }

    public void Initialize(int duration, CreatureStats sourceCaster, string restrictionText)
    {
        durationRounds = Mathf.Max(1, duration);
        caster = sourceCaster;
        RestrictionText = restrictionText ?? "";
    }

    public void MarkTemplateFlags(bool celestialApplied, bool fiendishApplied)
    {
        IsCelestialTemplateApplied = celestialApplied;
        IsFiendishTemplateApplied = fiendishApplied;
    }

    public override void _Ready()
    {
        AddToGroup("SummonedCreatures");
    }

    public void OnRoundEnd()
    {
        durationRounds--;
        if (durationRounds <= 0)
        {
            Unsummon();
        }
    }

    public void Unsummon()
    {
        GridNode parent = GetParent();
        if (parent == null) return;

        GD.Print($"{parent.Name} disappears (summon duration expired).");
        parent.QueueFree();
    }
}