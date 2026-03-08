using Godot;
using System.Collections.Generic;
using System.Linq;

[GlobalClass]
public partial class MultiProjectileDamageEffect : DamageEffect
{
    [ExportGroup("Projectile Count")]
    [Export] public int BaseProjectileCount = 1;
    [Export] public int AdditionalProjectilePerCasterLevels = 2;
    [Export] public int MaxProjectileCount = 5;

    [ExportGroup("Targeting")]
    [Export] public int MaxUniqueTargets = 5;
    [Export] public float MaxDistanceBetweenTargetsFeet = 15f;
    [Export] public bool RequireLineOfEffect = true;
    [Export] public bool RequireLineOfSight = true;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (Damage == null || context?.Caster == null)
            return;

        int projectileCount = GetProjectileCount(context.Caster.Template?.CasterLevel ?? 1);
        if (projectileCount <= 0)
            return;

        var validTargets = GetValidTargets(context, ability);
        if (validTargets.Count == 0)
            return;

        var cluster = MultiTargetingHelper.BuildPairwiseCluster(
            validTargets,
            context.PrimaryTarget,
            Mathf.Max(1, MaxUniqueTargets),
            Mathf.Max(0.1f, MaxDistanceBetweenTargetsFeet));

        if (cluster.Count == 0)
            return;

        var missileAssignments = AssignProjectiles(cluster, context.PrimaryTarget, projectileCount);
        foreach (var assignment in missileAssignments)
        {
            ApplyProjectileHits(context, assignment.Key, assignment.Value);
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (Damage == null || context?.Caster?.Template == null)
            return 0f;

        int projectileCount = GetProjectileCount(context.Caster.Template.CasterLevel);
        float avgPerProjectile = (Damage.DiceCount * (Damage.DieSides / 2f + 0.5f)) + Damage.FlatBonus;
        return avgPerProjectile * projectileCount;
    }

    private int GetProjectileCount(int casterLevel)
    {
        int extra = 0;
        if (AdditionalProjectilePerCasterLevels > 0)
            extra = Mathf.FloorToInt(Mathf.Max(0, casterLevel - 1) / (float)AdditionalProjectilePerCasterLevels);

        int total = BaseProjectileCount + extra;
        if (MaxProjectileCount > 0)
            total = Mathf.Min(total, MaxProjectileCount);

        return Mathf.Max(0, total);
    }

    private List<CreatureStats> GetValidTargets(EffectContext context, Ability_SO ability)
    {
        var allCombatants = TurnManager.Instance?.GetAllCombatants()?.ToList() ?? new List<CreatureStats>();
        float rangeFeet = ability?.Range?.GetRange(context.Caster) ?? 0f;

        return allCombatants
            .Where(t => t != null && t != context.Caster)
            .Where(t => rangeFeet <= 0f || context.Caster.GlobalPosition.DistanceTo(t.GlobalPosition) <= rangeFeet)
            .Where(t => TargetFilter == null || TargetFilter.IsTargetValid(context.Caster, t))
            .Where(t => !RequireLineOfEffect || LineOfSightManager.HasLineOfEffect(context.Caster, context.Caster.GlobalPosition, t.GlobalPosition))
            .Where(t =>
            {
                if (!RequireLineOfSight) return true;
                var visibility = LineOfSightManager.GetVisibility(context.Caster, t);
                return visibility.HasLineOfSight;
            })
            .ToList();
    }

    private Dictionary<CreatureStats, int> AssignProjectiles(List<CreatureStats> cluster, CreatureStats primaryTarget, int projectileCount)
    {
        var assignments = cluster.ToDictionary(t => t, _ => 0);

        int targetIndex = 0;
        while (projectileCount > 0 && targetIndex < cluster.Count)
        {
            assignments[cluster[targetIndex]]++;
            projectileCount--;
            targetIndex++;
        }

        if (projectileCount > 0)
        {
            var fallback = (primaryTarget != null && assignments.ContainsKey(primaryTarget)) ? primaryTarget : cluster[0];
            assignments[fallback] += projectileCount;
        }

        return assignments;
    }

    private void ApplyProjectileHits(EffectContext context, CreatureStats target, int hitCount)
    {
        if (target == null || hitCount <= 0)
            return;

        var magicalPseudoWeapon = new Item_SO();
        magicalPseudoWeapon.Modifications.Add(new StatModification
        {
            StatToModify = StatToModify.AttackRoll,
            BonusType = BonusType.Enhancement,
            ModifierValue = 1
        });

        for (int i = 0; i < hitCount; i++)
        {
            int damage = Dice.Roll(Damage.DiceCount, Damage.DieSides) + Damage.FlatBonus;
            if (damage > 0)
                target.TakeDamage(damage, Damage.DamageType, context.Caster, magicalPseudoWeapon, null);
        }
    }
}