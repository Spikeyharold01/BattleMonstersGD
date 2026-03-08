using Godot;
using System.Linq;
// =================================================================================================
// FILE: PassiveAbilityController.cs (GODOT VERSION)
// PURPOSE: Manages passive special abilities that react to game state changes.
// ATTACH TO: All creature scenes (as a child node of the CreatureStats root).
// =================================================================================================
public partial class PassiveAbilityController : Godot.Node
{
private CreatureStats myStats;
private StatusEffect_SO sickenedEffect;
private StatusEffect_SO confusionEffect;

// Flags for abilities this creature possesses to avoid searching the list every frame.
private bool hasSunlightDependency = false;

public override void _Ready()
{
    myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");

    if (myStats != null)
    {
        // Subscribe to events
        myStats.OnTargetedBySpell += HandleTorturedMind;

        // Pre-cache abilities and effects for performance.
        if (myStats.Template != null && myStats.Template.KnownAbilities != null)
        {
            hasSunlightDependency = myStats.Template.KnownAbilities.Any(a => a.AbilityName == "Sunlight Dependency");
        }
    }

    // Load specific status effects used by these passives.
    // Assuming standard path structure used in previous scripts.
    if (ResourceLoader.Exists("res://Data/StatusEffects/Sickened_Effect.tres"))
        sickenedEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Sickened_Effect.tres");
    else
        GD.PrintErr("PassiveAbilityController: Could not find 'res://Data/StatusEffects/Sickened_Effect.tres'");

    if (ResourceLoader.Exists("res://Data/StatusEffects/Confusion_Effect.tres"))
        confusionEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Confusion_Effect.tres");
}

public override void _ExitTree()
{
    if (myStats != null)
    {
        myStats.OnTargetedBySpell -= HandleTorturedMind;
    }
}

// Called by the engine every frame. Replaces Unity's Update().
public override void _Process(double delta)
{
    if (hasSunlightDependency)
    {
        CheckSunlightDependency();
    }
}

private void HandleTorturedMind(CreatureStats caster, Ability_SO spell)
{
    // 1. Check if I have the ability
    if (myStats.Template == null || myStats.Template.KnownAbilities == null) return;
    var ability = myStats.Template.KnownAbilities.FirstOrDefault(a => a.AbilityName.Equals("Tortured Mind", System.StringComparison.OrdinalIgnoreCase));
    if (ability == null) return;

    // 2. Check if the spell is Mind-Affecting
    bool isMindAffecting = (spell.DescriptionForTooltip?.ToLower().Contains("mind-affecting") ?? false) || 
                           spell.School == MagicSchool.Enchantment || 
                           (spell.School == MagicSchool.Illusion && spell.AbilityName.Contains("Phantasm"));

    if (!isMindAffecting) return;

    GD.PrintRich($"[color=magenta]Tortured Mind Triggered![/color] {caster.Name} tried to use {spell.AbilityName} on {myStats.Name}.");

    // 3. Resolve Retaliation (Will Save vs Confusion)
    // DC = 10 + 1/2 HD + Con + 4 (Racial)
    int hd = 1;
    if (!string.IsNullOrEmpty(myStats.Template.HitDice))
    {
        string[] parts = myStats.Template.HitDice.Split('d');
        if (parts.Length > 0)
            int.TryParse(parts[0], out hd);
    }
    
    int dc = 10 + (hd / 2) + myStats.ConModifier + 4;

    int save = Dice.Roll(1, 20) + caster.GetWillSave(myStats);
    
    if (save < dc)
    {
        GD.PrintRich($"[color=red]{caster.Name} fails Will save (Roll: {save} vs DC {dc}) and becomes Confused![/color]");
        
        // Apply Confusion
        if (confusionEffect != null)
        {
            var instance = (StatusEffect_SO)confusionEffect.Duplicate();
            instance.DurationInRounds = 10; // 1 minute
            caster.GetNode<StatusEffectController>("StatusEffectController").AddEffect(instance, myStats);
        }
        else
        {
            GD.PrintErr("PassiveAbilityController: Confusion effect resource not loaded.");
        }
    }
    else
    {
        GD.Print($"{caster.Name} resists the Tortured Mind backlash.");
    }
}

private void CheckSunlightDependency()
{
    if (sickenedEffect == null) return;

    // Get world position from the parent body (CharacterBody3D)
    var body = GetParent<Node3D>();
    GridNode currentNode = GridManager.Instance.NodeFromWorldPoint(body.GlobalPosition);
    
    // "Darkness" is defined as a light level of 0.
    bool isInDarkness = GridManager.Instance.GetEffectiveLightLevel(currentNode) == 0;
    bool isCurrentlySickened = myStats.MyEffects.HasEffect("Sunlight Dependency Sickness");

    if (isInDarkness && !isCurrentlySickened)
    {
        // Apply the sickened condition.
        GD.PrintRich($"[color=orange]{myStats.Name} is in darkness and becomes sickened due to Sunlight Dependency.[/color]");
        var effectInstance = (StatusEffect_SO)sickenedEffect.Duplicate();
        effectInstance.EffectName = "Sunlight Dependency Sickness"; // Give it a unique name to manage it
        
        var sourceAbility = myStats.Template.KnownAbilities.FirstOrDefault(a => a.AbilityName == "Sunlight Dependency");
        myStats.MyEffects.AddEffect(effectInstance, myStats, sourceAbility);
    }
    else if (!isInDarkness && isCurrentlySickened)
    {
        // Remove the sickened condition.
        GD.Print($"{myStats.Name} is no longer in darkness and recovers from Sunlight Dependency sickness.");
        myStats.MyEffects.RemoveEffect("Sunlight Dependency Sickness");
    }
}
}