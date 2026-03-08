using Godot;
using System.Linq;

// =================================================================================================
// FILE: BattleMonsters\Scripts\Creature\BreakableEffectController.cs
// PURPOSE: Tracks HP for a specific status effect (e.g. Encased in Ice, Webbed).
//          Listens to OnTakeDamage to reduce HP.
// =================================================================================================
public partial class BreakableEffectController : Godot.Node
{
    private CreatureStats owner;
    private string effectName;
    private int currentHP;
    private int breakDC;
    private Godot.Collections.Array<string> damageTypes;
    private bool allowStrength;

    public void Initialize(string name, int hp, int dc, Godot.Collections.Array<string> types, bool strength)
    {
        effectName = name;
        currentHP = hp;
        breakDC = dc;
        damageTypes = types;
        allowStrength = strength;
        
        owner = GetParent<CreatureStats>();
        if (owner != null) owner.OnTakeDamageDetailed += HandleDamage;
    }

    public override void _ExitTree()
    {
        if (owner != null) owner.OnTakeDamageDetailed -= HandleDamage;
    }

    private void HandleDamage(int amount, string type, CreatureStats attacker, Item_SO weapon, NaturalAttack natural)
    {
        // Check if incoming damage type matches vulnerabilities
        if (damageTypes.Contains(type))
        {
            currentHP -= amount;
            GD.Print($"[Breakable] {effectName} takes {amount} damage. {currentHP} remaining.");
            
            if (currentHP <= 0)
            {
                Break();
            }
        }
    }

    private void Break()
    {
        GD.PrintRich($"[color=cyan]{effectName} on {owner.Name} is destroyed![/color]");
        owner.MyEffects.RemoveEffect(effectName);
        QueueFree();
    }
}