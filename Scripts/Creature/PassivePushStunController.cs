using Godot;
using System.Linq;

// =================================================================================================
// FILE: PassivePushStunController.cs
// PURPOSE: Generic passive that forces a save vs Status Effect whenever the owner pushes a target.
// ATTACH TO: Creatures with "Stun on Push" abilities (Child Node).
// =================================================================================================
public partial class PassivePushStunController : GridNode
{
    [Export] public StatusEffect_SO EffectToApply; // e.g. Stunned
    [Export] public int BaseDC = 25;
    [Export] public AbilityScore DynamicDCStat = AbilityScore.Constitution;
    [Export] public int DurationRounds = 1;

    private CreatureStats owner;

    public override void _Ready()
    {
        owner = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
        
        // We need to listen to GLOBAL forced movement events, or hook into every creature.
        // Since we can't easily hook every creature individually without a global manager,
        // we will rely on a new static event in CreatureStats, similar to OnAnyCreatureDamaged.
        CreatureStats.OnAnyCreatureForcedMoved += HandleForcedMovement;
    }

    public override void _ExitTree()
    {
        CreatureStats.OnAnyCreatureForcedMoved -= HandleForcedMovement;
    }

    private void HandleForcedMovement(CreatureStats victim, CreatureStats pusher)
    {
        if (owner == null || pusher != owner) return;
        if (victim == owner) return;

        // Calculate DC
        int dc = BaseDC;
        // Logic: 10 + 1/2 HD + Stat
        // Note: BaseDC in inspector usually assumes a fixed CR value for simple monsters.
        // For dynamic scaling:
        int hd = 1;
        if (!string.IsNullOrEmpty(owner.Template.HitDice))
            int.TryParse(owner.Template.HitDice.Split('d')[0], out hd);
            
        int statMod = 0;
        switch(DynamicDCStat)
        {
            case AbilityScore.Constitution: statMod = owner.ConModifier; break;
            case AbilityScore.Strength: statMod = owner.StrModifier; break;
            // Add others if needed
        }
        
        // Recalculate DC dynamically if needed, or stick to BaseDC if it's simpler for data entry.
        // Pathfinder formula: 10 + 1/2 HD + Con Mod.
        // If BaseDC is set to 10 in inspector, we add the rest.
        // If BaseDC is set to 25 (final value), we assume it's pre-calculated.
        // Let's use the dynamic calculation to be robust.
        int calculatedDC = 10 + (hd / 2) + statMod;
        
        // Roll Save
        int save = Dice.Roll(1, 20) + victim.GetFortitudeSave(owner);
        GD.Print($"{owner.Name} pushed {victim.Name}, triggering Stun (Ex). Fort Save: {save} vs DC {calculatedDC}.");

        if (save < calculatedDC)
        {
            GD.PrintRich($"[color=red]{victim.Name} is Stunned![/color]");
            if (EffectToApply != null)
            {
                var instance = (StatusEffect_SO)EffectToApply.Duplicate();
                instance.DurationInRounds = DurationRounds;
                victim.MyEffects.AddEffect(instance, owner);
            }
        }
    }
}