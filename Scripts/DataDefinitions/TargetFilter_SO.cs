using Godot;
using System.Linq;

[GlobalClass]
public partial class TargetFilter_SO : Resource
{
    public enum HealthState { Any, FullHealth, Wounded, Dying }
    public enum Relationship { Any, Ally, Enemy }

    [ExportGroup("Alignment & Origin")]
    [Export] public string RequiredAlignment;
    [Export] public bool MustBeSummoned = false;
    
    [Export] public Godot.Collections.Array<CreatureType> RequiredTypes = new();
    [Export] public Godot.Collections.Array<CreatureType> ForbiddenTypes = new();
	
	[Export] public bool MustBreathe = false;
	[Export] public bool MustHear = false;
	
	 [Export] public string RequiredSpecialRule = "";
    
    [Export] public HealthState RequiredHealthState = HealthState.Any;
    [Export] public Relationship RequiredRelationship = Relationship.Any;

    /// <summary>
    /// Checks if a given target meets all the conditions of this filter.
    /// </summary>
    public bool IsTargetValid(CreatureStats caster, CreatureStats target)
    {
        if (target == null || caster == null) return false;

        // Check relationship using Groups (Godot's equivalent to Tags)
        // Assuming Player units are in group "Player" and Enemies are in group "Enemy"
        switch (RequiredRelationship)
        {
            case Relationship.Ally:
                // If one is Player and the other isn't, they are not allies
                if (caster.IsInGroup("Player") != target.IsInGroup("Player")) return false;
                break;
            case Relationship.Enemy:
                // If one is Player and the other is also Player, they are not enemies
                if (caster.IsInGroup("Player") == target.IsInGroup("Player")) return false;
                break;
        }

        // Check alignment
        if (!string.IsNullOrEmpty(RequiredAlignment))
        {
            if (string.IsNullOrEmpty(target.Template.Alignment) || !target.Template.Alignment.Contains(RequiredAlignment))
                return false;
        }

        // Check if summoned
        if (MustBeSummoned && !target.IsSummoned)
        {
            return false;
        }
		
        // Check breathing requirement
        if (MustBreathe && target.HasSpecialRule("No Breath"))
        {
            return false;
        }
		
		// Check hearing requirement
         if (MustHear && (SoundSystem.IsDeafened(target) || (target.MyEffects != null && target.MyEffects.HasCondition(Condition.Silenced))))
        {
            return false;
        }
		
		 // Check special rule requirement (e.g., Salt Water Vulnerability)
        if (!string.IsNullOrWhiteSpace(RequiredSpecialRule) && !target.HasSpecialRule(RequiredSpecialRule))
        {
            return false;
        }
		
		
        // Check required types
        if (RequiredTypes != null && RequiredTypes.Count > 0 && !RequiredTypes.Contains(target.Template.Type))
        {
            return false;
        }

        // Check forbidden types
        if (ForbiddenTypes != null && ForbiddenTypes.Count > 0 && ForbiddenTypes.Contains(target.Template.Type))
        {
            return false;
        }

        // Check health state
        switch (RequiredHealthState)
        {
            case HealthState.FullHealth:
                if (target.CurrentHP < target.Template.MaxHP) return false;
                break;
            case HealthState.Wounded:
                if (target.CurrentHP >= target.Template.MaxHP) return false;
                break;
            case HealthState.Dying:
                if (target.CurrentHP >= 0) return false;
                break;
        }

        return true;
    }
}