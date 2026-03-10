using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: VisibilityManager.cs (GODOT VERSION)
// PURPOSE: Manages the visual state of creatures based on the active player's line of sight.
// ATTACH TO: A persistent "GameManager" Node.
// =================================================================================================

public partial class VisibilityManager : Node
{
    // A list containing a reference to every creature involved in the current combat.
    private List<CreatureStats> allCombatants = new List<CreatureStats>();
    
    // A reference to the creature whose perspective we are currently using.
    private CreatureStats activeViewer;

   // Temporary reveal effects keyed by viewer, used by effects such as Discern Location.
    private readonly Dictionary<CreatureStats, HashSet<CreatureStats>> temporaryRevealsByViewer = new();

    // Singleton access if needed, though UpdateVisibility is often called by TurnManager directly.
    public static VisibilityManager Instance { get; private set; }

    public override void _Ready()
    {
        if (Instance != null && Instance != this) 
        {
            QueueFree();
        }
        else 
        {
            Instance = this;
        }
    }

    /// <summary>
    /// Initializes the manager at the start of combat with a list of all participants.
    /// </summary>
    public void Initialize(List<CreatureStats> combatants) 
    {
        allCombatants = combatants;
    }

    /// <summary>
    /// The core update method. It should be called every time the active creature changes.
    /// </summary>
    public void UpdateVisibility(CreatureStats currentTurnCreature) 
    {
        if (!GodotObject.IsInstanceValid(currentTurnCreature) || allCombatants == null) return;
        
        activeViewer = currentTurnCreature;

        foreach (var creatureToAssess in allCombatants) 
        {
            if (!GodotObject.IsInstanceValid(creatureToAssess)) continue;
            
            // The active creature should always be fully visible to itself.
            if (creatureToAssess == activeViewer) 
            {
                SetCreatureVisuals(creatureToAssess, true, false, 0); 
                continue; 
            }

            // --- The Core Logic ---
            VisibilityResult result = LineOfSightManager.GetVisibility(activeViewer, creatureToAssess);
            var stateController = activeViewer.GetNodeOrNull<CombatStateController>("CombatStateController");
            
            // --- Case 1: Clear Line of Sight ---
            if (result.HasLineOfSight)
            {
				stateController?.MarkCreatureAsSeen(creatureToAssess);
                stateController?.UpdateEnemyLocation(creatureToAssess, LocationStatus.Pinpointed);
                SetCreatureVisuals(creatureToAssess, true, false, result.ConcealmentMissChance);
            }
			 // --- Case 1.5: Temporarily revealed regardless of line of sight ---
            else if (IsTemporarilyRevealed(activeViewer, creatureToAssess))
            {
                stateController?.UpdateEnemyLocation(creatureToAssess, LocationStatus.Pinpointed);
                SetCreatureVisuals(creatureToAssess, true, false, 0);
            }
            // --- Case 2: No Line of Sight, but has Line of Effect ---
            else if (result.HasLineOfEffect)
            {
                SetCreatureVisuals(creatureToAssess, true, true, 100); 
            }
            // --- Case 3: No Line of Sight OR Line of Effect ---
            else
            {
                if (creatureToAssess.IsInGroup("Player"))
                {
                    SetCreatureVisuals(creatureToAssess, true, true, 100);
                }
                else 
                {
                    SetCreatureVisuals(creatureToAssess, false, false, 100);
                }
            }
        }
    }

 public void GrantTemporaryReveal(CreatureStats viewer, CreatureStats revealedCreature)
    {
        if (viewer == null || revealedCreature == null) return;

        if (!temporaryRevealsByViewer.TryGetValue(viewer, out var revealedSet))
        {
            revealedSet = new HashSet<CreatureStats>();
            temporaryRevealsByViewer[viewer] = revealedSet;
        }

        revealedSet.Add(revealedCreature);
    }

    public void ClearTemporaryReveals(CreatureStats viewer)
    {
        if (viewer == null) return;
        temporaryRevealsByViewer.Remove(viewer);
    }

    private bool IsTemporarilyRevealed(CreatureStats viewer, CreatureStats target)
    {
        return viewer != null && target != null &&
               temporaryRevealsByViewer.TryGetValue(viewer, out var revealedSet) &&
               revealedSet.Contains(target);
    }

    /// <summary>
    /// A helper method that applies the final visual changes to a creature's Node.
    /// </summary>
    private void SetCreatureVisuals(CreatureStats creature, bool isRendered, bool useOutline, int missChance) 
    {
        // Get the main visual component. 
        // In Godot, this might be a Sprite3D or a MeshInstance3D. 
        // We assume standard MeshInstance3D setup for 3D creatures.
        var renderer = creature.GetNodeOrNull<MeshInstance3D>("Visuals/Mesh") ?? creature.GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
        
        // Placeholder outline logic: In Godot, outlines are often shaders or second pass materials.
        // We will simulate "outline" by tinting or visibility for now, as specific shader implementation varies.
        
        Color outlineColor = Colors.Transparent; // Logic accumulator

        if (renderer != null) 
        {
            renderer.Visible = isRendered && !useOutline;
            
            // Handle Ghostly/Transparent look
            // Note: Changing material properties in Godot requires the material to be unique (ResourceLocalToScene = true) 
            // or cloned, otherwise it affects all instances.
            if (isRendered && creature.MyEffects.HasCondition(Condition.Invisible))
            {
                // Simple modulation for transparency if using StandardMaterial3D
                renderer.Transparency = 0.5f; 
            }
            else
            {
                renderer.Transparency = 0f;
            }
        }

        // --- PLAYER SENSE FEEDBACK LOGIC ---
        if (activeViewer.IsInGroup("Player") && isRendered)
        {
            // Lifesense: Green
            if (activeViewer.Template.HasLifesense)
            {
                bool isLiving = creature.Template.Type != CreatureType.Undead && creature.Template.Type != CreatureType.Construct;
                if (isLiving) outlineColor = Colors.Green;
            }

            // Detect Evil: Red
            if (activeViewer.Template.HasDetectEvil && CombatMemory.IsKnownToBeEvil(creature))
            {
                outlineColor = Colors.Red;
            }
            // Detect Good: Gold
            if (activeViewer.Template.HasDetectGood && CombatMemory.IsKnownToBeGood(creature))
            {
                outlineColor = Colors.Gold; 
            }
            // Detect Law: Blue
            if (activeViewer.Template.HasDetectLaw && CombatMemory.IsKnownToBeLawful(creature))
            {
                outlineColor = Colors.Blue;
            }
            // Detect Chaos: Magenta
            if (activeViewer.Template.HasDetectChaos && CombatMemory.IsKnownToBeChaotic(creature))
            {
                outlineColor = Colors.Magenta;
            }

            // Deathwatch Visualization
            bool hasDeathwatch = activeViewer.Template.PassiveEffects.Any(e => e.EffectName.Contains("Deathwatch")) 
                                 || activeViewer.MyEffects.HasCondition(Condition.SensingDeathwatch);

            if (hasDeathwatch)
            {
                float healthPercent = (float)creature.CurrentHP / creature.Template.MaxHP;
                
                if (creature.CurrentHP <= 0) outlineColor = Colors.Gray; // Dead/Dying
                else if (healthPercent < 0.25f) outlineColor = Colors.Red; // Fragile
                else if (creature.Template.Type == CreatureType.Undead) outlineColor = new Color(0.5f, 0f, 0.5f); // Purple
                else if (creature.Template.Type == CreatureType.Construct) outlineColor = new Color(0.5f, 0.5f, 0.5f); // Grey
            }

            // Thoughtsense Visuals
            if (activeViewer.Template.HasThoughtsense)
            {
                bool? sentient = CombatMemory.IsKnownToBeSentient(creature);
                if (sentient.HasValue)
                {
                    outlineColor = sentient.Value ? Colors.Magenta : Colors.Cyan;
                }
            }

            // Smoke Vision Feedback
            if (activeViewer.Template.HasSmokeVision)
            {
                GridNode targetNode = GridManager.Instance.NodeFromWorldPoint(creature.GlobalPosition);
                bool inSmoke = targetNode.environmentalTags.Contains("Smoke") || targetNode.environmentalTags.Contains("Ash");
                if (inSmoke)
                {
                    outlineColor = Colors.Gray; 
                }
            }

            // Disease Scent
            if (activeViewer.Template.HasDiseaseScent)
            {
                bool isDiseased = creature.MyEffects.HasCondition(Condition.Sickened) || 
                                  creature.MyEffects.HasCondition(Condition.Nauseated);
                if (isDiseased)
                {
                    outlineColor = new Color(0.6f, 0.8f, 0.2f); // Sickly Green
                }
            }

            // Appraising Sight
            if (activeViewer.Template.HasAppraisingSight)
            {
                var inv = creature.GetNodeOrNull<InventoryController>("InventoryController");
                if (inv != null)
                {
                    bool hasMagicLoot = false;
                    var weapon = inv.GetEquippedItem(EquipmentSlot.MainHand);
                    if (weapon != null && weapon.Modifications.Any(m => m.BonusType == BonusType.Enhancement)) hasMagicLoot = true;
                    
                    var armor = inv.GetEquippedItem(EquipmentSlot.Armor);
                    if (armor != null && armor.Modifications.Any(m => m.BonusType == BonusType.Enhancement)) hasMagicLoot = true;

                    if (hasMagicLoot)
                    {
                        outlineColor = Colors.Gold;
                    }
                }
            }
        }

        // Apply Outline (Godot Implementation Suggestion)
        // If outlineColor is not Transparent, apply it to a ShaderMaterial parameter or enable an Outline Mesh.
        if (renderer != null && outlineColor != Colors.Transparent)
        {
            // Example: Assumes shader has "outline_color" param
            // (renderer.GetActiveMaterial(0) as ShaderMaterial)?.SetShaderParameter("outline_color", outlineColor);
            // Or set a debug mesh/sprite color
        }
    }
}