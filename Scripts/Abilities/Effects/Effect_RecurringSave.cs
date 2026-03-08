
using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_RecurringSave.cs (GODOT VERSION)
// PURPOSE: A component that can be attached to a StatusEffect_SO to allow a creature to make
// a new saving throw each round to end the effect.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_RecurringSave : AbilityEffectComponent
{
[Export]
[Tooltip("The type of save the creature can make each round.")]
public SaveType SaveType;

[Export]
[Tooltip("The base DC of the recurring save.")]
public int BaseDC;

// This component's logic will be called by the StatusEffectController, not via the standard ExecuteEffect.
public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults) { }
public override float GetAIEstimatedValue(EffectContext context) { return 0; }

public bool AttemptRecurringSave(CreatureStats target, CreatureStats source)
{
    if (target == null) return false;

    int saveBonus = 0;
    switch (SaveType)
    {
        case SaveType.Fortitude: saveBonus = target.GetFortitudeSave(source); break;
        case SaveType.Reflex: saveBonus = target.GetReflexSave(source); break;
        case SaveType.Will: saveBonus = target.GetWillSave(source); break;
    }

    int saveRoll = Dice.Roll(1, 20) + saveBonus;
    GD.Print($"{target.Name} makes a recurring save against an ongoing effect (DC {BaseDC}). Rolls {saveRoll}.");

    return saveRoll >= BaseDC;
}
}