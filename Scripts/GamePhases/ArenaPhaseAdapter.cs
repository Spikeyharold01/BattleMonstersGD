using Godot;
using System;
using System.Linq;

/// <summary>
/// Wraps the existing Arena setup so it can be treated like a phase.
///
/// Important:
/// - does not change arena combat logic,
/// - only handles phase-level enter/exit behavior.
/// </summary>
public sealed class ArenaPhaseAdapter : IGamePhase
{
    // Existing arena root in the scene.
    private readonly Node3D _arenaRoot;

    // Optional hooks to call existing enter/exit logic.
    private readonly Action _onEnter;
    private readonly Action _onExit;

    /// <summary>
    /// Create the adapter.
    /// </summary>
    public ArenaPhaseAdapter(Node3D arenaRoot, Action onEnter, Action onExit)
    {
        _arenaRoot = arenaRoot;
        _onEnter = onEnter;
        _onExit = onExit;
    }

    /// <summary>
    /// This adapter represents Arena.
    /// </summary>
    public GamePhaseType PhaseType => GamePhaseType.Arena;

    /// <summary>
    /// Turn Arena on:
    /// - restore creatures under arena root,
    /// - show arena root,
    /// - run optional enter callback.
    /// </summary>
     public void EnterPhase(GamePhaseContext context)
    {
        ArenaStartContext startContext = context?.ConsumeArenaStartContext();

        if (_arenaRoot != null)
        {
            context.CreaturePersistence.RestoreAllCreatures(_arenaRoot);
            
            UnpackArmiesForCombat(context, _arenaRoot);

            _arenaRoot.Visible = true;

            // Store the transition payload at the arena root so existing arena systems can read it
            // without adding hard dependencies between travel and combat layers.
            _arenaRoot.SetMeta("ArenaStartContext", startContext);
        }

        // Before Arena callbacks run, lock in combat-map dimensions from one shared formula.
        // Travel handoff applies travel clamp (max 1,200 ft). Native arena entry applies arena cap.
        CombatMapSizingService.ApplySizingToGrid(startContext?.IsTravelCombat == true);

        // Boundary policy is selected once at combat initialization based on source mode.
        CombatBoundaryService.InitializeForCombat(startContext?.IsTravelCombat == true);

        if (startContext?.IsSurpriseAttack == true)
        {
            GD.Print($"[ArenaStart] Surprise attack initiated by creature instance {startContext.SurpriseSourceCreatureId}.");
        }

        _onEnter?.Invoke();
    }

    /// <summary>
    /// Turn Arena off:
    /// - run optional exit callback,
    /// - hide arena root.
    /// </summary>
    public void ExitPhase(GamePhaseContext context)
    {
        _onExit?.Invoke();

        if (_arenaRoot != null)
        {
            _arenaRoot.Visible = false;
        }
    }

    /// <summary>
    /// No per-frame work here.
    /// Arena keeps running through existing systems.
    /// </summary>
    public void UpdatePhase(double delta, GamePhaseContext context)
    {
        // Arena logic remains fully owned by existing arena systems.
    }
	private void UnpackArmiesForCombat(GamePhaseContext context, Node3D arenaRoot)
    {
        var tree = arenaRoot.GetTree();
        if (tree == null) return;

        // --- UNPACK ENEMY TROOPS ---
        var enemies = tree.GetNodesInGroup("Enemy");
        int enemyIndex = 0;
        foreach (Node node in enemies)
        {
            if (node is CreatureStats enemy)
            {
                // Unparent them from the Troop Leader and attach to the Arena
                if (enemy.GetParent() != arenaRoot)
                {
                    Vector3 originalGlobalPos = enemy.GlobalPosition;
                    enemy.GetParent().RemoveChild(enemy);
                    arenaRoot.AddChild(enemy);
                    enemy.GlobalPosition = originalGlobalPos;
                }

                // Restore visibility and collision
                enemy.Visible = true;
                var col = enemy.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
                if (col != null) col.Disabled = false;

                // Spread them out using a spiral pattern so they don't overlap
                float angle = enemyIndex * 2.4f; 
                float radius = (Mathf.Sqrt(enemyIndex) * 5f); // 5ft spacing
                Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                
                enemy.GlobalPosition += offset;
                enemyIndex++;
            }
        }
        
        // --- UNPACK PLAYER ALLIES ---
        var allies = context.CreaturePersistence.PersistentCreatures.Where(c => c.IsInGroup("Ally") && !c.IsInGroup("Player")).ToList();
        var player = context.CreaturePersistence.PersistentCreatures.FirstOrDefault(c => c.IsInGroup("Player"));
        Vector3 playerPos = player != null ? player.GlobalPosition : Vector3.Zero;
        
        int allyIndex = 1;
        foreach(var ally in allies)
        {
            float angle = allyIndex * 2.4f; 
            float radius = 5f + (Mathf.Sqrt(allyIndex) * 5f);
            Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            
            ally.GlobalPosition = playerPos + offset;
            allyIndex++;
        }
    }
}
