using Godot;
using System.Linq;

// =================================================================================================
// FILE: PolymorphController.cs
// PURPOSE: Handles the visual swap and item melding for polymorph effects. Cleans itself up
//          when the associated status effect expires.
// =================================================================================================
public partial class PolymorphController : GridNode
{
    private CreatureStats myStats;
    private CreatureTemplate_SO originalTemplate;
    private Node3D originalVisuals;
    private Node3D currentVisualOverride;
    
    private string trackingEffectName;
    public int NaturalSpellRoundsRemaining { get; private set; } = 0;

    public void Initialize(CreatureTemplate_SO newFormTemplate, CreatureTemplateModifier_SO runtimeModifiers, string effectName, int naturalSpellRounds)
    {
        myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
        trackingEffectName = effectName;
        NaturalSpellRoundsRemaining = naturalSpellRounds;

        // Cache original state
        originalTemplate = myStats.Template;
        originalVisuals = myStats.GetNodeOrNull<Node3D>("Visuals") ?? myStats.GetNodeOrNull<Node3D>("MeshInstance3D");

        // Apply visual swap
        if (newFormTemplate.CharacterPrefab != null)
        {
            if (originalVisuals != null) originalVisuals.Visible = false;
            
            currentVisualOverride = newFormTemplate.CharacterPrefab.Instantiate<Node3D>();
            myStats.AddChild(currentVisualOverride);
            currentVisualOverride.Position = Vector3.Zero;
            
            // Strip logic nodes from the visual dummy
            currentVisualOverride.GetNodeOrNull<CreatureStats>("CreatureStats")?.QueueFree();
            currentVisualOverride.GetNodeOrNull<AIController>("AIController")?.QueueFree();
        }

        // Meld equipment
        if (myStats.MyInventory != null)
        {
            myStats.MyInventory.IsEquipmentMelded = true;
        }

        // Apply physical stat modifications
        myStats.ApplyTemplateModifier(runtimeModifiers);
    }

    public override void _Process(double delta)
    {
        // Monitor the tracking effect. If it's dispelled or expires, we revert.
        if (myStats != null && myStats.MyEffects != null && !myStats.MyEffects.HasEffect(trackingEffectName))
        {
            QueueFree(); // Triggers _ExitTree for cleanup
        }
    }

    public void OnTurnStart()
    {
        if (NaturalSpellRoundsRemaining > 0)
        {
            NaturalSpellRoundsRemaining--;
        }
    }

    public bool IsActionRestricted(Ability_SO ability)
    {
        if (ability == null || ability.Category != AbilityCategory.Spell) return false;

        // "You cannot cast spells with verbal, somatic, material, or focus components while in a non-humanoid form."
        // Natural Spell circumvents this limitation.
        if (NaturalSpellRoundsRemaining > 0) return false;

        bool silent = myStats.HasFeat("Silent Spell");
        bool still = myStats.HasFeat("Still Spell");
        bool eschew = myStats.HasFeat("Eschew Materials");

        if (ability.Components.HasVerbal && !silent) return true;
        if (ability.Components.HasSomatic && !still) return true;
        if (ability.Components.HasFocus) return true;
        if (ability.Components.HasMaterial && !eschew) return true;
        if (ability.Components.HasDivineFocus) return true;

        return false;
    }

    public override void _ExitTree()
    {
        if (myStats != null)
        {
            GD.PrintRich($"[color=cyan]{myStats.Name} reverts to their true form![/color]");
            
            myStats.Template = originalTemplate;
            myStats.ApplyTemplate(); // Re-evaluates base stats

            if (myStats.MyInventory != null)
            {
                myStats.MyInventory.IsEquipmentMelded = false;
            }

            if (currentVisualOverride != null && GodotObject.IsInstanceValid(currentVisualOverride))
            {
                currentVisualOverride.QueueFree();
            }

            if (originalVisuals != null && GodotObject.IsInstanceValid(originalVisuals))
            {
                originalVisuals.Visible = true;
            }
        }
    }
}