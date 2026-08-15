namespace CareerCompanion.Core.Services;

/// <summary>
/// Decides whether a scanned FIFA save belongs to the companion career that is already open.
///
/// The identity of a career is the FIFA player it follows. It is deliberately not the save's file name:
/// FIFA names every save after the moment it was written, so the name changes each time the player saves.
/// Treating the name as identity made every save look like a different career, which created a duplicate
/// companion career, reset the statistics baseline, and left each new match unable to work out its own
/// goals and assists because it had no earlier total to compare against.
/// </summary>
public static class CareerLinkResolver
{
    /// <param name="linkedPlayerId">The FIFA player id this career was linked to, empty when never linked.</param>
    /// <param name="sameIdentity">Whether the save's player name and the career's name describe one person.</param>
    /// <param name="sameClub">Whether the save and the career agree on the current club.</param>
    public static bool BelongsToCareer(string? linkedPlayerId, int savePlayerId, bool sameIdentity, bool sameClub)
        => string.IsNullOrWhiteSpace(linkedPlayerId)
            // Never linked, so the only evidence is who the save says the player is and where he plays.
            ? sameIdentity && sameClub
            // Once linked, the player id settles it. A transfer changes the club and a new save changes the
            // file name, and neither of those makes it a different career.
            : string.Equals(linkedPlayerId, savePlayerId.ToString(), StringComparison.Ordinal);
}
