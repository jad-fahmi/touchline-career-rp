using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;

namespace CareerCompanion.Core.Simulation;

public enum FactTone { Positive, Negative, Neutral }

/// <summary>One notable, verifiable thing about a match. Salience decides what a character talks about first.</summary>
public sealed record MatchFact(string Key, int Salience, FactTone Tone, string Detail = "");

/// <summary>
/// Everything factual a character could reasonably know about a match and the run of form around it.
/// Built from stored career data only, so offline dialogue and model prompts share the same grounding.
/// </summary>
public sealed record MatchNarrative(
    Career Career,
    CareerMatch Match,
    IReadOnlyList<MatchFact> Facts,
    IReadOnlyList<MatchPerformance> Squad,
    IReadOnlyList<string> Narratives,
    PlayerState Player,
    int GoalDrought,
    int StartDrought,
    int WinlessRun,
    int UnbeatenRun,
    string FormLine,
    double TeamAverageRating,
    int TeamRank,
    int SeasonGoals,
    int SeasonAppearances,
    int MatchesTogether)
{
    public MatchInput Input => Match.Input;
    public MatchFact Headline => Facts.Count > 0 ? Facts[0] : new("routine", 30, FactTone.Neutral);
    public bool Has(string key) => Facts.Any(x => x.Key == key);
    public MatchFact? Find(string key) => Facts.FirstOrDefault(x => x.Key == key);
    /// <summary>How much this match deserves to dominate a conversation, 0-100.</summary>
    public int Intensity => Headline.Salience;

    /// <summary>
    /// A compact, strictly factual account of the latest match and the run around it. Offline dialogue and
    /// model prompts are both grounded in this, so a character knows the same things either way.
    /// </summary>
    public string Brief()
    {
        var input = Input;
        var lines = new List<string>();
        var venue = input.IsHomeKnown ? input.IsHome ? "at home against" : "away at" : "against";
        var opponent = input.OpponentKnown ? input.Opponent : "an opponent nobody has named to you";
        var outcome = input.ScoreKnown
            ? Match.Result switch { "W" => $"won {input.ScoreLabel}", "L" => $"lost {input.ScoreLabel}", _ => $"drew {input.ScoreLabel}" }
            : "the final score is not known here, so do not state a scoreline";
        var tags = new List<string> { input.Competition };
        if (input.IsDerby) tags.Add("local rivalry");
        if (input.TeamContext == "International") tags.Add($"international for {input.RepresentingTeam}");
        lines.Add($"Latest match {input.Date}, {venue} {opponent} ({string.Join(", ", tags)}): {outcome}.");

        var own = new List<string>();
        own.Add(input.StartedKnown ? input.Started ? "started" : "came off the bench" : "appeared");
        if (input.Minutes > 0) own.Add($"{input.Minutes} minutes");
        if (input.Rating > 0) own.Add($"match rating {input.Rating:0.#}");
        own.Add(input.Goals == 0 ? "did not score" : input.Goals == 1 ? "scored once" : $"scored {input.Goals}");
        if (input.Assists > 0) own.Add(input.Assists == 1 ? "one assist" : $"{input.Assists} assists");
        if (input.RedCard) own.Add("was sent off");
        else if (input.YellowCard) own.Add("was booked");
        lines.Add($"{Career.PlayerName} {string.Join(", ", own)}.");

        if (Squad.Count > 0)
        {
            var best = Squad.Where(x => x.Rating > 0).OrderByDescending(x => x.Rating).Take(3)
                .Select(x => $"{x.Name} {x.Rating:0.#}");
            lines.Add($"Best rated team-mates: {string.Join(", ", best)}. Squad average {TeamAverageRating:0.#}.");
        }
        if (!string.IsNullOrWhiteSpace(FormLine)) lines.Add($"Recent results, oldest first: {FormLine}.");
        if (GoalDrought >= 4 && Input.Goals == 0) lines.Add($"{GoalDrought} matches without a goal before this one.");
        if (StartDrought >= 2 && !Input.Started) lines.Add($"{StartDrought + 1} matches without starting.");
        if (UnbeatenRun >= 4) lines.Add($"The team was unbeaten in {UnbeatenRun} before this match.");
        if (WinlessRun >= 3) lines.Add($"The team had not won in {WinlessRun} before this match.");
        lines.Add($"Season so far: {SeasonGoals} {(SeasonGoals==1?"goal":"goals")} in {SeasonAppearances} {(SeasonAppearances==1?"appearance":"appearances")}.");
        if (Narratives.Count > 0) lines.Add($"Ongoing storylines: {string.Join(", ", Narratives.Select(x => x.Replace('_', ' ').ToLowerInvariant()))}.");
        lines.Add($"Player wellbeing: confidence {Player.Confidence}, pressure {Player.Pressure}, fatigue {Player.Fatigue}, isolation {Player.Isolation}.");
        return string.Join(" ", lines);
    }
}

public sealed class MatchNarrativeBuilder(Database db)
{
    public MatchNarrative Build(Career career, CareerMatch match)
    {
        var history = db.GetMatches(career.Id, 200).Where(x => string.CompareOrdinal(x.Input.Date, match.Input.Date) <= 0 && x.Id != match.Id)
            .OrderBy(x => x.Input.Date, StringComparer.Ordinal).ThenBy(x => x.Id).ToList();
        var squad = db.GetMatchPerformances(match.Id);
        var player = db.GetPlayerState(career.Id);
        var narratives = ActiveNarratives(career.Id);
        var input = match.Input;

        var goalDrought = 0;
        foreach (var previous in Enumerable.Reverse(history)) { if (previous.Input.Goals > 0) break; goalDrought++; }
        var startDrought = 0;
        foreach (var previous in Enumerable.Reverse(history)) { if (!previous.Input.StartedKnown || previous.Input.Started) break; startDrought++; }
        var winless = 0;
        foreach (var previous in Enumerable.Reverse(history)) { if (previous.Result == "W") break; if (previous.Result == "U") continue; winless++; }
        var unbeaten = 0;
        foreach (var previous in Enumerable.Reverse(history)) { if (previous.Result == "L") break; if (previous.Result == "U") continue; unbeaten++; }
        var formLine = string.Join(" ", history.TakeLast(5).Select(x => x.Result == "U" ? "?" : x.Result));

        var rated = squad.Where(x => x.Minutes >= 20 && x.Rating > 0).ToList();
        var teamAverage = rated.Count == 0 ? 0 : Math.Round(rated.Average(x => x.Rating), 1);
        var teamRank = rated.Count == 0 ? 0 : rated.Count(x => x.Rating > input.Rating) + 1;
        var seasonGoals = history.Where(x => SameSeason(x, match)).Sum(x => x.Input.Goals) + input.Goals;
        var seasonApps = history.Count(x => SameSeason(x, match)) + 1;

        var facts = BuildFacts(career, match, goalDrought, startDrought, winless, unbeaten, rated, teamRank, teamAverage);
        return new(career, match, facts, squad, narratives, player, goalDrought, startDrought, winless, unbeaten,
            formLine, teamAverage, teamRank, seasonGoals, seasonApps, history.Count);
    }

    private static bool SameSeason(CareerMatch a, CareerMatch b)
        => DateTime.TryParse(a.Input.Date, out var left) && DateTime.TryParse(b.Input.Date, out var right)
           && SeasonYear(left) == SeasonYear(right);
    private static int SeasonYear(DateTime date) => date.Month >= 7 ? date.Year : date.Year - 1;

    private IReadOnlyList<string> ActiveNarratives(long careerId)
    {
        using var connection = db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type FROM narratives WHERE career_id=$career AND status='active' ORDER BY strength DESC LIMIT 4";
        command.Parameters.AddWithValue("$career", careerId);
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    private static IReadOnlyList<MatchFact> BuildFacts(Career career, CareerMatch match, int goalDrought, int startDrought,
        int winless, int unbeaten, IReadOnlyList<MatchPerformance> rated, int teamRank, double teamAverage)
    {
        var input = match.Input;
        var facts = new List<MatchFact>();
        void Add(string key, int salience, FactTone tone, string detail = "") => facts.Add(new(key, salience, tone, detail));

        if (input.RedCard) Add("red_card", 95, FactTone.Negative);
        if (input.Goals >= 3) Add("hattrick", 94, FactTone.Positive, $"{input.Goals} goals");
        else if (input.Goals == 2) Add("brace", 78, FactTone.Positive);
        else if (input.Goals == 1) Add("goal", 62, FactTone.Positive);
        if (input.Assists >= 2) Add("assists", 66, FactTone.Positive, $"{input.Assists} assists");
        else if (input.Assists == 1) Add("assist", 54, FactTone.Positive);
        if (input.PenaltyMissed) Add("penalty_missed", 82, FactTone.Negative);
        if (input.PenaltyScored) Add("penalty_scored", 60, FactTone.Positive);
        if (input.YellowCard && !input.RedCard) Add("booked", 34, FactTone.Neutral);

        if (!input.ScoreKnown) Add("unknown_score", 30, FactTone.Neutral);
        else
        {
            var margin = input.TeamScore - input.OpponentScore;
            if (input.IsDerby && margin > 0) Add("derby_win", 88, FactTone.Positive);
            else if (input.IsDerby && margin < 0) Add("derby_loss", 86, FactTone.Negative);
            else if (input.IsDerby) Add("derby_draw", 62, FactTone.Neutral);
            if (margin <= -3) Add("heavy_defeat", 78, FactTone.Negative, $"{-margin} goal margin");
            else if (margin >= 3) Add("big_win", 68, FactTone.Positive, $"{margin} goal margin");
            else if (margin > 0) Add("narrow_win", 50, FactTone.Positive);
            else if (margin < 0) Add("defeat", 54, FactTone.Negative);
            else Add("draw", 38, FactTone.Neutral);
        }

        if (input.Minutes >= 25)
        {
            if (input.Rating >= 8.5) Add("outstanding", 74, FactTone.Positive, $"rated {input.Rating:0.#}");
            else if (input.Rating >= 7.5) Add("strong_display", 52, FactTone.Positive, $"rated {input.Rating:0.#}");
            else if (input.Rating > 0 && input.Rating <= 5.5) Add("poor_display", 72, FactTone.Negative, $"rated {input.Rating:0.#}");
            else if (input.Rating > 0 && input.Rating <= 6.2) Add("flat_display", 46, FactTone.Negative, $"rated {input.Rating:0.#}");
        }
        if (rated.Count >= 6 && input.Rating > 0)
        {
            if (teamRank == 1) Add("team_best", 60, FactTone.Positive, "the best rated player in the side");
            else if (teamRank >= rated.Count) Add("team_worst", 58, FactTone.Negative);
            if (input.Rating >= teamAverage + 1.5) Add("above_the_team", 56, FactTone.Positive);
            else if (input.Rating <= teamAverage - 1.5) Add("below_the_team", 56, FactTone.Negative);
        }

        if (input.StartedKnown && !input.Started && input.Minutes > 0) Add("bench_cameo", 48, FactTone.Neutral, $"{input.Minutes} minutes off the bench");
        if (input.StartedKnown && !input.Started && startDrought >= 3) Add("bench_streak", 70, FactTone.Negative, $"{startDrought + 1} matches without starting");
        if (input.Goals > 0 && goalDrought >= 5) Add("drought_broken", 76, FactTone.Positive, $"first goal in {goalDrought + 1} matches");
        else if (input.Goals == 0 && goalDrought >= 6 && IsAttacker(career.Position)) Add("drought", 64, FactTone.Negative, $"{goalDrought + 1} matches without scoring");
        if (input.ScoreKnown && match.Result == "W" && winless >= 4) Add("run_ended", 66, FactTone.Positive, $"first win in {winless + 1} matches");
        if (input.ScoreKnown && match.Result == "L" && unbeaten >= 6) Add("run_broken", 58, FactTone.Negative, $"a {unbeaten} match unbeaten run ended");
        if (input.TeamContext == "International") Add("international", 70, FactTone.Neutral, input.RepresentingTeam);
        if (input.IsMajorFixture && !input.IsDerby) Add("major_fixture", 58, FactTone.Neutral, input.Competition);

        facts.Sort((a, b) => b.Salience.CompareTo(a.Salience));
        return facts;
    }

    private static bool IsAttacker(string position)
        => new[] { "ST", "CF", "LW", "RW", "LS", "RS", "LF", "RF", "CAM", "LAM", "RAM" }
            .Contains(position, StringComparer.OrdinalIgnoreCase);
}
