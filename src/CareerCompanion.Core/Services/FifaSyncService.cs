using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Providers.Fifa18;
using System.Text.Json;

namespace CareerCompanion.Core.Services;

public sealed record FifaSyncOptions(bool SyncSquad = true, bool AutoTeammates = true, bool AutoManager = true,
    bool AutoNews = true, bool AutoSocial = true, bool AutoPress = true)
{
    public static FifaSyncOptions Default { get; } = new();
}

public sealed record FifaSyncOutcome(Fifa18ScanDisposition Disposition, IReadOnlyList<Fifa18DetectedMatch> Imported,
    IReadOnlyList<Fifa18DetectedMatch> NeedsReview, int ConfirmedScores, string SupportingSummary, string Message);

/// <summary>
/// Applies a parsed FIFA save to a career: progression, supporting facts, and every appearance detected
/// since the last import. Matches import on their own whenever the save proves what happened, so a normal
/// session needs no action from the player at all.
/// </summary>
public sealed class FifaSyncService(Database db, IFifa18LiveMatchSource? liveMatches = null)
{
    public const string ProviderName = Fifa18ImportService.ProviderName;

    /// <summary>Consulted only when the save cannot name an opponent, and harmless when FIFA is closed.</summary>
    private readonly IFifa18LiveMatchSource _live = liveMatches ?? new Fifa18LiveMatchReader();

    public FifaSyncOutcome Apply(long careerId, Fifa18ParsedCareer parsed, FifaSyncOptions? options = null)
    {
        options ??= FifaSyncOptions.Default;
        var previousProgress = db.GetLatestCareerProgressSnapshot(careerId);
        db.SetSetting($"career:{careerId}:fifa_player_id", parsed.PlayerId.ToString());
        db.UpdateCareerFromProvider(careerId, parsed.PlayerName, parsed.NationalityName, parsed.Age, parsed.ClubName,
            parsed.LeagueName, parsed.Season, parsed.CurrentDate, parsed.Position, parsed.ShirtNumber);
        var injured = parsed.PlayerInjuryKnown ? parsed.PlayerInjured : previousProgress?.Injured ?? false;
        var progress = new CareerProgressSnapshot(0, careerId, parsed.CapturedAt, parsed.CurrentDate, parsed.ClubName,
            parsed.LeagueName, parsed.Position, parsed.ShirtNumber, parsed.PlayerOverall, parsed.PlayerForm, injured,
            parsed.State.Appearances, parsed.State.Goals, parsed.State.Assists, parsed.State.YellowCards,
            parsed.State.RedCards, parsed.FileFingerprint);
        new AutomaticWorldService(db).ApplyProgress(careerId, progress, previousProgress);
        var supporting = new Fifa18ImportService(db).SyncSupportingFacts(careerId, parsed, options.SyncSquad,
            options.AutoTeammates, options.AutoManager);
        var supportingSummary = Describe(supporting, parsed);
        var confirmed = ConfirmLateScores(careerId, parsed);
        ReportMissedMatches(careerId, parsed);

        var imported = new List<Fifa18DetectedMatch>();
        var needsReview = new List<Fifa18DetectedMatch>();
        foreach (var pending in parsed.PendingMatches)
        {
            // Cheapest evidence first: a fixture already in the database, then the running game.
            var detected = ResolveOpponentFromLiveGame(parsed, ResolveOpponentFromFixture(careerId, pending));
            if (db.HasProviderImport(careerId, ProviderName, detected.EventKey))
            {
                db.SetMatchReviewStatus(careerId, ProviderName, detected.EventKey, "Imported");
                db.MarkNotificationRead(careerId, $"review:{detected.EventKey}");
                continue;
            }
            if (db.GetMatchReviewStatus(careerId, ProviderName, detected.EventKey) == "Dismissed") continue;
            db.StageMatchReview(careerId, ProviderName, detected.EventKey, parsed.SourcePath, parsed.FileFingerprint,
                parsed.CapturedAt, JsonSerializer.Serialize(detected),
                JsonSerializer.Serialize(parsed with { NewMatches = [detected], LatestMatch = detected }));
            if (CanImportWithoutReview(detected)) { ImportDetectedMatch(careerId, parsed, detected, options); imported.Add(detected); }
            else
            {
                needsReview.Add(detected);
                db.AddNotification(careerId, "Review",
                    detected.TeamContext == "International" ? "International appearance detected" : "Match needs a quick check",
                    $"{Describe(detected)} could not be confirmed automatically. Open the review to finish it.",
                    "Review", 85, $"review:{detected.EventKey}",
                    DateTime.TryParse(detected.Date, out var staged) ? staged : parsed.CapturedAt);
            }
        }

        var disposition = needsReview.Count > 0 ? Fifa18ScanDisposition.MatchDetected
            : imported.Count > 0 ? Fifa18ScanDisposition.MatchAutoImported
            : Fifa18ScanDisposition.NoNewMatch;
        var message = BuildMessage(imported, needsReview, confirmed, supportingSummary);
        return new(disposition, imported, needsReview, confirmed, supportingSummary, message);
    }

    /// <summary>
    /// Recovers an opponent the save can no longer prove. FIFA keeps only about twenty news articles at a
    /// time, and that article is the only place a played match's opponent is ever written, so by the time an
    /// appearance is detected the evidence has often been overwritten. A fixture stored on an earlier scan is
    /// the same save fact, captured while it still existed, so an appearance on exactly that date can borrow
    /// its opponent. Nothing else is borrowed: the fixtures table does not record whether FIFA had confirmed
    /// the venue, so home or away stays unknown, and the match still goes to review for the player to confirm.
    /// </summary>
    private Fifa18DetectedMatch ResolveOpponentFromFixture(long careerId, Fifa18DetectedMatch detected)
    {
        if (detected.OpponentKnown || detected.TeamContext != "Club") return detected;
        var candidates = db.GetFixtures(careerId, 100)
            .Where(x => x.Provider == ProviderName && x.Date == detected.Date && x.TeamContext == "Club"
                && x.Status != "Completed" && !string.IsNullOrWhiteSpace(x.Opponent)
                && !x.Opponent.Equals("Opponent unknown", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(x => x.Opponent, StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Two different fixtures recorded for one date cannot both be the match that was played, and
        // guessing between them would put an invented opponent into the career.
        if (candidates.Count != 1) return detected;
        var fixture = candidates[0];
        return detected with
        {
            Opponent = fixture.Opponent,
            Competition = string.IsNullOrWhiteSpace(detected.Competition) || detected.Competition == "Career match"
                ? fixture.Competition : detected.Competition,
            // Deliberately below the auto-import bar. An opponent carried over from an earlier scan is good
            // evidence, not proof that this appearance and that fixture are the same match.
            Confidence = Math.Clamp(detected.Confidence + 6, 0, 79),
            RequiresReview = true,
            Evidence = $"{detected.Evidence} Opponent taken from the fixture recorded for {fixture.Date} against {fixture.Opponent}, captured on an earlier scan before FIFA replaced the news for it."
        };
    }

    /// <summary>
    /// Asks the running game who the match was against. FIFA generates the fixture list at load and never
    /// writes it to the save, so when the news article naming the opponent has already been discarded, the
    /// live process holds the only remaining record of it. What comes back is the game's own account of the
    /// match, not a guess, so the match can import on its own instead of waiting for the player.
    ///
    /// The lookup only runs when nothing else could name the opponent, and returns nothing when FIFA is
    /// closed, which leaves the save-only behaviour exactly as it was.
    /// </summary>
    private Fifa18DetectedMatch ResolveOpponentFromLiveGame(Fifa18ParsedCareer parsed, Fifa18DetectedMatch detected)
    {
        if (detected.OpponentKnown || detected.TeamContext != "Club" || parsed.ClubTeamId <= 0) return detected;
        if (_live.FindMatch(parsed.ClubTeamId, detected.Date) is not { } live) return detected;
        var opponent = parsed.TeamNames?.GetValueOrDefault(live.OpponentTeamId, "") ?? "";
        if (string.IsNullOrWhiteSpace(opponent)) return detected;
        return detected with
        {
            Opponent = opponent,
            OpponentTeamId = live.OpponentTeamId,
            // A score the save already published stays authoritative; one it never published is filled in.
            TeamScore = detected.ScoreKnown ? detected.TeamScore : live.TeamScore,
            OpponentScore = detected.ScoreKnown ? detected.OpponentScore : live.OpponentScore,
            ScoreKnown = true,
            Confidence = Math.Max(detected.Confidence, 88),
            // Anything else left unresolved, such as season totals that cannot be split between two
            // appearances, still needs a look. Only the opponent has been settled here.
            RequiresReview = detected.Confidence < 70,
            Evidence = $"{detected.Evidence} Opponent and score read from the running game's record of the match on {detected.Date} ({live.TeamScore}-{live.OpponentScore})."
        };
    }

    private static string BuildMessage(IReadOnlyList<Fifa18DetectedMatch> imported,
        IReadOnlyList<Fifa18DetectedMatch> needsReview, int confirmed, string supporting)
    {
        var scores = confirmed > 0 ? $" {confirmed} final score{(confirmed == 1 ? "" : "s")} filled in." : "";
        if (imported.Count > 0 && needsReview.Count == 0)
            return (imported.Count == 1
                ? $"Match imported automatically: {Describe(imported[0])}."
                : $"{imported.Count} matches imported automatically, up to {Describe(imported[^1])}.") + scores;
        if (imported.Count > 0)
            return $"{imported.Count} match{(imported.Count == 1 ? "" : "es")} imported automatically. {needsReview.Count} still need{(needsReview.Count == 1 ? "s" : "")} a quick check.{scores}";
        if (needsReview.Count > 0)
            return $"{Describe(needsReview[^1])} needs a quick check before it joins the career.{scores}";
        return $"Save synchronized{supporting}; no new appearance to import.{scores}";
    }

    public static string Describe(Fifa18DetectedMatch match)
        => $"{match.Date}, {(match.IsHomeKnown ? match.IsHome ? "vs" : "away at" : "against")} {match.Opponent}, {match.ScoreLabel}";

    /// <summary>
    /// A match only needs the player when FIFA genuinely left something ambiguous. The opponent and the
    /// player's own line must be certain; a scoreline FIFA has not published yet is filled in later.
    /// </summary>
    public static bool CanImportWithoutReview(Fifa18DetectedMatch match)
        => !match.RequiresReview && match.OpponentKnown && match.Confidence >= 80;

    /// <summary>
    /// Imports a detected match. The provider baseline is stored per appearance so an interrupted batch
    /// resumes at the right place instead of skipping matches that had not been written yet.
    /// </summary>
    public void ImportDetectedMatch(long careerId, Fifa18ParsedCareer snapshot, Fifa18DetectedMatch detected,
        FifaSyncOptions? options = null)
    {
        options ??= FifaSyncOptions.Default;
        var input = detected.ToMatchInput() with
        {
            IsMajorFixture = detected.IsDerby || detected.TeamContext == "International" && IsMajorCompetition(detected.Competition)
        };
        var result = new CareerService(db).ProcessMatch(careerId, input, ProviderName, detected.EventKey);
        SavePerformances(careerId, result.Match.Id, snapshot.PlayerId, detected);
        db.CompleteMatchingFixture(careerId, input.Date, input.Opponent);
        var media = options.AutoNews || options.AutoSocial
            ? new MediaService(db).GenerateDeterministic(careerId, result.Events, options.AutoNews, options.AutoSocial)
            : new MediaGenerationResult(0, 0);
        var interview = new PostMatchInterviewService(db).CreateIfEligible(careerId, result, options.AutoPress);
        new AutomaticWorldService(db).ApplyMatch(result, options.AutoTeammates, options.AutoManager,
            interview is not null, media.NewsItems > 0, media.SocialPosts > 0);
        var baseline = detected.TeamContext == "Club" && detected.AppearanceKey >= 0
            ? snapshot.State with { LatestRatingKey = detected.AppearanceKey, LatestRatingDate = NumericDate(detected.Date, snapshot.State.LatestRatingDate) }
            : snapshot.State;
        db.RecordProviderImport(careerId, ProviderName, detected.EventKey, snapshot.SourcePath, snapshot.FileFingerprint,
            snapshot.CapturedAt, JsonSerializer.Serialize(baseline));
        db.CompleteProviderMatch(careerId, ProviderName, detected.EventKey);
        db.SetMatchReviewStatus(careerId, ProviderName, detected.EventKey, "Imported");
        db.MarkNotificationRead(careerId, $"review:{detected.EventKey}");
    }

    public void SavePerformances(long careerId, long matchId, int careerPlayerId, Fifa18DetectedMatch detected)
    {
        if (detected.TeamPerformances is not { Count: > 0 } performances) return;
        db.SaveMatchPerformances(careerId, matchId, ProviderName, performances
            .Where(x => x.PlayerId != careerPlayerId)
            .Select(x => new MatchPerformance(x.PlayerId.ToString(), x.Name, x.Position, x.Started, x.Minutes, x.Rating)));
    }

    /// <summary>Fills in scores FIFA published after a match was already imported, without regenerating its world.</summary>
    public int ConfirmLateScores(long careerId, Fifa18ParsedCareer parsed)
    {
        if (parsed.ResolvedResults is not { Count: > 0 } results) return 0;
        var confirmed = 0;
        foreach (var match in db.GetUnscoredProviderMatches(careerId))
        {
            var result = results.FirstOrDefault(x => x.Date == match.Input.Date &&
                (string.Equals(x.Opponent, match.Input.Opponent, StringComparison.OrdinalIgnoreCase)
                 || match.Input.Opponent.Equals("Opponent unknown", StringComparison.OrdinalIgnoreCase)));
            if (result is null || !db.ConfirmMatchScore(careerId, match.Id, result.TeamScore, result.OpponentScore)) continue;
            var timestamp = DateTime.TryParse(match.Input.Date, out var date) ? date : parsed.CapturedAt;
            // Event summaries are read back by characters, so the wording stays inside the football world.
            var summary = $"The final score against {result.Opponent} was confirmed: {result.TeamScore}-{result.OpponentScore}.";
            db.SaveEvent(new(0, careerId, match.Id, "MATCH_SCORE_CONFIRMED", timestamp, 35, "[]",
                JsonSerializer.Serialize(new { result.Evidence }), summary, FactClassification.SaveFact));
            db.AddNotification(careerId, "Result", "Final score confirmed", summary, "Matches", 45,
                $"score-confirmed:{match.Id}", timestamp);
            confirmed++;
        }
        return confirmed;
    }

    /// <summary>Being left out is part of the story, so the world hears about it once per scan that shows it.</summary>
    private void ReportMissedMatches(long careerId, Fifa18ParsedCareer parsed)
    {
        if (parsed.MissedClubMatches <= 0) return;
        var timestamp = DateTime.TryParse(parsed.CurrentDate, out var date) ? date : parsed.CapturedAt;
        var summary = parsed.MissedClubMatches == 1
            ? $"{parsed.ClubName} played a match without {parsed.PlayerName} in the matchday squad."
            : $"{parsed.ClubName} played {parsed.MissedClubMatches} matches without {parsed.PlayerName} in the matchday squad.";
        var key = $"missed-matches:{parsed.FileFingerprint}";
        db.SaveEvent(new(0, careerId, null, "PLAYER_NOT_SELECTED", timestamp,
            parsed.MissedClubMatches >= 3 ? 72 : 55, "[]",
            JsonSerializer.Serialize(new { parsed.MissedClubMatches, parsed.FileFingerprint }), summary,
            FactClassification.SaveFact));
        db.AddNotification(careerId, "Selection", "Left out of the squad", summary, "Timeline",
            parsed.MissedClubMatches >= 3 ? 74 : 58, key, timestamp);
    }

    private static string Describe(Fifa18SupportingSyncResult result, Fifa18ParsedCareer parsed)
    {
        var parts = new List<string>();
        if (result.Squad is { } squad) parts.Add($"{parsed.Squad.Count} teammates synced ({squad.Added} new, {squad.MarkedInactive} departed)");
        if (result.FixtureUpdated && parsed.NextFixture is { } fixture) parts.Add($"next fixture: {(fixture.IsHome ? "vs" : "at")} {fixture.Opponent}");
        return parts.Count == 0 ? "" : " | " + string.Join(" | ", parts);
    }

    private static int NumericDate(string date, int fallback)
        => DateTime.TryParse(date, out var value) ? int.Parse(value.ToString("yyyyMMdd")) : fallback;
    private static bool IsMajorCompetition(string competition)
        => new[] { "World Cup", "WC ", "Euro", "Final", "Knockout" }.Any(x => competition.Contains(x, StringComparison.OrdinalIgnoreCase));
}
