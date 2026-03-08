using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: CreateLightSourceEffect.cs (GODOT VERSION)
// PURPOSE: Generic effect component that creates a magical light or magical darkness source.
// DESIGN GOAL:
// - Keep behavior data-driven so designers can build many spells from one reusable script.
// - Allow use by player-controlled and AI-controlled casters without special-case code.
// - Work in both combat-heavy scenes (arena) and free-movement scenes (travel) because it only
//   depends on shared scene services (SceneTree + GridManager).
// =================================================================================================
[GlobalClass]
public partial class CreateLightSourceEffect : AbilityEffectComponent
{
    // The data asset that describes radius, intensity shift, supernatural flag, and duration.
    // Designers can create multiple assets and reuse this same effect script for each spell.
    [Export] public LightAndDarknessInfo LightData;

    // Spell level is used for countering/dispelling checks between opposing light/darkness effects.
    [Export] public int SpellLevel;

    // Optional switch for mythic behavior. This keeps one script flexible for mythic and non-mythic
    // versions of otherwise similar spells.
    [Export]
    [Tooltip("If true, this effect applies Mythic rules (e.g., minimum of normal light for Daylight).")]
    public bool IsMythicEffect = false;

    [ExportGroup("AI SCORING")]

    // Optional tactical tag so AI can score this effect without writing spell-specific AI scripts.
    [Export]
    [Tooltip("(Optional) Describes the tactical purpose of this effect for the AI.")]
    public TacticalTag_SO AiTacticalTag;

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        // Safety guard: if a designer forgets to assign data, fail gracefully and explain what happened.
        if (LightData == null)
        {
            GD.PrintErr("CreateLightSourceEffect cannot run because LightData is not assigned.");
            return;
        }

        // RULE SUPPORT:
        // "You can only have one light spell active at any one time" style behavior.
        // We only replace the caster's own previous cantrip-level light source (spell level 0)
        // to preserve existing project behavior.
        var existingLights = context.Caster.GetTree().GetNodesInGroup("LightSources");
        foreach (GridNode n in existingLights)
        {
            if (n is LightSourceController lsc && lsc.Info.Caster == context.Caster)
            {
                if (lsc.Info.Data.IsMagical && lsc.Info.SpellLevel == 0)
                {
                    GD.Print($"{context.Caster.Name} casts Light again, dismissing the old one.");
                    lsc.QueueFree();
                }
            }
        }

        Node3D enchantedObject = null;

        // TARGET HANDLING:
        // 1) If the ability targeted a scene object, anchor the source to that object.
        // 2) If the ability targeted empty space, create a simple scene node at the aimed point.
        Node3D targetObjNode = context.TargetObject as Node3D;

        if (targetObjNode != null)
        {
            // If the targeted object is a creature, respect save outcomes provided by the pipeline.
            var targetCreature = targetObjNode as CreatureStats ?? targetObjNode.GetNodeOrNull<CreatureStats>("CreatureStats");
            if (targetCreature != null)
            {
                bool didSave = targetSaveResults.ContainsKey(targetCreature) && targetSaveResults[targetCreature];
                if (didSave)
                {
                    GD.Print($"{targetCreature.Name} saved against {LightData.ResourceName}. Effect failed.");
                    return;
                }
            }

            enchantedObject = targetObjNode;
            GD.Print($"{context.Caster.Name} casts {LightData.ResourceName} on the object '{enchantedObject.Name}'.");
        }
        else
        {
            GD.Print($"{context.Caster.Name} casts {LightData.ResourceName} on a point on the ground.");

            // A neutral anchor node is enough to carry the LightSourceController in world space.
            enchantedObject = new Node3D();
            enchantedObject.Name = $"Static Light Emitter ({LightData.ResourceName})";

            // Add to the active scene and place at the selected point.
            var tree = (SceneTree)Engine.GetMainLoop();
            tree.CurrentScene.AddChild(enchantedObject);
            enchantedObject.GlobalPosition = context.AimPoint;
        }

        if (enchantedObject == null)
        {
            GD.PrintErr("CreateLightSourceEffect failed: No valid target object or aim point.");
            return;
        }

        // Create and initialize the runtime controller that feeds lighting influence into GridManager.
        var lightController = new LightSourceController();
        lightController.Name = "LightSourceController";
        enchantedObject.AddChild(lightController);
        lightController.Initialize(LightData, SpellLevel, context.Caster, IsMythicEffect);

        // COUNTER / DISPEL LOGIC:
        // - Light can counter/dispel darkness of equal or lower spell level.
        // - Darkness can counter/dispel light of equal or lower spell level.
        // This remains generic and works for many spell variants by reading just IntensityChange + SpellLevel.
        bool newSourceIsLight = LightData.IntensityChange > 0;
        bool newSourceIsDarkness = LightData.IntensityChange < 0;

        var allLightSources = ((SceneTree)Engine.GetMainLoop()).GetNodesInGroup("LightSources");
        foreach (GridNode node in allLightSources)
        {
            if (node is not LightSourceController existingSource) continue;
            if (existingSource == lightController) continue;

            bool existingIsLight = existingSource.Info.Data.IntensityChange > 0;
            bool existingIsDarkness = existingSource.Info.Data.IntensityChange < 0;

            // We only attempt a counter check when the two effects are opposing categories.
            bool areOpposed = (newSourceIsLight && existingIsDarkness) || (newSourceIsDarkness && existingIsLight);
            if (!areOpposed) continue;

            // Equal-or-lower level requirement from the spell text.
            if (SpellLevel < existingSource.Info.SpellLevel) continue;

            // Area intersection check: if radii overlap, they can interact.
            float combinedRadius = lightController.Info.Data.Radius + existingSource.Info.Data.Radius;
            float distance = enchantedObject.GlobalPosition.DistanceTo(existingSource.GlobalPosition);
            if (distance > combinedRadius) continue;

            GD.PrintRich($"<color=cyan>{LightData.ResourceName} counters {existingSource.Info.Data.ResourceName}.</color>");
            existingSource.QueueFree();
        }
    }

    // AI value is entirely data-driven through TacticalTag_SO so the same effect works for any actor.
    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (AiTacticalTag == null) return 0;
        return AIScoringEngine.ScoreTacticalTag(AiTacticalTag, context, 1);
    }
}
