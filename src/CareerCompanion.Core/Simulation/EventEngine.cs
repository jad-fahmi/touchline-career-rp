using CareerCompanion.Core.Domain;

namespace CareerCompanion.Core.Simulation;

public sealed class EventEngine
{
    public IReadOnlyList<CareerEvent> Detect(long careerId, long matchId, MatchInput match,
        IReadOnlyList<CareerMatch> previousMatches)
    {
        var now = DateTime.TryParse(match.Date, out var parsed) ? parsed : DateTime.UtcNow;
        var items = new List<CareerEvent>();
        void Add(string type, int importance, string summary, object? meta = null) => items.Add(new(
            0, careerId, matchId, type, now, Math.Clamp(importance, 1, 100), "[]",
            System.Text.Json.JsonSerializer.Serialize(meta ?? new { }), summary));

        var result = match.TeamScore.CompareTo(match.OpponentScore);
        Add(result > 0 ? "MATCH_WON" : result < 0 ? "MATCH_LOST" : "MATCH_DRAWN",
            BaseImportance(match), $"{match.Competition}: {match.RepresentingTeam} {(match.IsHome ? "vs" : "at")} {match.Opponent}, {match.TeamScore}-{match.OpponentScore}.");
        if(match.TeamContext=="International")
        {
            var debut=!previousMatches.Any(x=>x.Input.TeamContext=="International");
            Add(debut?"INTERNATIONAL_DEBUT":"INTERNATIONAL_APPEARANCE",debut?88:48,debut?$"Made a senior international debut for {match.RepresentingTeam} against {match.Opponent}.":$"Represented {match.RepresentingTeam} against {match.Opponent}.");
            if(match.Goals>0)Add("INTERNATIONAL_GOAL",Math.Min(95,65+match.Goals*8),$"Scored {match.Goals} international goal(s) for {match.RepresentingTeam}.");
        }
        if (match.Goals == 1) Add("PLAYER_SCORED", 35, $"Scored against {match.Opponent}.");
        if (match.Goals == 2) Add("PLAYER_BRACE", 58, $"Scored twice against {match.Opponent}.");
        if (match.Goals >= 3) Add("PLAYER_HATTRICK", 82 + Math.Min(10, match.Goals - 3), $"Scored {match.Goals} goals against {match.Opponent}.");
        if (match.Assists > 0) Add("PLAYER_ASSISTED", 28 + match.Assists * 8, $"Provided {match.Assists} assist(s).");
        if (match.Rating >= 9) Add("PLAYER_HIGH_RATING", 55, $"Earned a {match.Rating:0.0} rating.");
        if (match.RedCard) Add("PLAYER_RED_CARD", 75, "Was sent off.");
        if (match.YellowCard && !match.RedCard) Add("PLAYER_YELLOW_CARD", 16, "Was booked.");
        if (match.StartedKnown) { if (match.Started) Add("PLAYER_STARTED", 8, "Started the match."); else Add("PLAYER_BENCHED", 25, "Began the match on the bench."); }
        if (match.PenaltyMissed) Add("PLAYER_MISSED_PENALTY", 55, "Missed a penalty.");
        if (match.IsDerby) Add("RIVAL_MATCH", 30 + BaseImportance(match), $"Played a rivalry match against {match.Opponent}.");
        if (result < 0 && match.OpponentScore - match.TeamScore >= 3) Add("LARGE_DEFEAT", 78, "Suffered a heavy defeat.");
        if (result > 0 && match.Notes.Contains("late", StringComparison.OrdinalIgnoreCase)) Add("LATE_WINNER", 85, "Won with a late goal.");

        var outcomes = previousMatches.TakeLast(4).Select(x => x.Result).Append(result > 0 ? "W" : result < 0 ? "L" : "D").ToArray();
        var wins = outcomes.Reverse().TakeWhile(x => x == "W").Count();
        var losses = outcomes.Reverse().TakeWhile(x => x == "L").Count();
        if (wins >= 3) Add("WINNING_STREAK", 40 + wins * 5, $"Extended the winning streak to {wins} matches.", new { length = wins });
        if (losses >= 3) Add("LOSING_STREAK", 55 + losses * 5, $"The losing streak reached {losses} matches.", new { length = losses });
        return items.OrderByDescending(x => x.Importance).ToList();
    }

    private static int BaseImportance(MatchInput m) => Math.Clamp(25 + (m.IsDerby ? 20 : 0) +
        (m.IsMajorFixture ? 25 : 0) + (m.TeamContext=="International"?15:0) +
        (m.Competition.Contains("World Cup",StringComparison.OrdinalIgnoreCase)||m.Competition.Contains("Euro",StringComparison.OrdinalIgnoreCase)?15:0) +
        (m.Competition.Contains("Final", StringComparison.OrdinalIgnoreCase) ? 30 : 0), 10, 95);
}
