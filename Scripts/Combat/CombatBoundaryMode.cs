/// <summary>
/// Defines how the active combat map border behaves.
///
/// Expected output:
/// - ArenaLocked means the encounter is a sealed battlefield where edge crossing is never allowed.
/// - TravelEscapable means the edge behaves like an exit lane so intentional disengagement can leave combat.
/// </summary>
public enum CombatBoundaryMode
{
    ArenaLocked,
    TravelEscapable
}
