using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: AuraController.cs (GODOT VERSION)
// PURPOSE: A component that marks a Node as having a magical aura for detection purposes.
// ATTACH TO: Magic item prefabs, or added at runtime to creatures under spell effects.
// =================================================================================================


// Note: MagicSchool enum is likely in CombatEnums.cs already. If not, uncomment below.
// public enum MagicSchool { Abjuration, Conjuration, Divination, Enchantment, Evocation, Illusion, Necromancy, Transmutation, Universal }

public class MagicAura
{
    public Ability_SO SourceAbility { get; set; } // Reference the Ability_SO itself
    public string SourceName; // e.g., "Bull's Strength spell" or "Helm of Brilliance"
    public int CasterLevel;
    public int SpellLevel; // 0 for non-spell effects
    public MagicSchool School;
    
    public AuraStrength Strength
    {
        get
        {
            int level = (SpellLevel > 0) ? SpellLevel : CasterLevel;
            if (SpellLevel > 0)
            {
                if (level <= 3) return AuraStrength.Faint;
                if (level <= 6) return AuraStrength.Moderate;
                if (level <= 9) return AuraStrength.Strong;
                return AuraStrength.Overwhelming;
            }
            else // It's an item
            {
                if (level <= 5) return AuraStrength.Faint;
                if (level <= 11) return AuraStrength.Moderate;
                if (level <= 20) return AuraStrength.Strong;
                return AuraStrength.Overwhelming;
            }
        }
    }
}

public partial class AuraController : Node
{
    public List<MagicAura> Auras = new List<MagicAura>();
	public override void _Ready()
{
    AddToGroup("AuraControllers");
}

    public override void _PhysicsProcess(double delta)
    {
        // This is where we will check for continuous effects like freezing water.
        var parentNode = GetParent() as Node3D;
        if (parentNode == null) return;

        foreach (var aura in Auras)
        {
            // Check if this specific aura has the freeze water effect component.
            // In Godot, EffectComponents are Resources. We check the type.
            if (aura.SourceAbility != null && aura.SourceAbility.EffectComponents.OfType<Effect_AuraFreezeWater>().Any())
            {
                // If it does, call the GridManager to freeze water within the aura's radius.
                float radius = aura.SourceAbility.AreaOfEffect.Range;
                GridManager.Instance?.FreezeWaterInArea(parentNode.GlobalPosition, radius);
            }
        }
    }

    public AuraStrength GetMostPotentAuraStrength()
    {
        var myStats = GetParent().GetNodeOrNull<CreatureStats>("CreatureStats");
        // Or GetParent() as CreatureStats if attached directly
        if (myStats == null) myStats = GetParent() as CreatureStats;

        if (myStats != null && myStats.Template.MythicRank > 0)
        {
            // A mythic creature always radiates an overwhelming aura, regardless of items/spells.
            return AuraStrength.Overwhelming;
        }
        
        if (Auras == null || !Auras.Any()) return AuraStrength.Dim;
        return Auras.Max(a => a.Strength);
    }
}