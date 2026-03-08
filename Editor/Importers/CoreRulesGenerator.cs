#if TOOLS
using Godot;
using System;
using System.Collections.Generic;

[Tool]
public partial class CoreRulesGenerator : EditorPlugin
{
    public override void _EnterTree()
    {
        AddToolMenuItem("Generate All Core Conditions", new Callable(this, nameof(GenerateConditions)));
    }

    public override void _ExitTree()
    {
        RemoveToolMenuItem("Generate All Core Conditions");
    }

    public void GenerateConditions()
    {
        string path = "res://Data/StatusEffects/";
        DirAccess.MakeDirRecursiveAbsolute(path);

        // --- FEAR EFFECTS ---
        CreateCondition(path, "Shaken", Condition.Shaken, attack: -2, saves: -2, skills: -2);
        CreateCondition(path, "Frightened", Condition.Frightened, attack: -2, saves: -2, skills: -2); // + Fleeing Logic
        CreateCondition(path, "Panicked", Condition.Panicked, attack: -2, saves: -2, skills: -2); // + Drop items Logic
        CreateCondition(path, "Cowering", Condition.Cowering, attack: 0, saves: 0, skills: 0); // -2 AC (Logic handles Dex loss)

        // --- PHYSICAL DEBUFFS ---
        CreateCondition(path, "Sickened", Condition.Sickened, attack: -2, saves: -2, skills: -2, damage: -2);
        CreateCondition(path, "Nauseated", Condition.Nauseated, 0, 0, 0); // Move action only (Logic)
        CreateCondition(path, "Staggered", Condition.Staggered, 0, 0, 0); // Single action (Logic)
        CreateCondition(path, "Dazed", Condition.Dazed, 0, 0, 0); // No actions (Logic)
        CreateCondition(path, "Stunned", Condition.Stunned, 0, 0, 0); // Drop items, No actions, -2 AC, Lose Dex (Logic)
        
        // --- SENSORY ---
        CreateCondition(path, "Dazzled", Condition.Dazzled, attack: -1, saves: 0, skills: -1); // Skills is Perception only usually, but generic -1 is safe
        CreateCondition(path, "Blinded", Condition.Blinded, 0, 0, -4); // -2 AC, Lose Dex (Logic), -4 Perception
        CreateCondition(path, "Deafened", Condition.Deafened, 0, 0, 0); // -4 Init (Logic), Auto-fail Perception (Logic)

        // --- MOVEMENT & GRAPPLES ---
        CreateCondition(path, "Entangled", Condition.Entangled, attack: -2, saves: 0, skills: 0); // -4 Dex handled via Attribute Mod
        // Note: Entangled applies -4 Dex.
        AddStatMod(path, "Entangled", StatToModify.Dexterity, -4);

        CreateCondition(path, "Grappled", Condition.Grappled, attack: -2, saves: 0, skills: 0); 
        // Note: Grappled applies -4 Dex.
        AddStatMod(path, "Grappled", StatToModify.Dexterity, -4);

        CreateCondition(path, "Pinned", Condition.Pinned, attack: 0, saves: 0, skills: 0); // -4 AC, Lose Dex (Logic)
        
        CreateCondition(path, "Prone", Condition.Prone, 0, 0, 0); // Complex AC/Atk rules (Logic handles Melee vs Ranged diff)

        // --- FATIGUE / EXHAUSTION ---
        CreateCondition(path, "Fatigued", Condition.Fatigued, 0, 0, 0);
        AddStatMod(path, "Fatigued", StatToModify.Strength, -2);
        AddStatMod(path, "Fatigued", StatToModify.Dexterity, -2);

        CreateCondition(path, "Exhausted", Condition.Exhausted, 0, 0, 0);
        AddStatMod(path, "Exhausted", StatToModify.Strength, -6);
        AddStatMod(path, "Exhausted", StatToModify.Dexterity, -6);

        // --- OTHER ---
        CreateCondition(path, "Helpless", Condition.Helpless, 0, 0, 0); // Dex=0 (Logic)
        CreateCondition(path, "Bleed", Condition.Bleed, 0, 0, 0); // Damage over time (Logic)
        CreateCondition(path, "Confused", Condition.Confused, 0, 0, 0); // Random action (Logic)
        CreateCondition(path, "Invisible", Condition.Invisible, 0, 0, 0); // +20/+40 Stealth (Logic), +2 Atk (Logic)

        GD.PrintRich("[color=green]All Core Conditions Generated successfully.[/color]");
    }

    private void CreateCondition(string path, string name, Condition cond, int attack, int saves, int skills, int damage = 0)
    {
        string filePath = $"{path}SE_{name}.tres";
        if (FileAccess.FileExists(filePath)) return; 

        var effect = new StatusEffect_SO();
        effect.EffectName = name;
        effect.ConditionApplied = cond;
        effect.DurationInRounds = 0; 

        if (attack != 0) effect.Modifications.Add(CreateMod(StatToModify.AttackRoll, attack));
        if (damage != 0) effect.Modifications.Add(CreateMod(StatToModify.MeleeDamage, damage));
        
        if (saves != 0)
        {
            effect.Modifications.Add(CreateMod(StatToModify.FortitudeSave, saves));
            effect.Modifications.Add(CreateMod(StatToModify.ReflexSave, saves));
            effect.Modifications.Add(CreateMod(StatToModify.WillSave, saves));
        }
        
        if (skills != 0)
        {
            // Perception is a good generic proxy for "Skill Checks" penalty in simple sim
            effect.Modifications.Add(CreateMod(StatToModify.Perception, skills));
            effect.Modifications.Add(CreateMod(StatToModify.Stealth, skills));
        }

        if (name == "Shaken" || name == "Frightened" || name == "Panicked" || name == "Confused") 
            effect.IsMindControlEffect = true;

        ResourceSaver.Save(effect, filePath);
    }

    private void AddStatMod(string path, string name, StatToModify stat, int val)
    {
        string filePath = $"{path}SE_{name}.tres";
        if (!FileAccess.FileExists(filePath)) return;

        var effect = GD.Load<StatusEffect_SO>(filePath);
        effect.Modifications.Add(CreateMod(stat, val));
        ResourceSaver.Save(effect, filePath);
    }

    private StatModification CreateMod(StatToModify stat, int val)
    {
        return new StatModification { StatToModify = stat, ModifierValue = val, BonusType = BonusType.Untyped };
    }
}
#endif