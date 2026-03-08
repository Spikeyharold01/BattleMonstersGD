using Godot;

[GlobalClass]
public partial class SkillActionDatabase : Resource
{
    [Export] public Godot.Collections.Array<Ability_SO> AllSkillActions = new();
}