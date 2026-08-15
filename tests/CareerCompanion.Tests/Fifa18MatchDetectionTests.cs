using CareerCompanion.Core.Providers.Fifa18;

namespace CareerCompanion.Tests;

/// <summary>
/// End-to-end detection over an assembled FIFA table set. The shapes here mirror rows read from real
/// FIFA 18 career saves: a rating-history row per player per club match, news articles carrying the
/// other club's id, and season totals that only move when the player scores.
/// </summary>
public sealed class Fifa18MatchDetectionTests
{
    private const int Club = 241;          // FC Barcelona
    private const int Opponent = 243;      // Real Madrid
    private const int Player = 138449;
    private const int UserId = 0;

    private sealed class SaveBuilder
    {
        private readonly Fifa18SaveData _data = new();
        private readonly List<IReadOnlyDictionary<string, object>> _ratings = [];
        private readonly List<IReadOnlyDictionary<string, object>> _news = [];
        private readonly List<IReadOnlyDictionary<string, object>> _links = [];

        public SaveBuilder()
        {
            _data.Add("career_users", [Row(("userid", UserId), ("usertype", 2L), ("clubteamid", Club), ("leagueid", 53),
                ("seasoncount", 1), ("nationalteamid", -1), ("nationalityid", 54), ("firstname", "Test"), ("surname", "Player"),
                ("agentname", ""))]);
            _data.Add("career_playasplayer", [Row(("userid", UserId), ("playerid", Player), ("position", 27))]);
            _data.Add("career_calendar", [Row(("currdate", 20170810), ("startdate", 20170701))]);
            _data.Add("teams", [
                Row(("teamid", Club), ("teamname", "FC Barcelona"), ("rivalteam", Opponent)),
                Row(("teamid", Opponent), ("teamname", "Real Madrid"), ("rivalteam", Club)),
                Row(("teamid", 17), ("teamname", "Southampton"), ("rivalteam", -1))]);
            _data.Add("leagues", [Row(("leagueid", 53), ("leaguename", "LaLiga Santander"))]);
            _data.Add("nations", [Row(("nationid", 54), ("nationname", "Brazil"))]);
            _data.Add("players", [Row(("playerid", Player), ("birthdate", 150000), ("nationality", 54), ("preferredposition1", 27), ("overallrating", 84))]);
            _data.Add("rivals", [Row(("rivaltype", 0), ("teamid", Club), ("rivalteamid", Opponent))]);
        }

        public SaveBuilder Season(int appearances, int goals, int assists, int yellows = 0, int reds = 0)
        {
            _data.Add("career_playasplayerhistory", [Row(("userid", UserId), ("season", 1), ("teamid", Club),
                ("appearances", appearances), ("goals", goals), ("assists", assists), ("totalyellows", yellows),
                ("totalreds", reds), ("overall", 84))]);
            return this;
        }

        /// <summary>Adds a full team sheet for one matchday, with the career player in the given slot.</summary>
        public SaveBuilder MatchDay(int date, int firstKey, int playerPosition = 27, int minutes = 90, int rating = 8)
        {
            _ratings.Add(Row(("artificialkey", firstKey), ("date", date), ("playerid", Player),
                ("minsplayed", minutes), ("rating", rating), ("position", playerPosition)));
            for (var i = 1; i <= 12; i++)
                _ratings.Add(Row(("artificialkey", firstKey + i), ("date", date), ("playerid", 200000 + i),
                    ("minsplayed", 90), ("rating", 7), ("position", i)));
            return this;
        }

        /// <summary>A club matchday the career player has no rating row for.</summary>
        public SaveBuilder MatchDayWithoutPlayer(int date, int firstKey)
        {
            for (var i = 1; i <= 12; i++)
                _ratings.Add(Row(("artificialkey", firstKey + i), ("date", date), ("playerid", 200000 + i),
                    ("minsplayed", 90), ("rating", 7), ("position", i)));
            return this;
        }

        public SaveBuilder News(int date, int teamId, int relatedTeamId, string title, string body)
        {
            _news.Add(Row(("date", date), ("teamid", teamId), ("relatedteamid", relatedTeamId),
                ("title", title), ("body", body), ("importance", 3), ("playerid", -1)));
            return this;
        }

        public Fifa18ParsedCareer BuildWithRemembered(IReadOnlyList<CareerCompanion.Core.Domain.CachedProviderArticle> remembered,
            Fifa18SyncState? previous = null)
        {
            Finish();
            return new Fifa18CareerNormalizer().Normalize(_data, "Career20170810", "fingerprint", previous, remembered);
        }

        public Fifa18ParsedCareer Build(Fifa18SyncState? previous = null)
        {
            Finish();
            return new Fifa18CareerNormalizer().Normalize(_data, "Career20170810", "fingerprint", previous);
        }

        private void Finish()
        {
            for (var i = 1; i <= 12; i++) _links.Add(Row(("playerid", 200000 + i), ("teamid", Club), ("jerseynumber", i), ("form", 5), ("injury", 0)));
            _links.Add(Row(("playerid", Player), ("teamid", Club), ("jerseynumber", 11), ("form", 6), ("injury", 0)));
            _data.Add("teamplayerlinks", _links);
            _data.Add("career_playermatchratinghistory", _ratings);
            _data.Add("career_news", _news);
        }

        private static Dictionary<string, object> Row(params (string Key, object Value)[] values)
            => values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
    }

    private static Fifa18SyncState Baseline(int ratingKey, int date, int appearances, int goals, int assists)
        => new(Player, Club, 20170801, 1, appearances, goals, assists, 0, 0, ratingKey, date);

    [Fact]
    public void Reads_started_minutes_and_rating_from_the_appearance_log()
    {
        var parsed = new SaveBuilder().Season(1, 0, 0)
            .MatchDay(20170806, 50, playerPosition: 27, minutes: 90, rating: 9)
            .News(20170806, Club, Opponent, "Supercopa Review: FC Barcelona vs Real Madrid",
                "FC Barcelona were victorious 3-2 over Real Madrid.")
            .Build();

        var match = Assert.Single(parsed.PendingMatches);
        Assert.True(match.Started);
        Assert.True(match.StartedKnown);
        Assert.Equal(90, match.Minutes);
        Assert.Equal(9, match.Rating);
        Assert.Equal("2017-08-06", match.Date);
    }

    [Fact]
    public void Marks_a_substitute_appearance_as_not_started()
    {
        var parsed = new SaveBuilder().Season(1, 0, 0)
            .MatchDay(20170806, 50, playerPosition: 28, minutes: 22)
            .News(20170806, Club, Opponent, "Supercopa Review: FC Barcelona vs Real Madrid",
                "FC Barcelona were victorious 3-2 over Real Madrid.")
            .Build();

        var match = Assert.Single(parsed.PendingMatches);
        Assert.False(match.Started);
        Assert.True(match.StartedKnown);
        Assert.Equal(22, match.Minutes);
    }

    [Fact]
    public void Resolves_the_opponent_from_the_article_team_id_and_flags_the_derby()
    {
        var parsed = new SaveBuilder().Season(1, 0, 0)
            .MatchDay(20170806, 50)
            .News(20170806, Club, Opponent, "Supercopa Review: FC Barcelona vs Real Madrid",
                "FC Barcelona were victorious 3-2 over Real Madrid.")
            .Build();

        var match = Assert.Single(parsed.PendingMatches);
        Assert.Equal("Real Madrid", match.Opponent);
        Assert.Equal(Opponent, match.OpponentTeamId);
        Assert.True(match.IsDerby);
        Assert.True(match.IsHome);
        Assert.True(match.IsHomeKnown);
        Assert.Equal(3, match.TeamScore);
        Assert.Equal(2, match.OpponentScore);
        Assert.False(match.RequiresReview);
    }

    [Fact]
    public void Attributes_season_totals_to_a_single_new_appearance()
    {
        var parsed = new SaveBuilder().Season(2, 3, 1)
            .MatchDay(20170801, 20)
            .MatchDay(20170806, 50)
            .News(20170806, Club, Opponent, "Supercopa Review: FC Barcelona vs Real Madrid",
                "FC Barcelona were victorious 3-2 over Real Madrid.")
            .Build(Baseline(20, 20170801, 1, 1, 0));

        var match = Assert.Single(parsed.PendingMatches);
        Assert.Equal(2, match.Goals);
        Assert.Equal(1, match.Assists);
        Assert.False(match.RequiresReview);
    }

    [Fact]
    public void Keeps_goals_unknown_when_two_appearances_arrive_at_once()
    {
        var parsed = new SaveBuilder().Season(3, 4, 2)
            .MatchDay(20170801, 20).MatchDay(20170806, 50).MatchDay(20170809, 70)
            .News(20170806, Club, Opponent, "Supercopa Review: FC Barcelona vs Real Madrid",
                "FC Barcelona were victorious 3-2 over Real Madrid.")
            .News(20170809, Club, 17, "LaLiga Santander Review: Southampton vs FC Barcelona",
                "Southampton were victorious 2-0 over FC Barcelona.")
            .Build(Baseline(20, 20170801, 1, 1, 0));

        Assert.Equal(2, parsed.PendingMatches.Count);
        Assert.All(parsed.PendingMatches, x => Assert.Equal(0, x.Goals));
        Assert.All(parsed.PendingMatches, x => Assert.True(x.RequiresReview));
        Assert.Equal("2017-08-06", parsed.PendingMatches[0].Date);
        Assert.Equal("2017-08-09", parsed.PendingMatches[1].Date);
    }

    [Fact]
    public void Reads_an_away_defeat_in_the_players_own_direction()
    {
        var parsed = new SaveBuilder().Season(1, 0, 0)
            .MatchDay(20170806, 50)
            .News(20170806, Club, 17, "LaLiga Santander Review: Southampton vs FC Barcelona",
                "Southampton snuck away with a 2-1 win over FC Barcelona.")
            .Build();

        var match = Assert.Single(parsed.PendingMatches);
        Assert.Equal("Southampton", match.Opponent);
        Assert.False(match.IsHome);
        Assert.True(match.IsHomeKnown);
        Assert.Equal(1, match.TeamScore);
        Assert.Equal(2, match.OpponentScore);
    }

    [Fact]
    public void Uses_a_preview_headline_when_no_report_was_published()
    {
        var parsed = new SaveBuilder().Season(1, 0, 0)
            .MatchDay(20170806, 50)
            .News(20170806, Club, Club, "LaLiga Santander Preview: Southampton vs FC Barcelona",
                "Both teams will be looking to win this one.")
            .Build();

        var match = Assert.Single(parsed.PendingMatches);
        Assert.Equal("Southampton", match.Opponent);
        Assert.False(match.IsHome);
        Assert.False(match.ScoreKnown);
        Assert.False(match.RequiresReview);
    }

    [Fact]
    public void Never_reads_a_result_out_of_forward_looking_wording()
    {
        var parsed = new SaveBuilder().Season(1, 0, 0)
            .MatchDay(20170806, 50)
            .News(20170806, Club, Opponent, "Supercopa Preview: FC Barcelona vs Real Madrid",
                "FC Barcelona will be hoping to beat Real Madrid after their 3-1 win in last season's meeting.")
            .Build();

        var match = Assert.Single(parsed.PendingMatches);
        Assert.False(match.ScoreKnown);
        Assert.Equal("Real Madrid", match.Opponent);
    }

    [Fact]
    public void Identifies_the_opponent_from_the_other_clubs_own_article()
    {
        var parsed = new SaveBuilder().Season(1, 0, 0)
            .MatchDay(20170806, 50)
            .News(20170806, Opponent, -1, "Manager Praises Opposition Ahead Of Explosive Cup Final",
                "With the Supercopa Final looming, the manager has nothing but praise for his opponents FC Barcelona.")
            .Build();

        var match = Assert.Single(parsed.PendingMatches);
        Assert.Equal("Real Madrid", match.Opponent);
        Assert.False(match.IsHomeKnown);
        Assert.False(match.ScoreKnown);
        Assert.True(match.IsDerby);
    }

    [Fact]
    public void Counts_club_matches_the_player_was_left_out_of()
    {
        var parsed = new SaveBuilder().Season(2, 0, 0)
            .MatchDay(20170801, 20)
            .MatchDayWithoutPlayer(20170804, 40)
            .MatchDay(20170806, 60)
            .News(20170806, Club, Opponent, "Supercopa Review: FC Barcelona vs Real Madrid",
                "FC Barcelona were victorious 3-2 over Real Madrid.")
            .Build(Baseline(20, 20170801, 1, 0, 0));

        Assert.Equal(1, parsed.MissedClubMatches);
    }

    [Fact]
    public void Records_the_whole_matchday_squad_for_the_imported_match()
    {
        var parsed = new SaveBuilder().Season(1, 0, 0)
            .MatchDay(20170806, 50)
            .News(20170806, Club, Opponent, "Supercopa Review: FC Barcelona vs Real Madrid",
                "FC Barcelona were victorious 3-2 over Real Madrid.")
            .Build();

        var match = Assert.Single(parsed.PendingMatches);
        Assert.NotNull(match.TeamPerformances);
        Assert.Equal(13, match.TeamPerformances!.Count);
        Assert.Contains(match.TeamPerformances, x => x.PlayerId == Player && x.Minutes == 90);
    }

    [Fact]
    public void Reports_no_new_match_when_the_appearance_was_already_imported()
    {
        var parsed = new SaveBuilder().Season(1, 0, 0)
            .MatchDay(20170806, 50)
            .News(20170806, Club, Opponent, "Supercopa Review: FC Barcelona vs Real Madrid",
                "FC Barcelona were victorious 3-2 over Real Madrid.")
            .Build(Baseline(50, 20170806, 1, 0, 0));

        Assert.Empty(parsed.PendingMatches);
    }

    [Fact]
    public void Remembers_articles_so_a_later_scan_can_still_resolve_the_opponent()
    {
        var remembered = new[]
        {
            new CareerCompanion.Core.Domain.CachedProviderArticle("k1","2017-08-06",Club,Opponent,
                "Supercopa Review: FC Barcelona vs Real Madrid","FC Barcelona were victorious 3-2 over Real Madrid.")
        };
        var data = new SaveBuilder().Season(1, 0, 0).MatchDay(20170806, 50);
        var withoutNews = data.Build();
        Assert.Equal("Opponent unknown", Assert.Single(withoutNews.PendingMatches).Opponent);

        var parsed = new SaveBuilder().Season(1, 0, 0).MatchDay(20170806, 50)
            .BuildWithRemembered(remembered);
        var match = Assert.Single(parsed.PendingMatches);
        Assert.Equal("Real Madrid", match.Opponent);
        Assert.Equal(3, match.TeamScore);
    }

    [Fact]
    public void Finds_the_next_fixture_from_a_preview_headline()
    {
        var parsed = new SaveBuilder().Season(1, 0, 0)
            .MatchDay(20170806, 50)
            .News(20170812, Club, Opponent, "LaLiga Santander Preview: Real Madrid vs FC Barcelona",
                "Both sides go into this one in good form.")
            .Build(Baseline(50, 20170806, 1, 0, 0));

        Assert.NotNull(parsed.NextFixture);
        Assert.Equal("Real Madrid", parsed.NextFixture!.Opponent);
        Assert.False(parsed.NextFixture.IsHome);
        Assert.True(parsed.NextFixture.IsHomeKnown);
        Assert.Equal("2017-08-12", parsed.NextFixture.Date);
    }

    [Fact]
    public void Finds_the_next_fixture_from_a_player_story_that_only_carries_the_other_clubs_id()
    {
        var parsed = new SaveBuilder().Season(1, 0, 0)
            .MatchDay(20170806, 50)
            .News(20170812, Club, 17, "A Special Day For Our Number Nine",
                "An interesting encounter coming up as FC Barcelona host Southampton in this round of games.")
            .Build(Baseline(50, 20170806, 1, 0, 0));

        Assert.NotNull(parsed.NextFixture);
        Assert.Equal("Southampton", parsed.NextFixture!.Opponent);
        Assert.True(parsed.NextFixture.IsHome);
        Assert.True(parsed.NextFixture.IsHomeKnown);
    }

    [Fact]
    public void Leaves_the_venue_unknown_when_the_report_does_not_say_who_is_hosting()
    {
        var parsed = new SaveBuilder().Season(1, 0, 0)
            .MatchDay(20170806, 50)
            .News(20170812, Club, 17, "Squad Rotation Expected",
                "The manager is expected to rotate for the match against Southampton.")
            .Build(Baseline(50, 20170806, 1, 0, 0));

        Assert.NotNull(parsed.NextFixture);
        Assert.Equal("Southampton", parsed.NextFixture!.Opponent);
        Assert.False(parsed.NextFixture.IsHomeKnown);
    }

    [Fact]
    public void A_match_already_played_is_never_offered_as_the_next_fixture()
    {
        var parsed = new SaveBuilder().Season(1, 0, 0)
            .MatchDay(20170806, 50)
            .News(20170806, Club, Opponent, "Supercopa Review: FC Barcelona vs Real Madrid",
                "FC Barcelona were victorious 3-2 over Real Madrid.")
            .Build(Baseline(50, 20170806, 1, 0, 0));

        Assert.Null(parsed.NextFixture);
    }

    [Fact]
    public void Publishes_a_late_score_for_an_appearance_that_is_already_in_the_career()
    {
        var parsed = new SaveBuilder().Season(1, 0, 0)
            .MatchDay(20170806, 50)
            .News(20170806, Club, Opponent, "Supercopa Review: FC Barcelona vs Real Madrid",
                "FC Barcelona were victorious 3-2 over Real Madrid.")
            .Build(Baseline(50, 20170806, 1, 0, 0));

        var result = Assert.Single(parsed.ResolvedResults!);
        Assert.Equal("2017-08-06", result.Date);
        Assert.Equal(3, result.TeamScore);
        Assert.Equal(2, result.OpponentScore);
    }
}
