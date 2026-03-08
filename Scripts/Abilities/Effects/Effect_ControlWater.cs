using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: Effect_ControlWater.cs (GODOT VERSION)
// PURPOSE: Casts Control Water. Dynamically sizes the area based on Caster Level.
//          Alters the grid, and applies Slow to aquatic creatures if Lower Water is used.
// =================================================================================================
[GlobalClass]
public partial class Effect_ControlWater : AbilityEffectComponent
{
    [ExportGroup("Spell Mode")]
    [Export]
    [Tooltip("If true, lowers water and slows aquatics. If false, raises water and floods land.")]
    public bool IsLowerWater = true;

    [Export]
    [Tooltip("The Slow effect applied to Water Elementals/Aquatics on a failed save (Used only in Lower Water mode).")]
    public StatusEffect_SO AquaticSlowEffect;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        if (context.Caster == null) return;

        int cl = context.Caster.Template.CasterLevel;
        
        // Rule: 10 ft./level by 10 ft./level by 2 ft./level
        float widthAndLength = cl * 10f;
        float height = cl * 2f;
        float durationSeconds = cl * 10f * 60f; // 10 min/level

        // 1. Alter the Physical Map Grid
        var zoneNode = new Node3D();
        zoneNode.Name = "ControlWaterZone";
        context.Caster.GetTree().CurrentScene.AddChild(zoneNode);
        zoneNode.GlobalPosition = context.AimPoint;

        var zoneController = new PersistentEffect_ControlWater();
        zoneController.Name = "PersistentEffect_ControlWater";
        zoneNode.AddChild(zoneController);
        
        zoneController.Initialize(widthAndLength, height, IsLowerWater, durationSeconds);
        GD.PrintRich($"[color=cyan]{context.Caster.Name} casts Control Water ({widthAndLength}x{widthAndLength}x{height} ft).[/color]");

        // 2. Resolve Hostile Effect on Aquatic Creatures (Lower Water only)
        if (IsLowerWater && AquaticSlowEffect != null)
        {
            // We must manually query the targets because the area is dynamically sized,
            // so CombatMagic's pre-population couldn't catch them.
            float radius = widthAndLength / 2f;
            var targetsInArea = AoEHelper.GetTargetsInBurst(context.AimPoint, new AreaOfEffect { Range = radius }, "Creature");

            foreach (var target in targetsInArea)
            {
                if (target == context.Caster) continue;
                if (TargetFilter != null && !TargetFilter.IsTargetValid(context.Caster, target)) continue;

                // Check for water-based dependency
                bool isAquatic = target.Template.SubTypes.Contains("Water") || 
                                 target.Template.SubTypes.Contains("Aquatic") || 
                                 target.Template.NaturalEnvironmentProperties.Contains(EnvironmentProperty.Aquatic);

                if (isAquatic)
                {
                    // Resolve Will Save locally (since target wasn't in CombatMagic's original sweep)
                    int saveRoll = Dice.Roll(1, 20) + target.GetWillSave(context.Caster, ability);
                    
                    int dc = ability.SavingThrow.BaseDC;
                    if (ability.SavingThrow.IsDynamicDC)
                    {
                        int statMod = context.Caster.GetAbilityScore(ability.SavingThrow.DynamicDCStat);
                        dc = 10 + ability.SpellLevel + statMod;
                    }

                    GD.Print($"{target.Name} (Aquatic) must save vs Lower Water! Roll: {saveRoll} vs DC {dc}.");

                    if (saveRoll < dc)
                    {
                        GD.PrintRich($"[color=red]{target.Name} fails and is Slowed by the evaporating water![/color]");
                        var effectInstance = (StatusEffect_SO)AquaticSlowEffect.Duplicate();
                        effectInstance.DurationInRounds = Mathf.FloorToInt(durationSeconds / 6f);
                        target.MyEffects.AddEffect(effectInstance, context.Caster, ability);
                    }
                }
            }
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        float score = 0f;
        var enemies = AISpatialAnalysis.FindVisibleTargets(context.Caster);
        var allies = AISpatialAnalysis.FindAllies(context.Caster);
        allies.Add(context.Caster);

        if (IsLowerWater)
        {
            // High value if fighting aquatic enemies
            foreach (var enemy in enemies)
            {
                bool isAquatic = enemy.Template.SubTypes.Contains("Water") || enemy.Template.SubTypes.Contains("Aquatic");
                if (isAquatic && context.AimPoint.DistanceTo(enemy.GlobalPosition) <= (context.Caster.Template.CasterLevel * 5f))
                {
                    score += 150f; // AOE Slow and strips water advantage
                }
            }

            // High value to save drowning allies
            foreach (var ally in allies)
            {
                var swimCtrl = ally.GetNodeOrNull<SwimController>("SwimController");
                if (swimCtrl != null && ally.CurrentHP > 0 && ally.CurrentHP < 10) // Dying from drowning proxy
                {
                    if (GridManager.Instance.NodeFromWorldPoint(ally.GlobalPosition).terrainType == TerrainType.Water)
                    {
                        score += 300f; // Save an ally
                    }
                }
            }
        }
        else // Raise Water
        {
            // High value if the caster/allies are aquatic and enemies are not (drowns enemies)
            bool casterIsAquatic = context.Caster.Template.NaturalEnvironmentProperties.Contains(EnvironmentProperty.Aquatic);
            if (casterIsAquatic)
            {
                int nonAquaticEnemies = enemies.Count(e => !e.Template.NaturalEnvironmentProperties.Contains(EnvironmentProperty.Aquatic));
                score += nonAquaticEnemies * 80f;
            }
        }

        return score;
    }
}