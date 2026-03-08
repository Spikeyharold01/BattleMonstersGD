using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: Effect_BeastShape.cs
// PURPOSE: Calculates dynamic stat bonuses and filters abilities for Beast Shape I-IV and Mythic.
// =================================================================================================
[GlobalClass]
public partial class Effect_BeastShape : AbilityEffectComponent
{
    [ExportGroup("Spell Configuration")]
    [Export(PropertyHint.Range, "1,4,1")] public int SpellTier = 1; // 1, 2, 3, or 4
    [Export] public int MinutesPerLevel = 1;
    [Export] public Godot.Collections.Array<CreatureTemplate_SO> AllowedForms = new();

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        CreatureTemplate_SO targetForm = context.SelectedResource as CreatureTemplate_SO;
        if (targetForm == null && AllowedForms.Count > 0) targetForm = AllowedForms[0];

        if (targetForm == null || context.Caster == null)
        {
            GD.PrintErr("Beast Shape failed: No valid target form provided.");
            return;
        }

        // Clean up previous polymorphs
        var existingPoly = context.Caster.GetNodeOrNull<PolymorphController>("PolymorphController");
        if (existingPoly != null) existingPoly.QueueFree(); // Let it clean up next frame

        bool isMythic = context.IsMythicCast;
        bool isAugmented = isMythic && context.Caster.CurrentMythicPower >= 1; // Uses 2 total

        if (isAugmented) context.Caster.ConsumeMythicPower();

        // 1. Calculate Base Modifiers
        var mods = CalculateFormBonuses(targetForm.Size, targetForm.Type, isMythic, isAugmented);

        // 2. Create the Status Effect
        var polymorphEffect = new StatusEffect_SO
        {
            EffectName = $"Beast Shape ({targetForm.CreatureName})",
            ConditionApplied = Condition.Polymorphed,
            DurationInRounds = context.Caster.Template.CasterLevel * 10 * MinutesPerLevel
        };

        if (mods.strBonus != 0) polymorphEffect.Modifications.Add(new StatModification { StatToModify = StatToModify.Strength, ModifierValue = mods.strBonus, BonusType = BonusType.Enhancement }); // Polymorph bonuses are size bonuses, mapped to Untyped or Size. We'll use Untyped for simplicity here to ensure stacking if base size isn't overridden properly. Actually Size bonus is correct.
        if (mods.dexBonus != 0) polymorphEffect.Modifications.Add(new StatModification { StatToModify = StatToModify.Dexterity, ModifierValue = mods.dexBonus, BonusType = BonusType.Enhancement });
        if (mods.conBonus != 0) polymorphEffect.Modifications.Add(new StatModification { StatToModify = StatToModify.Constitution, ModifierValue = mods.conBonus, BonusType = BonusType.Enhancement });
        if (mods.naBonus != 0) polymorphEffect.Modifications.Add(new StatModification { StatToModify = StatToModify.ArmorClass, ModifierValue = mods.naBonus, BonusType = BonusType.Natural });
        
        // Element Resistances (Beast Shape IV)
        if (SpellTier >= 4 && targetForm.Resistances != null && targetForm.Type == CreatureType.MagicalBeast)
        {
            foreach (var res in targetForm.Resistances)
            {
                foreach(var type in res.DamageTypes)
                {
                    polymorphEffect.ResistDamageTypes.Add(type);
                }
            }
            if (polymorphEffect.ResistDamageTypes.Count > 0) polymorphEffect.DamageResistanceAmount = 20;
        }

        context.Caster.MyEffects.AddEffect(polymorphEffect, context.Caster, ability);

        // 3. Create the Template Modifier
        var templateMod = new CreatureTemplateModifier_SO();
        templateMod.ResourceName = $"BeastShape_{targetForm.CreatureName}";
        
        // Base Overrides
        templateMod.ChangeTypeTo = CreatureType.None; // We retain our original type
        FilterAndApplyAbilities(targetForm, templateMod, SpellTier);

        // 4. Apply Mythic Attack Buff
        if (isMythic && templateMod.AddMeleeAttacks.Count > 0)
        {
            // Find highest damage natural attack to buff
            var bestAttack = templateMod.AddMeleeAttacks.OrderByDescending(a => a.DamageInfo.FirstOrDefault()?.DieSides ?? 0).First();
            bestAttack.CriticalMultiplier = Mathf.Min(4, bestAttack.CriticalMultiplier + 1);
            GD.Print($"Mythic Beast Shape increased {bestAttack.AttackName} crit multiplier to x{bestAttack.CriticalMultiplier}.");
        }

        // 5. Attach Controller
        var controller = new PolymorphController();
        controller.Name = "PolymorphController";
        context.Caster.AddChild(controller);
        
        int naturalSpellRounds = isAugmented ? Mathf.Max(1, context.Caster.Template.MythicRank) : 0;
        controller.Initialize(targetForm, templateMod, polymorphEffect.EffectName, naturalSpellRounds);

        GD.PrintRich($"[color=purple]{context.Caster.Name} polymorphs into a {targetForm.CreatureName}![/color]");
    }

    private void FilterAndApplyAbilities(CreatureTemplate_SO targetForm, CreatureTemplateModifier_SO modifier, int tier)
    {
        // Duplicate attacks so we don't modify the master asset
        foreach (var atk in targetForm.MeleeAttacks)
        {
            modifier.AddMeleeAttacks.Add((NaturalAttack)atk.Duplicate());
        }

        // Filter Speeds
        float maxClimb = tier >= 3 ? 90 : tier == 2 ? 60 : 30;
        float maxFly = tier >= 4 ? 120 : tier >= 3 ? 90 : tier == 2 ? 60 : 30;
        float maxSwim = tier >= 4 ? 120 : tier >= 3 ? 90 : tier == 2 ? 60 : 30;
        float maxBurrow = tier >= 4 ? 60 : tier >= 3 ? 30 : 0;

        if (targetForm.Speed_Climb > 0) modifier.AddSpecialQualities.Add($"Climb Speed {Mathf.Min(targetForm.Speed_Climb, maxClimb)}"); // Hack: applied loosely for now, real implementation would inject the speed directly via template copy
        
        // Allowed Special Abilities Check
        var allowedSpecials = new HashSet<string>();
        if (tier >= 1) { allowedSpecials.Add("Low-Light Vision"); allowedSpecials.Add("Scent"); }
        if (tier >= 2) { allowedSpecials.Add("Grab"); allowedSpecials.Add("Pounce"); allowedSpecials.Add("Trip"); }
        if (tier >= 3) { allowedSpecials.Add("Constrict"); allowedSpecials.Add("Ferocity"); allowedSpecials.Add("Jet"); allowedSpecials.Add("Poison"); allowedSpecials.Add("Rake"); allowedSpecials.Add("Trample"); allowedSpecials.Add("Web"); }
        if (tier >= 4) { allowedSpecials.Add("Breath Weapon"); allowedSpecials.Add("Rend"); allowedSpecials.Add("Roar"); allowedSpecials.Add("Spikes"); }

        foreach (var sa in targetForm.SpecialAttacks)
        {
            if (allowedSpecials.Any(s => sa.AbilityName.Contains(s, System.StringComparison.OrdinalIgnoreCase)))
            {
                modifier.AddSpecialAttacks.Add((Ability_SO)sa.Duplicate());
            }
        }
    }

    private (int strBonus, int dexBonus, int conBonus, int naBonus) CalculateFormBonuses(CreatureSize size, CreatureType type, bool isMythic, bool isAugmented)
    {
        int str = 0, dex = 0, con = 0, na = 0;

        if (type == CreatureType.Animal)
        {
            if (size == CreatureSize.Diminutive && SpellTier >= 3) { dex = 6; str = -4; na = 1; }
            else if (size == CreatureSize.Tiny && SpellTier >= 2) { dex = 4; str = -2; na = 1; }
            else if (size == CreatureSize.Small) { dex = 2; na = 1; }
            else if (size == CreatureSize.Medium) { str = 2; na = 2; }
            else if (size == CreatureSize.Large && SpellTier >= 2) { str = 4; dex = -2; na = 4; }
            else if (size == CreatureSize.Huge && SpellTier >= 3) { str = 6; dex = -4; na = 6; }
        }
        else if (type == CreatureType.MagicalBeast && SpellTier >= 3)
        {
            if (size == CreatureSize.Tiny && SpellTier >= 4) { dex = 8; str = -2; na = 3; }
            else if (size == CreatureSize.Small) { dex = 4; na = 2; }
            else if (size == CreatureSize.Medium) { str = 4; na = 4; }
            else if (size == CreatureSize.Large && SpellTier >= 4) { str = 6; dex = -2; con = 2; na = 6; }
        }

        if (isMythic)
        {
            str += str > 0 ? 2 : (str < 0 ? 2 : 0); 
            dex += dex > 0 ? 2 : (dex < 0 ? 2 : 0);
            con += con > 0 ? 2 : (con < 0 ? 2 : 0);
            na += 1;

            if (isAugmented)
            {
                str += str > 0 ? 2 : 0;
                dex += dex > 0 ? 2 : 0;
                con += con > 0 ? 2 : 0;
            }
            
            str = Mathf.Min(0, str); // Min penalty 0
            dex = Mathf.Min(0, dex);
        }

        return (str, dex, con, na);
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        CreatureTemplate_SO evaluatedForm = context.SelectedResource as CreatureTemplate_SO;
        if (evaluatedForm == null) return 0f;

        float score = 50f;
        var caster = context.Caster;
        var enemies = AISpatialAnalysis.FindVisibleTargets(caster);

        // 1. Mobility Needs
        bool needsFlight = enemies.Any(e => e.GlobalPosition.Y > caster.GlobalPosition.Y + 10f);
        if (needsFlight && evaluatedForm.Speed_Fly > 0) score += 150f;

        // 2. Offense Needs
        int myMaxMelee = caster.Template.MeleeAttacks.Count;
        int newMaxMelee = evaluatedForm.MeleeAttacks.Count;
        
        if (newMaxMelee > myMaxMelee) score += (newMaxMelee - myMaxMelee) * 30f;

        if (evaluatedForm.SpecialQualities.Contains("Pounce") || evaluatedForm.SpecialAttacks.Any(a => a.AbilityName.Contains("Pounce")))
        {
            // Extremely high value if we have distant targets
            score += 200f;
        }

        if (evaluatedForm.SpecialAttacks.Any(a => a.AbilityName.Contains("Grab")))
        {
            var vulnerableEnemy = enemies.FirstOrDefault(e => e.Template.Size < evaluatedForm.Size);
            if (vulnerableEnemy != null) score += 100f;
        }

        // Penalize forms that strip casting if we want to cast
        if (!context.IsAugmentedMythicCast)
        {
            if (caster.Template.PrimaryCastingStat != AbilityScore.None && caster.MyUsage != null)
            {
                score -= 80f; // Hesitant to lock out own spellcasting
            }
        }

        return Mathf.Max(0f, score);
    }
}