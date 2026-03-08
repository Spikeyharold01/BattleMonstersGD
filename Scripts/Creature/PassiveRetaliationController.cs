using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: PassiveRetaliationController.cs
// PURPOSE: A highly reusable controller that automatically counter-attacks when the creature
//          takes damage from specific types of attacks (e.g. Poisonous flesh, Acid blood, Fire shield).
// ATTACH TO: Creature Root Node.
// =================================================================================================
[GlobalClass]
public partial class PassiveRetaliationController : GridNode
{
    [Export]
    [Tooltip("The Ability to trigger against the attacker (e.g., Ala Poison, Acid Splash).")]
    public Ability_SO RetaliationAbility;

    [ExportGroup("Trigger Conditions")]
    [Export]
    [Tooltip("If true, triggers when hit by a natural attack (Bite, Claw, Slam, etc.).")]
    public bool TriggerOnNaturalAttacks = true;

    [Export]
    [Tooltip("If populated, ONLY triggers on natural attacks containing this keyword (e.g. 'bite'). Leave empty to trigger on ANY natural attack.")]
    public string SpecificNaturalAttackKeyword = "bite";

    [Export]
    [Tooltip("If true, triggers when hit by a manufactured melee weapon (e.g. Swords, Maces).")]
    public bool TriggerOnManufacturedMeleeWeapons = false;

    private CreatureStats myStats;

    public override void _Ready()
    {
        myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
        if (myStats != null)
        {
            myStats.OnTakeDamageDetailed += OnDamageTaken;
        }
    }

    public override void _ExitTree()
    {
        if (myStats != null)
        {
            myStats.OnTakeDamageDetailed -= OnDamageTaken;
        }
    }

    private void OnDamageTaken(int damage, string damageType, CreatureStats attacker, Item_SO weapon, NaturalAttack naturalAttack)
    {
        if (attacker == null || RetaliationAbility == null) return;

        bool triggers = false;

        // Check Natural Attacks
        if (TriggerOnNaturalAttacks && naturalAttack != null)
        {
            if (string.IsNullOrWhiteSpace(SpecificNaturalAttackKeyword))
            {
                triggers = true;
            }
            else if (naturalAttack.AttackName.Contains(SpecificNaturalAttackKeyword, System.StringComparison.OrdinalIgnoreCase))
            {
                triggers = true;
            }
        }

        // Check Manufactured Weapons
        if (TriggerOnManufacturedMeleeWeapons && weapon != null && weapon.WeaponType == WeaponType.Melee)
        {
            // Optional: You could add a check here for weapon.WeaponReach to ensure they don't get splashed from 10ft away using a polearm.
            triggers = true;
        }

        if (triggers)
        {
            GD.PrintRich($"[color=purple]{myStats.Name} retaliates against {attacker.Name}'s attack![/color]");
            
            // Execute the ability directly against the attacker.
            _ = CombatManager.ResolveAbility(myStats, attacker, attacker, attacker.GlobalPosition, RetaliationAbility, false);
        }
    }
}