using Godot;
using System;

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
}
