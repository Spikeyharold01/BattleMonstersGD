using Godot;
using System.Collections.Generic;
// =================================================================================================
// FILE: Effect_GaseousForm.cs (GODOT VERSION)
// PURPOSE: Applies Gaseous Form or Wind Walk.
// ATTACH TO: Create assets from this in the FileSystem dock.
// =================================================================================================
[GlobalClass]
public partial class Effect_GaseousForm : AbilityEffectComponent
{
[Export] public StatusEffect_SO GaseousStatus;
[Export] public bool IsWindWalk = false;
[Export] public bool UseNormalSpeedInGaseousForm = false;
[Export(PropertyHint.Range, "1,120,1")] public int MinutesPerLevel = 2;
[Export] public bool RemoveExistingController = true;
[Export] public bool MythicAugmentSpendTwoUsesForMoveShift = false;

public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
{
    foreach(var target in context.AllTargetsInAoE)
    {
         if (target == null) continue;

         // 2. Apply Status
         var instance = (StatusEffect_SO)GaseousStatus.Duplicate();
         int casterLevel = context.Caster?.Template?.CasterLevel ?? 1;
         instance.DurationInRounds = casterLevel * MinutesPerLevel * 10;
         target.MyEffects.AddEffect(instance, context.Caster);

         if (RemoveExistingController)
         {
             var existing = target.GetNodeOrNull<GaseousFormController>("GaseousFormController");
             existing?.QueueFree();
         }

         // 3. Add Logic Controller
         var ctrl = new GaseousFormController();
         ctrl.Name = "GaseousFormController";
         target.AddChild(ctrl);
         
         ctrl.IsWindWalk = IsWindWalk;
          ctrl.UseNormalSpeedInGaseousForm = UseNormalSpeedInGaseousForm;
         ctrl.IsMythic = context.IsMythicCast;
          ctrl.CanShiftAsMoveAction = context.IsMythicCast && MythicAugmentSpendTwoUsesForMoveShift && context.Caster != null && context.Caster.CurrentMythicPower >= 2;

         if (ctrl.CanShiftAsMoveAction)
         {
             context.Caster.ConsumeMythicPower();
             context.Caster.ConsumeMythicPower();
         }
    }
}

public override float GetAIEstimatedValue(EffectContext context)
{
    return 50f; // Defensive utility
}
}
// Depe