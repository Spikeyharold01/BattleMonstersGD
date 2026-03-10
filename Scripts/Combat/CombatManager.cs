using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

public static class CombatManager
{
    #region Attacks (Delegated to CombatAttacks)
    
    public static void ResolveMeleeAttack(CreatureStats attacker, CreatureStats defender, NaturalAttack intendedNaturalAttack = null, Item_SO intendedWeapon = null)
    {
        CombatAttacks.ResolveMeleeAttack(attacker, defender, intendedNaturalAttack, intendedWeapon);
    }

    public static void ResolveFullAttack(CreatureStats attacker, CreatureStats defender)
    {
        CombatAttacks.ResolveFullAttack(attacker, defender);
    }

    public static void ResolveRangedAttack(CreatureStats attacker, CreatureStats defender, Item_SO weapon)
    {
        CombatAttacks.ResolveRangedAttack(attacker, defender, weapon);
    }
    
    public static void ResolveChargeAttack(CreatureStats attacker, CreatureStats defender)
    {
        CombatAttacks.ResolveChargeAttack(attacker, defender);
    }

    /// <summary>
    /// Checks if an attacker can target a defender protected by Sanctuary.
    /// Resolves the Will save if not yet attempted.
    /// Returns TRUE if the attack can proceed, FALSE if blocked.
    /// </summary>
    public static bool CheckSanctuary(CreatureStats attacker, CreatureStats defender)
    {
        if (!defender.MyEffects.HasCondition(Condition.Sanctuary)) return true;

        // 1. Check Memory
        bool? previousResult = CombatMemory.GetSanctuaryResult(attacker, defender);
        if (previousResult.HasValue)
        {
            if (previousResult.Value == false)
            {
                GD.Print($"{attacker.Name} cannot attack {defender.Name} (Previously failed Sanctuary save).");
                return false;
            }
            return true; // Previously saved, can attack normally
        }

        // 2. Resolve New Save
        var effectController = defender.GetNodeOrNull<StatusEffectController>("StatusEffectController");
        int dc = effectController != null ? effectController.GetSanctuaryDC() : 10;
        
        GD.Print($"{attacker.Name} attempts to attack {defender.Name} but must break Sanctuary (DC {dc}).");

        int willSave = RollManager.Instance.MakeD20Roll(attacker) + attacker.GetWillSave(defender);
        
        if (willSave >= dc)
        {
            GD.PrintRich($"[color=green]Success![/color] {attacker.Name} breaks through the Sanctuary (Roll: {willSave}).");
            CombatMemory.RecordSanctuaryResult(attacker, defender, true);
            return true;
        }
        else
        {
            GD.PrintRich($"[color=red]Failure.[/color] {attacker.Name} cannot bring themselves to attack {defender.Name}.");
            CombatMemory.RecordSanctuaryResult(attacker, defender, false);
            return false;
        }
    }
    #endregion

    #region Magic (Delegated to CombatMagic)

    // Changed to async Task for Godot compatibility
    public static async Task ResolveAbility(CreatureStats caster, CreatureStats primaryTarget, Node3D targetObject, Vector3 aimPoint, Ability_SO ability, bool isMythicCast, CommandWord command = CommandWord.None)
    {
        await CombatMagic.ResolveAbility(caster, primaryTarget, targetObject, aimPoint, ability, isMythicCast, command);
    }

    public static bool CheckConcentration(CreatureStats caster, int dc)
    {
        return CombatMagic.CheckConcentration(caster, dc);
    }

    public static bool ResolveAbilityAttack(CreatureStats attacker, CreatureStats defender, Ability_SO ability)
    {
        return CombatMagic.ResolveAbilityAttack(attacker, defender, ability);
    }

    public static void ResolveIllusionDisbelief(IllusionController illusion, CreatureStats interactor)
    {
        CombatMagic.ResolveIllusionDisbelief(illusion, interactor);
    }

    #endregion

    #region Maneuvers (Delegated to CombatManeuvers)

    public static void ResolveTrip(CreatureStats attacker, CreatureStats defender)
    {
        CombatManeuvers.ResolveTrip(attacker, defender);
    }

    public static void ResolveBullRush(CreatureStats attacker, CreatureStats defender)
    {
        _ = CombatManeuvers.ResolveBullRushCoroutine(attacker, defender);
    }

    public static void ResolveAwesomeBlow(CreatureStats attacker, CreatureStats defender)
    {
        CombatManeuvers.ResolveAwesomeBlow(attacker, defender);
    }
    
 public static void ResolveGrapple(CreatureStats attacker, CreatureStats defender, bool isFreeAction = false, NaturalAttack initiatingAttack = null)
    {
        CombatManeuvers.ResolveGrapple(attacker, defender, isFreeAction, initiatingAttack);
    }


    #endregion

    #region Utilities (Delegated to CombatCalculations)

    public static bool IsFlankedBy(CreatureStats defender, CreatureStats attacker)
    {
        return CombatCalculations.IsFlankedBy(defender, attacker);
    }

    // Extension method simulation
    public static void TakeDamage(this CreatureStats creature, int damage, string damageType)
    {
        // Calling the real TakeDamage method on CreatureStats
        // Note: C# Extension methods work on Godot objects fine
        creature.TakeDamage(damage, damageType, null, null, null);

        // --- FLY SKILL CHECK ---
        // Rule: A creature with wings taking damage must make a Fly check to avoid losing altitude.
        if (creature.Template.Speed_Fly > 0 && damage > 0)
        {
            var mover = creature.GetNodeOrNull<CreatureMover>("CreatureMover");
            mover?.HandleDamageWhileFlying(creature.Template.HasWings);
        }
    }
    #endregion
}