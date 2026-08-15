using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using Microsoft.Data.Sqlite;
using CareerCompanion.Core.Services;

namespace CareerCompanion.Tests;

/// <summary>
/// Existing careers must survive an upgrade. These build a database shaped like an older release and
/// check that migrating it keeps every row and fills the new columns with safe defaults.
/// </summary>
public sealed class MigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "touchline-migration-" + Guid.NewGuid().ToString("N"));

    private string NewPath() { Directory.CreateDirectory(_dir); return Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db"); }

    [Fact]
    public void Migrating_twice_changes_nothing()
    {
        var path = NewPath();
        var db = new Database(path);
        db.Migrate();
        var career = db.CreateCareer("Save", "Player", "", 20, "Club", "League", "2017/18", "ST", 9);
        db.SaveMatch(career, new("2017-09-02", "League", "Other", true, 2, 1, true, 90, 1, 0, 8, false, false, false, false, ""));
        db.Migrate();
        db.Migrate();
        Assert.Single(db.GetMatches(career));
        Assert.Single(db.GetCareers());
    }

    [Fact]
    public void A_database_without_the_newer_match_columns_upgrades_and_keeps_its_matches()
    {
        var path = NewPath();
        // Shape the file like an older release: matches without any of the "known" flags.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            connection.Open();
            using var setup = connection.CreateCommand();
            setup.CommandText = """
                CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);
                CREATE TABLE careers(id INTEGER PRIMARY KEY, save_name TEXT NOT NULL, player_name TEXT NOT NULL,
                  nationality TEXT NOT NULL, age INTEGER NOT NULL, club TEXT NOT NULL, league TEXT NOT NULL, season TEXT NOT NULL,
                  current_date TEXT NOT NULL, position TEXT NOT NULL, shirt_number INTEGER NOT NULL, next_opponent TEXT,
                  created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
                CREATE TABLE matches(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL, date TEXT NOT NULL,
                  competition TEXT NOT NULL, opponent TEXT NOT NULL, is_home INTEGER NOT NULL, team_score INTEGER NOT NULL,
                  opponent_score INTEGER NOT NULL, started INTEGER NOT NULL, minutes INTEGER NOT NULL, goals INTEGER NOT NULL,
                  assists INTEGER NOT NULL, rating REAL NOT NULL, yellow_card INTEGER NOT NULL, red_card INTEGER NOT NULL,
                  penalty_scored INTEGER NOT NULL, penalty_missed INTEGER NOT NULL, notes TEXT NOT NULL, next_opponent TEXT,
                  is_derby INTEGER NOT NULL, is_major INTEGER NOT NULL, result TEXT NOT NULL, created_at TEXT NOT NULL);
                INSERT INTO careers(id,save_name,player_name,nationality,age,club,league,season,current_date,position,shirt_number,created_at,updated_at)
                  VALUES(1,'Old Save','Legacy Player','English',22,'Old Club','Old League','2017/18','2017-09-01','ST',9,'2017-09-01','2017-09-01');
                INSERT INTO matches(id,career_id,date,competition,opponent,is_home,team_score,opponent_score,started,minutes,goals,assists,rating,
                  yellow_card,red_card,penalty_scored,penalty_missed,notes,next_opponent,is_derby,is_major,result,created_at)
                  VALUES(1,1,'2017-09-02','League','Rivals',1,2,1,1,90,1,0,8.0,0,0,0,0,'legacy note',NULL,0,0,'W','2017-09-02');
                INSERT INTO schema_migrations(version,applied_at) VALUES(1,'2017-09-01');
                """;
            setup.ExecuteNonQuery();
        }

        var db = new Database(path);
        db.Migrate();

        var careers = db.GetCareers();
        var career = Assert.Single(careers);
        Assert.Equal("Legacy Player", career.PlayerName);
        var match = Assert.Single(db.GetMatches(career.Id));
        Assert.Equal("Rivals", match.Input.Opponent);
        Assert.Equal(1, match.Input.Goals);
        Assert.Equal("legacy note", match.Input.Notes);
        // Older matches were entered by hand, so their venue, score, and starter status were all asserted.
        Assert.True(match.Input.IsHomeKnown);
        Assert.True(match.Input.ScoreKnown);
        Assert.True(match.Input.StartedKnown);

        // The new tables must be usable straight away on an upgraded file.
        db.SaveMatchPerformances(career.Id, match.Id, "FIFA 18 Save", [new("900", "Mate", "CM", true, 90, 7)]);
        Assert.Single(db.GetMatchPerformances(match.Id));
        db.CacheProviderNews(career.Id, "FIFA 18 Save", [new("k", "2017-09-02", 1, 2, "Title", "Body")]);
        Assert.Single(db.GetCachedProviderNews(career.Id, "FIFA 18 Save"));
        db.RecordDialogueKeys(career.Id, 1, ["phrase"]);
        Assert.Single(db.GetRecentDialogueKeys(career.Id, 1));
    }

    [Fact]
    public void A_backup_round_trip_keeps_the_career_intact()
    {
        var path = NewPath();
        var db = new Database(path);
        db.Migrate();
        var career = db.CreateCareer("Save", "Player", "", 20, "Club", "League", "2017/18", "ST", 9);
        var mate = db.AddCharacter(career, "Mate", 24, "", "Club", "CM", "Starter", CharacterType.Teammate);
        db.SaveMatch(career, new("2017-09-02", "League", "Other", true, 2, 1, true, 90, 1, 0, 8, false, false, false, false, ""));
        db.AddMemory(career, mate, null, "A shared memory", 50, 20, "match");

        var backup = Path.Combine(_dir, "backup.db");
        db.Backup(backup);

        db.CreateCareer("Later", "Someone Else", "", 20, "Club", "League", "2017/18", "ST", 9);
        db.Restore(backup);

        Assert.Single(db.GetCareers());
        Assert.Single(db.GetMatches(career));
        Assert.Single(db.GetMemories(mate));
    }

    [Fact]
    public void A_briefing_that_blamed_the_game_for_an_unnamed_team_is_repaired_on_upgrade()
    {
        var path = Path.Combine(_dir, "legacy.db");
        var db = new Database(path);
        db.Migrate();
        var career = db.CreateCareer("Save", "Player", "", 20, "Club", "League", "2017/18", "ST", 9, "2017-09-01");
        // Written the way an older build phrased it, then rolled back so the repair runs again.
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var seed = connection.CreateCommand();
            seed.CommandText = """
                DELETE FROM schema_migrations WHERE version=14;
                INSERT INTO notifications(career_id,kind,title,body,action,priority,is_read,dedupe_key,created_at)
                  VALUES($career,'Scouting','Briefing: FC Basel','Home fixture against FC Basel. player selection not confirmed by FIFA; playing time must not be assumed.','PreMatch',60,0,'legacy-briefing','2017-09-13T00:00:00');
                """;
            seed.Parameters.AddWithValue("$career", career);
            seed.ExecuteNonQuery();
        }

        new Database(path).Migrate();

        var repaired = db.GetNotifications(career).Single(x => x.DedupeKey == "legacy-briefing");
        Assert.DoesNotContain("FIFA", repaired.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("the manager has not named the team yet", repaired.Body);
    }

    // FIFA names every save after the moment it was written, so a career linked to one file name looked
    // like a different career on the very next save. That created a duplicate companion career, which reset
    // the statistics baseline and left the next match reporting no goals and no assists.
    [Theory]
    [InlineData("138449", 138449, "a new save file of the same career still belongs to it")]
    [InlineData("138449", 138449, "a transfer does not make it a different career")]
    public void A_linked_career_is_recognised_by_its_player_not_its_file(string linked, int savePlayer, string because)
        => Assert.True(CareerLinkResolver.BelongsToCareer(linked, savePlayer, sameIdentity: false, sameClub: false), because);

    [Fact]
    public void A_save_for_a_different_player_is_still_a_different_career()
        => Assert.False(CareerLinkResolver.BelongsToCareer("138449", 30999, sameIdentity: true, sameClub: true));

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void An_unlinked_career_falls_back_to_the_player_and_club_it_describes(bool sameIdentity, bool sameClub, bool belongs)
        => Assert.Equal(belongs, CareerLinkResolver.BelongsToCareer("", 138449, sameIdentity, sameClub));

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(30, "just now")]
    [InlineData(90, "a minute ago")]
    [InlineData(60 * 8, "8 minutes ago")]
    [InlineData(60 * 75, "an hour ago")]
    [InlineData(60 * 60 * 5, "5 hours ago")]
    [InlineData(60 * 60 * 30, "yesterday")]
    [InlineData(60 * 60 * 24 * 4, "4 days ago")]
    public void A_sync_age_is_phrased_the_way_a_person_would_say_it(int secondsAgo, string expected)
    {
        var now = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, RelativeTime.Since(now.AddSeconds(-secondsAgo), now));
    }

    [Fact]
    public void A_clock_that_runs_behind_never_reports_a_sync_in_the_future()
    {
        var now = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("just now", RelativeTime.Since(now.AddMinutes(5), now));
    }

    [Fact]
    public void The_career_being_played_wins_when_older_duplicates_share_a_player()
    {
        var db = new Database(Path.Combine(_dir, "routing.db"));
        db.Migrate();
        var abandoned = db.CreateCareer("Old", "Ricardo", "", 20, "Real Betis", "League", "2017/18", "CAM", 22);
        var live = db.CreateCareer("Current", "Ricardo", "", 20, "Milan", "League", "2017/18", "CAM", 22);
        foreach (var id in new[] { abandoned, live }) db.SetSetting($"career:{id}:fifa_player_id", "138449");
        db.SetSetting($"career:{abandoned}:fifa_last_sync_at", new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc).ToString("O"));
        db.SetSetting($"career:{live}:fifa_last_sync_at", new DateTime(2026, 8, 15, 13, 16, 0, DateTimeKind.Utc).ToString("O"));

        Assert.Equal(live, db.FindCareerIdByFifaPlayerId(138449));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
