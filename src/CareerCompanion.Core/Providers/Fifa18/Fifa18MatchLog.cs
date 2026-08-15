namespace CareerCompanion.Core.Providers.Fifa18;

/// <summary>
/// One entry from the FIFA player match-rating history. FIFA writes a row for every player who
/// appeared in a club match, so the career player's rows form a complete, ordered appearance log.
/// Positions 28 (SUB) and 29 (RES) mean the player came off the bench.
/// </summary>
public sealed record Fifa18Appearance(int RatingKey, string Date, int Minutes, int Rating, int PositionCode)
{
    public bool Started => PositionCode is >= 0 and < 28;
    public string Position => Fifa18CareerNormalizer.PositionName(PositionCode);
}

/// <summary>How one squad member performed in a specific club match.</summary>
public sealed record Fifa18SquadPerformance(int PlayerId, string Name, string Position, int PositionCode,
    int Minutes, int Rating)
{
    public bool Started => PositionCode is >= 0 and < 28;
}

/// <summary>Every rated performance FIFA recorded for one club matchday.</summary>
public sealed record Fifa18MatchDay(string Date, int HighestRatingKey, IReadOnlyList<Fifa18SquadPerformance> Squad)
{
    public Fifa18SquadPerformance? For(int playerId) => Squad.FirstOrDefault(x => x.PlayerId == playerId);
}
