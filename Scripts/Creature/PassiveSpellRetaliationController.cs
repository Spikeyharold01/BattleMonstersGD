using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: PassiveSpellRetaliationController.cs
// PURPOSE: A reusable controller that automatically fires an ability back at a caster
//          when targeted by specific types of spells (e.g., Mind-Affecting).
// ATTACH TO: Creature Root Node (The 3D Prefab).
// =================================================================================================
[GlobalClass]
public partial class PassiveSpellRetaliationController : Node
{
    [Export]
    [Tooltip("The Ability_SO to cast back at the attacker (e.g., Madness Wisdom Drain).")]
    public Ability_SO RetaliationAbility;

    [ExportGroup("Trigger Conditions")]
    [Export]
    [Tooltip("If true, triggers whenever targeted by a mind-affecting spell, telepathy, or thought detection.")]
    public bool TriggerOnMindAffecting = true;

    [Export]
    [Tooltip("If true, triggers whenever targeted by alignment detection (Detect Evil, etc).")]
    public bool TriggerOnAlignmentDetection = false;

    private CreatureStats myStats;

    public override void _Ready()
    {
        myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
        if (myStats != null)
        {
            myStats.OnTargetedBySpell += HandleTargetedBySpell;
        }
    }

    public override void _ExitTree()
    {
        if (myStats != null)
        {
            myStats.OnTargetedBySpell -= HandleTargetedBySpell;
        }
    }

    private void HandleTargetedBySpell(CreatureStats caster, Ability_SO spell)
    {
        if (caster == null || spell == null || RetaliationAbility == null || caster == myStats) return;

        bool triggers = false;

        // Condition 1: Mind-Affecting / Telepathy
        if (TriggerOnMindAffecting)
        {
            bool isMindAffecting = (spell.DescriptionForTooltip?.ToLowerInvariant().Contains("mind-affecting") ?? false) || 
                                   spell.School == MagicSchool.Enchantment || 
                                   (spell.School == MagicSchool.Illusion && spell.AbilityName.Contains("Phantasm", System.StringComparison.OrdinalIgnoreCase)) ||
                                   spell.AbilityName.Contains("Detect Thoughts", System.StringComparison.OrdinalIgnoreCase) ||
                                   spell.AbilityName.Contains("Telepath", System.StringComparison.OrdinalIgnoreCase);

            if (isMindAffecting) triggers = true;
        }

        // Condition 2: Alignment Detection
        if (TriggerOnAlignmentDetection)
        {
            bool isAlignmentDetection = spell.AbilityName.Contains("Detect Evil", System.StringComparison.OrdinalIgnoreCase) ||
                                        spell.AbilityName.Contains("Detect Good", System.StringComparison.OrdinalIgnoreCase) ||
                                        spell.AbilityName.Contains("Detect Law", System.StringComparison.OrdinalIgnoreCase) ||
                                        spell.AbilityName.Contains("Detect Chaos", System.StringComparison.OrdinalIgnoreCase);
                                        
            if (isAlignmentDetection) triggers = true;
        }

        // Execute Retaliation
        if (triggers)
        {
            GD.PrintRich($"[color=purple]{myStats.Name} passively retaliates against {caster.Name}'s spell using {RetaliationAbility.AbilityName}![/color]");
            
            // Resolve the retaliation ability instantly as a free/no action.
            _ = CombatManager.ResolveAbility(myStats, caster, caster, caster.GlobalPosition, RetaliationAbility, false);
        }
    }
}