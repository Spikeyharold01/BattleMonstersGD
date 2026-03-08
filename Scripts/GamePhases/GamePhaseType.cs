/// <summary>
/// The main mode the game is currently running.
///
/// This helps the manager decide which big game flow is active.
/// </summary>
public enum GamePhaseType
{
    /// <summary>
    /// Normal arena combat mode.
    /// </summary>
    Arena = 0,

    /// <summary>
    /// Travel/exploration mode.
    /// </summary>
    Travel = 1
}
