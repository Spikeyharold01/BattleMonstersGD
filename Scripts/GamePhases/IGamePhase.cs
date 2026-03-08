/// <summary>
/// A simple rulebook for any game phase.
///
/// If a class is a phase, it must tell us:
/// - what type it is,
/// - what to do when it starts,
/// - what to do when it ends,
/// - what to do each frame while active.
/// </summary>
public interface IGamePhase
{
    /// <summary>
    /// Which phase this object represents.
    /// </summary>
    GamePhaseType PhaseType { get; }

    /// <summary>
    /// Called when this phase becomes active.
    ///
    /// Typical result:
    /// - load/create needed objects,
    /// - place creatures,
    /// - connect events.
    /// </summary>
    void EnterPhase(GamePhaseContext context);

    /// <summary>
    /// Called right before this phase is turned off.
    ///
    /// Typical result:
    /// - disconnect events,
    /// - save/park creatures,
    /// - clean up temporary objects.
    /// </summary>
    void ExitPhase(GamePhaseContext context);

    /// <summary>
    /// Called every frame while this phase is active.
    /// </summary>
    void UpdatePhase(double delta, GamePhaseContext context);
}
