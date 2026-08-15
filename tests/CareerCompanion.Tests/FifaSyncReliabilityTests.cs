using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Providers.Fifa18;
using CareerCompanion.Core.Services;

namespace CareerCompanion.Tests;

/// <summary>
/// Covers what happens when the real world interferes: the same save scanned twice, Touchline restarting
/// part-way through a batch, two careers on one machine, and scores that only arrive later.
/// </summary>
public sealed class FifaSyncReliabilityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "touchline-sync-" + Guid.NewGuid().ToString("N"));
    private Database NewDb() { var db = new Database(Path.Combine(_dir, "world.db")); db.Migrate(); return db; }

    private static Fifa18DetectedMatch Detected(string date, int key, string opponent = "Real Madrid",
        bool scoreKnown = true, int teamScore = 2, int opponentScore = 1, int goals = 1, bool review = false)
        => new($"p1:rating:{key}:date:{date.Replace("-", "")}", date, "League", opponent, true, teamScore, opponentScore,
            true, 90, goals, 0, 8, false, false, review ? 60 : 95, "evidence", review, true, "Club", "Club FC",
            scoreKnown, false, 243, [new(900, "Mate", "CM", 5, 90, 8)], true, key);

    private static Fifa18ParsedCareer Parsed(IReadOnlyList<Fifa18DetectedMatch> matches, string careerDate = "2017-09-10",
        int goals = 1, int appearances = 1, string fingerprint = "fp-1",
        IReadOnlyList<Fifa18ResolvedResult>? resolved = null)
        => new("C:/save/Career1", fingerprint, new DateTime(2017, 9, 10), "Alex Player", 1, 54, "Brazil", 21,
            "Club FC", 241, 53, "League", "2017/18", careerDate, "ST", 9,
            new(1, 241, int.Parse(careerDate.Replace("-", "")), 1, appearances, goals, 0, 0, 0,
                matches.Count > 0 ? matches[^1].AppearanceKey : 0, 20170910),
            matches.Count > 0 ? matches[^1] : null, [], null, 1, [], 80, 5, false, "Boss", "Agent", [],
            false, -1, "", null, "Unknown", null, matches, [], 0, null, resolved);

    private static long Career(Database db)
    {
        var id = db.CreateCareer("Save", "Alex Player", "Brazil", 21, "Club FC", "League", "2017/18", "ST", 9, "2017-09-01");
        db.AddCharacter(id, "Boss", 50, "", "Club FC", "Manager", "Manager", CharacterType.Manager);
        db.AddCharacter(id, "Mate", 25, "", "Club FC", "CM", "Starter", CharacterType.Teammate);
        return id;
    }

    [Fact]
    public void Scanning_the_same_save_twice_imports_one_match()
    {
        var db = NewDb();
        var career = Career(db);
        var sync = new FifaSyncService(db);
        var parsed = Parsed([Detected("2017-09-09", 50)]);

        var first = sync.Apply(career, parsed);
        var second = sync.Apply(career, parsed);

        Assert.Single(first.Imported);
        Assert.Empty(second.Imported);
        Assert.Single(db.GetMatches(career));
        Assert.Equal(Fifa18ScanDisposition.NoNewMatch, second.Disposition);
    }

    [Fact]
    public void A_batch_of_missed_matches_imports_in_chronological_order()
    {
        var db = NewDb();
        var career = Career(db);
        var matches = new[] { Detected("2017-09-02", 20, review: true), Detected("2017-09-09", 40, review: true) };
        // Two appearances at once means the season totals cannot be split, so both come through for a check.
        var outcome = new FifaSyncService(db).Apply(career, Parsed(matches, appearances: 2, goals: 2));
        Assert.Equal(2, outcome.NeedsReview.Count);
        Assert.Equal("2017-09-02", outcome.NeedsReview[0].Date);

        var confirmed = new[] { Detected("2017-09-02", 20), Detected("2017-09-09", 40) };
        var second = new FifaSyncService(db).Apply(career, Parsed(confirmed, appearances: 2, goals: 2, fingerprint: "fp-2"));
        Assert.Equal(2, second.Imported.Count);
        var stored = db.GetMatches(career);
        Assert.Equal(["2017-09-02", "2017-09-09"], stored.Select(x => x.Input.Date));
    }

    [Fact]
    public void An_interrupted_batch_resumes_at_the_match_it_did_not_reach()
    {
        var db = NewDb();
        var career = Career(db);
        var sync = new FifaSyncService(db);
        var snapshot = Parsed([Detected("2017-09-02", 20), Detected("2017-09-09", 40)], appearances: 2, goals: 2);

        // Touchline is closed after the first match of the batch is written.
        sync.ImportDetectedMatch(career, snapshot, snapshot.PendingMatches[0]);
        var payload = db.GetLatestProviderPayload(career, FifaSyncService.ProviderName);
        var baseline = System.Text.Json.JsonSerializer.Deserialize<Fifa18SyncState>(payload!);

        Assert.Equal(20, baseline!.LatestRatingKey);
        Assert.Equal(20170902, baseline.LatestRatingDate);

        var resumed = sync.Apply(career, snapshot);
        Assert.Single(resumed.Imported);
        Assert.Equal("2017-09-09", resumed.Imported[0].Date);
        Assert.Equal(2, db.GetMatches(career).Count);
    }

    [Fact]
    public void A_dismissed_match_is_never_offered_again()
    {
        var db = NewDb();
        var career = Career(db);
        var sync = new FifaSyncService(db);
        var parsed = Parsed([Detected("2017-09-09", 50, review: true)], appearances: 2);
        sync.Apply(career, parsed);
        db.SetMatchReviewStatus(career, FifaSyncService.ProviderName, parsed.PendingMatches[0].EventKey, "Dismissed");

        var second = sync.Apply(career, parsed);
        Assert.Empty(second.Imported);
        Assert.Empty(second.NeedsReview);
        Assert.Empty(db.GetMatches(career));
    }

    [Fact]
    public void Two_careers_on_one_machine_keep_separate_baselines()
    {
        var db = NewDb();
        var first = Career(db);
        var second = Career(db);
        var sync = new FifaSyncService(db);
        var parsed = Parsed([Detected("2017-09-09", 50)]);

        sync.Apply(first, parsed);
        var outcome = sync.Apply(second, parsed);

        Assert.Single(outcome.Imported);
        Assert.Single(db.GetMatches(first));
        Assert.Single(db.GetMatches(second));
    }

    [Fact]
    public void A_score_published_later_is_filled_in_without_duplicating_the_match()
    {
        var db = NewDb();
        var career = Career(db);
        var sync = new FifaSyncService(db);
        sync.Apply(career, Parsed([Detected("2017-09-09", 50, scoreKnown: false, teamScore: 0, opponentScore: 0)]));
        var match = Assert.Single(db.GetMatches(career));
        Assert.False(match.Input.ScoreKnown);

        var withScore = Parsed([], careerDate: "2017-09-12", fingerprint: "fp-2",
            resolved: [new("2017-09-09", "Real Madrid", 3, 1, "FIFA report")]);
        var outcome = sync.Apply(career, withScore);

        Assert.Equal(1, outcome.ConfirmedScores);
        var updated = Assert.Single(db.GetMatches(career));
        Assert.True(updated.Input.ScoreKnown);
        Assert.Equal(3, updated.Input.TeamScore);
        Assert.Equal("W", updated.Result);
        Assert.Equal(0, sync.Apply(career, withScore).ConfirmedScores);
    }

    [Fact]
    public void Squad_performances_are_stored_with_the_imported_match()
    {
        var db = NewDb();
        var career = Career(db);
        new FifaSyncService(db).Apply(career, Parsed([Detected("2017-09-09", 50)]));
        var match = Assert.Single(db.GetMatches(career));
        var performances = db.GetMatchPerformances(match.Id);
        Assert.Single(performances);
        Assert.Equal("Mate", performances[0].Name);
        Assert.True(performances[0].Started);
    }

    [Fact]
    public void Repeated_scans_do_not_multiply_events_or_notifications()
    {
        var db = NewDb();
        var career = Career(db);
        var sync = new FifaSyncService(db);
        var parsed = Parsed([Detected("2017-09-09", 50)]);
        for (var i = 0; i < 4; i++) sync.Apply(career, parsed);
        var events = db.GetEvents(career, 500).Where(x => x.MatchId is not null).ToList();
        Assert.Equal(events.Select(x => x.Id).Distinct().Count(), events.Count);
        Assert.Single(db.GetMatches(career));
        var notifications = db.GetNotifications(career, 500);
        Assert.Equal(notifications.Select(x => x.DedupeKey).Distinct().Count(), notifications.Count);
    }

    [Fact]
    public void Being_left_out_of_the_squad_reaches_the_career_world()
    {
        var db = NewDb();
        var career = Career(db);
        var parsed = Parsed([]) with { MissedClubMatches = 2 };
        new FifaSyncService(db).Apply(career, parsed);
        Assert.Contains(db.GetEvents(career), x => x.Type == "PLAYER_NOT_SELECTED");
        Assert.Contains(db.GetNotifications(career), x => x.Kind == "Selection");
    }

    // FIFA keeps only about twenty news articles, and that article is the only record of who a played match
    // was against. A fixture stored on an earlier scan outlives it, so it can name an opponent the save can
    // no longer prove.
    private static void StoreFixture(Database db, long career, string date, string opponent, string eventKey)
        => db.UpsertFixture(career, FifaSyncService.ProviderName, eventKey, date, "League", opponent, true, 80,
            "FIFA preview named the fixture", "fp-fixture");

    [Fact]
    public void An_opponent_the_news_no_longer_names_is_recovered_from_an_earlier_fixture()
    {
        var db = NewDb();
        var career = Career(db);
        StoreFixture(db, career, "2017-09-09", "Cagliari", "fixture-1");

        var outcome = new FifaSyncService(db).Apply(career,
            Parsed([Detected("2017-09-09", 50, opponent: "Opponent unknown", review: true)]));

        var reviewed = Assert.Single(outcome.NeedsReview);
        Assert.Equal("Cagliari", reviewed.Opponent);
        Assert.Contains("earlier scan", reviewed.Evidence);
        // Recovered, not proven: it still goes to the player rather than importing itself.
        Assert.True(reviewed.Confidence < 80);
        Assert.Empty(outcome.Imported);
    }

    [Fact]
    public void Two_fixtures_on_one_date_leave_the_opponent_unknown()
    {
        var db = NewDb();
        var career = Career(db);
        StoreFixture(db, career, "2017-09-09", "Cagliari", "fixture-1");
        StoreFixture(db, career, "2017-09-09", "Torino", "fixture-2");

        var outcome = new FifaSyncService(db).Apply(career,
            Parsed([Detected("2017-09-09", 50, opponent: "Opponent unknown", review: true)]));

        Assert.Equal("Opponent unknown", Assert.Single(outcome.NeedsReview).Opponent);
    }

    [Fact]
    public void A_fixture_never_overrides_an_opponent_the_save_confirmed()
    {
        var db = NewDb();
        var career = Career(db);
        StoreFixture(db, career, "2017-09-09", "Cagliari", "fixture-1");

        var outcome = new FifaSyncService(db).Apply(career, Parsed([Detected("2017-09-09", 50, opponent: "Torino")]));

        Assert.Equal("Torino", Assert.Single(outcome.Imported).Opponent);
    }

    [Fact]
    public void A_fixture_already_used_by_an_imported_match_is_not_reused()
    {
        var db = NewDb();
        var career = Career(db);
        StoreFixture(db, career, "2017-09-09", "Cagliari", "fixture-1");
        var sync = new FifaSyncService(db);
        sync.Apply(career, Parsed([Detected("2017-09-09", 50, opponent: "Cagliari")]));

        var later = sync.Apply(career, Parsed([Detected("2017-09-09", 51, opponent: "Opponent unknown", review: true)],
            appearances: 2, goals: 2, fingerprint: "fp-3"));

        Assert.Equal("Opponent unknown", Assert.Single(later.NeedsReview).Opponent);
    }

    public void Dispose() { try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (IOException) { } }
}
