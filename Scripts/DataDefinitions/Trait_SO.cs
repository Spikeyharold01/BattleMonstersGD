using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class Trait_SO : Resource
{
    [Export] public string TraitName;
    [Export(PropertyHint.MultilineText)] public string Description;

    // Uses the global ImmunityType enum
    [Export] public Godot.Collections.Array<ImmunityType> Immunities = new();
	
	 [Export] public Godot.Collections.Array<DamageConversion> Conversions = new();
    
    [Export] public Ability_SO AssociatedPassiveAbility;
}