using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Services;
using CareerCompanion.Core.Simulation;

namespace CareerCompanion.Tests;

public sealed class ReactionComposerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "touchline-composer-" + Guid.NewGuid().ToString("N"));
    private Database NewDb() { var db = new Database(Path.Combine(_dir, "world.db")); db.Migrate(); return db; }

    private static MatchInput Match(string date, int teamScore, int opponentScore, int goals = 0, double rating = 7,
        bool red = false, bool started = true, int minutes = 90, bool derby = false)
        => new(date, "League", "Rivals FC", true, teamScore, opponentScore, started, minutes, goals, 0, rating,
            false, red, false, false, "", null, derby, false);

    [Fact]
    public void Different_events_produce_meaningfully_different_messages()
    {
        var db = NewDb();
        var career = db.CreateCareer("Save", "Alex Player", "", 21, "Club", "League", "2017/18", "ST", 9, "2017-09-01");
        var mate = db.AddCharacter(career, "Mate", 25, "", "Club", "CM", "Starter", CharacterType.Teammate);
        var service = new CareerService(db);
        var builder = new MatchNarrativeBuilder(db);
        var composer = new ReactionComposer(db);
        var character = db.GetCharacters(career).Single(x => x.Id == mate);

        var hattrick = service.ProcessMatch(career, Match("2017-09-02", 4, 0, goals: 3, rating: 9.2));
        var sending = service.ProcessMatch(career, Match("2017-09-09", 0, 2, rating: 4.5, red: true));

        var first = composer.Compose(character, builder.Build(db.GetCareer(career), hattrick.Match), hattrick.Events[0].Id);
        var second = composer.Compose(character, builder.Build(db.GetCareer(career), sending.Match), sending.Events[0].Id);

        Assert.NotEqual(first.Text, second.Text);
        Assert.NotEqual(first.Stance, second.Stance);
        Assert.True(first.Valence > second.Valence, $"a hat-trick should be warmer than a red card ({first.Valence} vs {second.Valence})");
    }

    [Fact]
    public void A_hostile_team_mate_criticises_where_a_close_one_supports()
    {
        var db = NewDb();
        var career = db.CreateCareer("Save", "Alex Player", "", 21, "Club", "League", "2017/18", "ST", 9, "2017-09-01");
        var blunt = db.AddCharacter(career, "Blunt", 30, "", "Club", "CB", "Starter", CharacterType.Teammate,
            new(60, 85, 20, 30, 75, 20, 70, 60, 30, 50, 40, 60), new("brief", 85, 20, 10, 60, 30, 10, 60));
        var friend = db.AddCharacter(career, "Friend", 23, "", "Club", "CM", "Starter", CharacterType.Teammate,
            new(60, 50, 55, 80, 20, 75, 50, 45, 75, 40, 80, 70), new("moderate", 45, 25, 35, 60, 40, 20, 5));
        db.SaveRelationship(new(blunt, -40, -30, -10, -35, 70, 60, 40));
        db.SaveRelationship(new(friend, 70, 65, 55, 70, 0, 0, 60));

        var result = new CareerService(db).ProcessMatch(career, Match("2017-09-02", 0, 3, rating: 4.2));
        var narrative = new MatchNarrativeBuilder(db).Build(db.GetCareer(career), result.Match);
        var composer = new ReactionComposer(db);
        var characters = db.GetCharacters(career).ToDictionary(x => x.Id);

        var hostile = composer.Compose(characters[blunt], narrative, result.Events[0].Id);
        var warm = composer.Compose(characters[friend], narrative, result.Events[0].Id);

        Assert.True(hostile.Valence < 0, $"a hostile team-mate should not be warm after a bad night: {hostile.Text}");
        Assert.True(warm.Valence >= 0, $"a close friend should stay supportive: {warm.Text}");
        Assert.NotEqual(hostile.Text, warm.Text);
    }

    [Fact]
    public void The_same_character_does_not_repeat_itself_across_similar_matches()
    {
        var db = NewDb();
        var career = db.CreateCareer("Save", "Alex Player", "", 21, "Club", "League", "2017/18", "ST", 9, "2017-09-01");
        var mate = db.AddCharacter(career, "Mate", 25, "", "Club", "CM", "Starter", CharacterType.Teammate);
        var service = new CareerService(db);
        var builder = new MatchNarrativeBuilder(db);
        var composer = new ReactionComposer(db);
        var texts = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var result = service.ProcessMatch(career, Match($"2017-09-{2 + i:00}", 1, 0));
            var character = db.GetCharacters(career).Single(x => x.Id == mate);
            var composed = composer.Compose(character, builder.Build(db.GetCareer(career), result.Match), result.Events[0].Id);
            composer.Remember(career, mate, composed.PhraseKeys);
            texts.Add(composed.Text);
        }
        Assert.True(texts.Distinct(StringComparer.Ordinal).Count() >= 5,
            "six routine wins should not produce the same message: " + string.Join(" | ", texts));
    }

    [Fact]
    public void Two_characters_reacting_to_one_match_do_not_send_the_same_line()
    {
        var db = NewDb();
        var career = db.CreateCareer("Save", "Alex Player", "", 21, "Club", "League", "2017/18", "ST", 9, "2017-09-01");
        db.AddCharacter(career, "Boss", 50, "", "Club", "Manager", "Manager", CharacterType.Manager);
        for (var i = 0; i < 5; i++) db.AddCharacter(career, $"Mate {i}", 24 + i, "", "Club", "CM", "Starter", CharacterType.Teammate);
        var result = new CareerService(db).ProcessMatch(career, Match("2017-09-02", 3, 1, goals: 2, rating: 8.8));
        new AutomaticWorldService(db).ApplyMatch(result, true, true, false, false, false);
        var messages = db.GetCharacters(career).SelectMany(x => db.GetMessages(career, x.Id))
            .Where(x => x.Role == "assistant").Select(x => x.Content).ToList();
        Assert.True(messages.Count >= 2);
        Assert.Equal(messages.Count, messages.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Offline_reactions_never_mention_the_save_provider_or_parser()
    {
        var db = NewDb();
        var career = db.CreateCareer("Save", "Alex Player", "", 21, "Club", "League", "2017/18", "ST", 9, "2017-09-01");
        db.AddCharacter(career, "Boss", 50, "", "Club", "Manager", "Manager", CharacterType.Manager);
        db.AddCharacter(career, "Mate", 25, "", "Club", "CM", "Starter", CharacterType.Teammate);
        var service = new CareerService(db);
        var world = new AutomaticWorldService(db);
        for (var i = 0; i < 10; i++)
        {
            var result = service.ProcessMatch(career, Match($"2017-09-{2 + i:00}", i % 3, i % 2, goals: i % 4, rating: 5 + i % 5));
            world.ApplyMatch(result, true, true, false, false, false);
        }
        var banned = new[] { "FIFA", "provider", "parser", "json", "save file", "event key", "database", "unknown score", "null" };
        var messages = db.GetCharacters(career).SelectMany(x => db.GetMessages(career, x.Id))
            .Where(x => x.Role == "assistant").Select(x => x.Content).ToList();
        Assert.NotEmpty(messages);
        foreach (var message in messages)
            foreach (var term in banned)
                Assert.DoesNotContain(term, message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_scoring_drought_and_a_bench_run_are_recognised_as_their_own_stories()
    {
        var db = NewDb();
        var career = db.CreateCareer("Save", "Alex Player", "", 21, "Club", "League", "2017/18", "ST", 9, "2017-09-01");
        var service = new CareerService(db);
        for (var i = 0; i < 7; i++) service.ProcessMatch(career, Match($"2017-09-{2 + i:00}", 1, 1));
        var latest = service.ProcessMatch(career, Match("2017-09-20", 1, 1));
        var narrative = new MatchNarrativeBuilder(db).Build(db.GetCareer(career), latest.Match);
        Assert.True(narrative.Has("drought"), "a striker without a goal in eight matches should have a drought story");

        var benched = service.ProcessMatch(career, Match("2017-09-21", 1, 0, started: false, minutes: 15));
        for (var i = 0; i < 3; i++) service.ProcessMatch(career, Match($"2017-09-{22 + i:00}", 1, 0, started: false, minutes: 12));
        var last = service.ProcessMatch(career, Match("2017-09-26", 1, 0, started: false, minutes: 10));
        var benchNarrative = new MatchNarrativeBuilder(db).Build(db.GetCareer(career), last.Match);
        Assert.True(benchNarrative.Has("bench_streak"), "repeated benchings should become their own story");
        Assert.NotNull(benched);
    }

    [Fact]
    public void A_routine_win_is_quieter_than_a_derby_win()
    {
        var db = NewDb();
        var career = db.CreateCareer("Save", "Alex Player", "", 21, "Club", "League", "2017/18", "ST", 9, "2017-09-01");
        var service = new CareerService(db);
        var builder = new MatchNarrativeBuilder(db);
        var routine = builder.Build(db.GetCareer(career), service.ProcessMatch(career, Match("2017-09-02", 1, 0)).Match);
        var derby = builder.Build(db.GetCareer(career), service.ProcessMatch(career, Match("2017-09-09", 1, 0, derby: true)).Match);
        Assert.True(derby.Intensity > routine.Intensity);
    }

    [Fact]
    public void Team_mates_form_an_opinion_from_matches_and_rescans_do_not_inflate_it()
    {
        var db = NewDb();
        var career = db.CreateCareer("Save", "Alex Player", "", 21, "Club", "League", "2017/18", "ST", 9, "2017-09-01");
        var mate = db.AddCharacter(career, "Mate", 25, "", "Club", "CM", "Starter", CharacterType.Teammate);
        var result = new CareerService(db).ProcessMatch(career, Match("2017-09-02", 3, 0, goals: 2, rating: 9));
        db.SaveMatchPerformances(career, result.Match.Id, "FIFA 18 Save", [new("900", "Mate", "CM", true, 90, 7)]);
        var before = db.GetRelationship(mate);

        var world = new AutomaticWorldService(db);
        world.ApplyMatch(result, true, true, false, false, false);
        var after = db.GetRelationship(mate);
        Assert.True(after.Respect > before.Respect, "a strong performance should earn respect");

        world.ApplyMatch(result, true, true, false, false, false);
        Assert.Equal(after, db.GetRelationship(mate));
    }

    [Fact]
    public void A_sending_off_costs_standing_in_the_dressing_room()
    {
        var db = NewDb();
        var career = db.CreateCareer("Save", "Alex Player", "", 21, "Club", "League", "2017/18", "ST", 9, "2017-09-01");
        var mate = db.AddCharacter(career, "Mate", 25, "", "Club", "CM", "Starter", CharacterType.Teammate);
        var result = new CareerService(db).ProcessMatch(career, Match("2017-09-02", 0, 3, rating: 4.5, red: true));
        db.SaveMatchPerformances(career, result.Match.Id, "FIFA 18 Save", [new("900", "Mate", "CM", true, 90, 6)]);
        new AutomaticWorldService(db).ApplyMatch(result, true, true, false, false, false);
        var after = db.GetRelationship(mate);
        Assert.True(after.Respect < 0, "a poor, red-carded night should cost respect");
        Assert.True(after.Tension > 0);
    }

    public void Dispose() { try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (IOException) { } }
}
