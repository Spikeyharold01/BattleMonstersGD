
using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: PersistentEffectController.cs (GODOT VERSION)
// PURPOSE: A component attached to a Node to represent a lingering spell effect on the grid.
// ATTACH TO: A generic prefab that will be spawned by a spell effect (Node3D).
// =================================================================================================
public class PersistentEffectInfo
{
public int InstanceID;
public string EffectName;
public string Description;
public Ability_SO SourceAbility;
public CreatureStats Caster;
public int SpellLevel;
public int SaveDC;
public Godot.Collections.Array<AbilityEffectComponent> LingeringEffects;
}
public partial class PersistentEffectController : Node3D
{
public PersistentEffectInfo Info { get; private set; }
public float Duration { get; private set; }
public AreaOfEffect Area { get; private set; }

private static int nextInstanceID = 1;

// A list of creatures currently inside the effect's area.
private HashSet<CreatureStats> creaturesInArea = new HashSet<CreatureStats>();

public void Initialize(PersistentEffectInfo info, float duration, AreaOfEffect area)
{
    info.InstanceID = nextInstanceID++;
    this.Info = info;
    this.Duration = duration;
    this.Area = area;

    // Scale a visual representation (Node3D Scale)
    Scale = new Vector3(area.Range * 2, 1, area.Range * 2); // For a circular burst
    
    EffectManager.Instance?.RegisterEffect(this);
    AddToGroup("PersistentEffect");
}

public override void _Process(double delta)
{
    Duration -= (float)delta;
	// Wind Dispersal Logic
    if (Info.EffectName.ToLower().Contains("cloud") || Info.EffectName.ToLower().Contains("fog"))
    {
        if (WeatherManager.Instance != null && WeatherManager.Instance.CurrentWeather != null)
        {
            var wind = WeatherManager.Instance.CurrentWeather.WindStrength;
            
            bool isMythicResistant = Info.EffectName.Contains("(Mythic)");

            if (wind >= WindStrength.Strong)
            {
                GD.Print($"{Info.EffectName} is dispersed by strong wind!");
                Duration = 0; 
            }
            else if (wind >= WindStrength.Moderate && !isMythicResistant)
            {
                Duration -= (float)delta * 3; 
            }
        }
    }
    if (Duration <= 0)
    {
        foreach (var creature in creaturesInArea)
        {
            creature.MyEffects.RemoveEffectsFromSource(Info.InstanceID);
        }
        EffectManager.Instance.UnregisterEffect(this);
        QueueFree();
    }
}

// Called by the EffectManager every frame to check for creatures entering/leaving.
public void TickAreaCheck()
{
    // Check for newly entered creatures
    var allCreatures = TurnManager.Instance.GetAllCombatants();
    var creaturesNowInArea = new HashSet<CreatureStats>();

    foreach (var creature in allCreatures)
    {
        bool isInside = false;
        if (Area.Shape == AoEShape.Cylinder)
        {
            float hDist = new Vector2(GlobalPosition.X, GlobalPosition.Z).DistanceTo(new Vector2(creature.GlobalPosition.X, creature.GlobalPosition.Z));
            float vDist = creature.GlobalPosition.Y - GlobalPosition.Y;
            if (hDist <= Area.Range && vDist >= 0 && vDist <= Area.Height) isInside = true;
        }
        else
        {
            if (GlobalPosition.DistanceTo(creature.GlobalPosition) <= Area.Range) isInside = true;
        }

        if (isInside)
        {
            creaturesNowInArea.Add(creature);
        }
    }

    // Check for creatures that have left
    var creaturesThatLeft = new HashSet<CreatureStats>(creaturesInArea);
    creaturesThatLeft.ExceptWith(creaturesNowInArea);
    foreach (var creature in creaturesThatLeft)
    {
        OnCreatureExit(creature);
    }

    // Update the master list
    creaturesInArea = creaturesNowInArea;
}

private void OnCreatureEnter(CreatureStats creature)
{
    GD.Print($"{creature.Name} enters the area of {Info.EffectName}.");
    ApplyLingeringEffects(creature);
}

private void OnCreatureExit(CreatureStats creature)
{
    GD.Print($"{creature.Name} leaves the area of {Info.EffectName}.");
    creature.MyEffects.RemoveEffectsFromSource(Info.InstanceID);
}

public void ApplyLingeringEffects(CreatureStats target)
{
    if (Info.LingeringEffects == null) return;
    
    var context = new EffectContext
    {
        Caster = Info.Caster,
        PrimaryTarget = target,
        AllTargetsInAoE = new Godot.Collections.Array<CreatureStats> { target }
    };

    bool saved = false;
    if (Info.SaveDC > 0)
    {
        int saveRoll = Dice.Roll(1, 20) + target.GetReflexSave(Info.Caster); 
        if (saveRoll >= Info.SaveDC) saved = true;
    }

    var saveResults = new Dictionary<CreatureStats, bool> { { target, saved } };

    foreach (var effect in Info.LingeringEffects)
    {
        // Note: `ExecuteAndTrackLingeringEffect` is used to handle status effect ID tracking.
        if (effect is ApplyStatusEffect applyStatusEffect)
        {
            ExecuteAndTrackLingeringEffect(effect, context, saveResults, target);
        }
        else
        {
             effect.ExecuteEffect(context, Info.SourceAbility, saveResults);
        }
    }
}

private void ExecuteAndTrackLingeringEffect(AbilityEffectComponent effect, EffectContext context, Dictionary<CreatureStats, bool> saveResults, CreatureStats target)
{
     if (effect is ApplyStatusEffect applyStatusEffect)
    {
        bool didSave = saveResults.ContainsKey(target) && saveResults[target];
        if (!didSave)
        {
            var effectInstance = (StatusEffect_SO)applyStatusEffect.EffectToApply.Duplicate();
            
            var activeEffectInstance = new ActiveStatusEffect(effectInstance, context.Caster)
            {
                SourcePersistentEffectID = this.Info.InstanceID,
                SourceSpellLevel = Info.SourceAbility.SpellLevel,
                SaveDC = Info.SaveDC
            };
            target.MyEffects.ActiveEffects.Add(activeEffectInstance);
             GD.Print($"{target.Name} is now affected by {effectInstance.EffectName} from {Info.EffectName}.");
        }
    }
    else
    {
        effect.ExecuteEffect(context, Info.SourceAbility, saveResults);
    }
}
}