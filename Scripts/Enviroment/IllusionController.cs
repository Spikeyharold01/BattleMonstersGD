using Godot;
using System.Collections.Generic;

// =================================================================================================
// FILE: IllusionController.cs (GODOT VERSION)
// PURPOSE: Controls an illusion object, handling disbelief saves and interactions.
// ATTACH TO: An Area3D node representing the illusion.
// =================================================================================================

public partial class IllusionController : Area3D
{
    public CreatureStats Caster { get; private set; }
    public Ability_SO SourceAbility { get; private set; }
    
    private HashSet<CreatureStats> disbelievers = new HashSet<CreatureStats>();
    private Aabb illusionBounds;

    public override void _Ready()
    {
        // Connect the signal for "OnTriggerEnter" logic
        // Godot 4 uses 'BodyEntered' for Area3D monitoring bodies (PhysicsBody3D/CharacterBody3D)
        BodyEntered += OnBodyEntered;
    }

    /// <summary>
    /// Initializes the illusion with all necessary data from the caster.
    /// </summary>
    public void Initialize(CreatureStats caster, Ability_SO sourceAbility, Vector3 dimensions)
    {
        this.Caster = caster;
        this.SourceAbility = sourceAbility;
        
        // Setup Collider
        // In Godot, we look for a CollisionShape3D child.
        // We assume the scene has one, or we create it dynamically.
        var colShape = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        if (colShape == null)
        {
            colShape = new CollisionShape3D();
            AddChild(colShape);
            colShape.Shape = new BoxShape3D();
        }

        if (colShape.Shape is BoxShape3D box)
        {
            box.Size = dimensions;
        }

        // Initialize Bounds
        // AABB center is relative to position if using VisualServer logic, 
        // but for high-level logic we define it in global space.
        illusionBounds = new Aabb(GlobalPosition - (dimensions / 2), dimensions);

        // The caster automatically "disbelieves" their own illusion.
        disbelievers.Add(caster);
        
        // Register with Manager immediately upon init
        IllusionManager.Instance?.Register(this);
    }

    public override void _ExitTree()
    {
        IllusionManager.Instance?.Unregister(this);
    }

    /// <summary>
    /// When a creature physically touches or moves into the illusion's space, they get a save.
    /// Replaces OnTriggerEnter.
    /// </summary>
    private void OnBodyEntered(Node3D body)
    {
        // In Godot, 'body' is the node that entered. We check if it's a Creature.
        // Assuming CreatureStats is attached to the root of the creature body or easily accessible.
        CreatureStats interactor = body as CreatureStats;
        if (interactor == null)
        {
            interactor = body.GetNodeOrNull<CreatureStats>("CreatureStats");
        }

        if (interactor != null && !disbelievers.Contains(interactor))
        {
            float resistanceBonus = IntelligenceGrowthRuntime.Service.ComputeManipulationResistanceBonus(interactor, ManipulationType.Illusion);
            if (GD.Randf() < resistanceBonus)
            {
                disbelievers.Add(interactor);
                GD.Print($"{interactor.Name} reads through the illusion by cognitive resistance ({resistanceBonus:P0}).");
                return;
            }

            GD.Print($"{interactor.Name} has interacted with the {SourceAbility.AbilityName}. They get a Will save to disbelieve.");
            CombatManager.ResolveIllusionDisbelief(this, interactor);
        }
    }

    public void AddDisbeliever(CreatureStats creature)
    {
        disbelievers.Add(creature);
    }

    public bool HasDisbelieved(CreatureStats creature)
    {
        return disbelievers.Contains(creature);
    }

    public Aabb GetBounds()
    {
        // Update bounds in case the illusion moves
        // Aabb position is the corner, not center.
        var size = illusionBounds.Size;
        illusionBounds.Position = GlobalPosition - (size / 2);
        return illusionBounds;
    }
}