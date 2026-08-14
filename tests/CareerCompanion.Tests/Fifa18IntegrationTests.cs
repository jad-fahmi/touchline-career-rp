using CareerCompanion.Core.Providers.Fifa18;
using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Services;
using System.Text.Json;

namespace CareerCompanion.Tests;

public sealed class Fifa18IntegrationTests : IDisposable
{
    private readonly string _dir=Path.Combine(Path.GetTempPath(),"touchline-fifa-tests-"+Guid.NewGuid().ToString("N"));

    [Fact]
    public void Parses_away_defeat_from_Fifa_news()
    {
        var report=Fifa18CareerNormalizer.ParseMatchReport(
            "LaLiga Santander Review: RC Deportivo vs Real Madrid",
            "In this early season game, RC Deportivo were victorious 1-0 over Real Madrid in their league clash.","Real Madrid");
        Assert.NotNull(report);Assert.False(report.IsHome);Assert.Equal("RC Deportivo",report.Opponent);
        Assert.Equal(0,report.TeamScore);Assert.Equal(1,report.OpponentScore);Assert.Equal("LaLiga Santander",report.Competition);
    }

    [Fact]
    public void Parses_home_victory_from_Fifa_news()
    {
        var report=Fifa18CareerNormalizer.ParseMatchReport(
            "Premier League Review: Arsenal vs Chelsea",
            "Arsenal were victorious 3-2 over Chelsea in a dramatic match.","Arsenal");
        Assert.NotNull(report);Assert.True(report.IsHome);Assert.Equal(3,report.TeamScore);Assert.Equal(2,report.OpponentScore);
    }

    [Fact] public void Ignores_unrelated_news()=>Assert.Null(Fifa18CareerNormalizer.ParseMatchReport("Transfer update","No match here.","Arsenal"));

    [Fact]
    public void Parses_next_fixture_from_Fifa_preview()
    {
        var fixture=Fifa18CareerNormalizer.ParseFixturePreview("LaLiga Santander Preview: Real Madrid vs Valencia CF",20170827,"Real Madrid",243,"Varane will miss the match after his sending-off.");
        Assert.NotNull(fixture);Assert.True(fixture.IsHome);Assert.Equal("Valencia CF",fixture.Opponent);
        Assert.Equal("2017-08-27",fixture.Date);Assert.Equal("LaLiga Santander",fixture.Competition);Assert.Contains("Varane will miss",fixture.Evidence);
    }

    [Fact]
    public void Fixture_does_not_claim_player_is_available_without_named_evidence()
    {
        var fixture=Fifa18CareerNormalizer.ParseFixturePreview("LaLiga Santander Preview: Real Madrid vs Valencia CF",20170827,"Real Madrid",243,
            "Varane will miss the match after his sending-off.","Kaka");
        Assert.NotNull(fixture);Assert.Equal("Unknown",fixture.Availability);
    }

    [Fact]
    public void Fixture_marks_named_player_injury_and_selection_states()
    {
        var injured=Fifa18CareerNormalizer.ParseFixturePreview("LaLiga Santander Preview: Real Madrid vs Valencia CF",20170827,"Real Madrid",243,
            "Kaka will miss the match after suffering an injury.","Kaka");
        var benched=Fifa18CareerNormalizer.ParseFixturePreview("LaLiga Santander Preview: Real Madrid vs Valencia CF",20170827,"Real Madrid",243,
            "Kaka is named among the substitutes.","Kaka");
        Assert.Equal("Injured",injured?.Availability);Assert.Equal("Benched",benched?.Availability);
    }

    [Fact]
    public void Embedded_player_index_resolves_save_ids()
    {
        var player=new Fifa18PlayerNameResolver().Find(156616);
        Assert.NotNull(player);Assert.Equal("F. Ribéry",player.Name);Assert.Equal("France",player.Nationality);
    }

    [Theory]
    [InlineData(5,"CB")]
    [InlineData(10,"CDM")]
    [InlineData(14,"CM")]
    [InlineData(18,"CAM")]
    [InlineData(25,"ST")]
    public void Maps_Fifa_position_ids(int id,string expected)=>Assert.Equal(expected,Fifa18CareerNormalizer.PositionName(id));

    [Fact]
    public void Locator_selects_newest_career_file()
    {
        Directory.CreateDirectory(_dir);var old=Path.Combine(_dir,"CareerOld");var newest=Path.Combine(_dir,"CareerNew");
        File.WriteAllText(old,"old");File.WriteAllText(newest,"new");File.SetLastWriteTimeUtc(old,DateTime.UtcNow.AddMinutes(-2));File.SetLastWriteTimeUtc(newest,DateTime.UtcNow);
        Assert.Equal(newest,new Fifa18SaveLocator().FindLatestCareer(_dir));
    }

    [Fact]
    public void Provider_import_keys_are_deduplicated_and_persisted()
    {
        Directory.CreateDirectory(_dir);var db=new Database(Path.Combine(_dir,"world.db"));db.Migrate();var career=db.CreateCareer("s","p","n",18,"c","l","2017/18","ST",9);
        db.RecordProviderImport(career,"FIFA 18 Save","event-1","save","hash",DateTime.UtcNow,"{\"PlayerId\":1}");
        db.RecordProviderImport(career,"FIFA 18 Save","event-1","save","hash",DateTime.UtcNow,"different");
        Assert.True(db.HasProviderImport(career,"FIFA 18 Save","event-1"));Assert.Equal("{\"PlayerId\":1}",new Database(Path.Combine(_dir,"world.db")).GetLatestProviderPayload(career,"FIFA 18 Save"));
    }

    [Fact]
    public void Squad_sync_updates_in_place_preserves_relationships_and_marks_departures()
    {
        Directory.CreateDirectory(_dir);var db=new Database(Path.Combine(_dir,"squad.db"));db.Migrate();var career=db.CreateCareer("s","p","n",18,"Real Madrid","l","2017/18","ST",9);
        var first=new ProviderCharacterFact("1","Player One",25,"Spain","Real Madrid","CM","Squad member",CharacterType.Teammate,"{\"overall\":80}","{}");
        var second=new ProviderCharacterFact("2","Player Two",27,"France","Real Madrid","CB","Squad member",CharacterType.Teammate,"{\"overall\":82}","{}");
        var initial=db.SyncProviderCharacters(career,"FIFA 18 Save",[first,second]);Assert.Equal(2,initial.Added);
        var one=db.GetCharacters(career).Single(x=>x.Name=="Player One");db.SaveRelationship(new(one.Id,Score:31,Trust:22));
        db.UpdateCharacterProfile(one.Id,"{\"userNote\":\"captain material\"}",one.PersonalityJson,one.CommunicationJson,one.HistoricalNotes,one.IsPublic);
        var result=db.SyncProviderCharacters(career,"FIFA 18 Save",[first with{Position="CAM",FactsJson="{\"overall\":84}"}]);
        Assert.Equal(1,result.Updated);Assert.Equal(1,result.MarkedInactive);Assert.Equal(2,db.GetCharacters(career).Count);
        one=db.GetCharacters(career).Single(x=>x.Name=="Player One");Assert.Equal("CAM",one.Position);Assert.Equal(31,db.GetRelationship(one.Id).Score);
        using var facts=JsonDocument.Parse(one.FactsJson);Assert.Equal("captain material",facts.RootElement.GetProperty("userNote").GetString());Assert.Equal(84,facts.RootElement.GetProperty("overall").GetInt32());
        Assert.Equal("Former teammate",db.GetCharacters(career).Single(x=>x.Name=="Player Two").SquadRole);
    }

    [Fact]
    public void New_fixture_supersedes_previous_upcoming_fixture()
    {
        Directory.CreateDirectory(_dir);var db=new Database(Path.Combine(_dir,"fixtures.db"));db.Migrate();var career=db.CreateCareer("s","p","n",18,"c","l","2017/18","ST",9);
        db.UpsertFixture(career,"FIFA 18 Save","f1","2017-08-27","League","Valencia",true,90,"preview","a");
        db.UpsertFixture(career,"FIFA 18 Save","f2","2017-09-03","League","Levante",false,90,"preview","b");
        var fixtures=db.GetFixtures(career);Assert.Equal("Levante",fixtures.Single(x=>x.Status=="Upcoming").Opponent);Assert.Equal("Superseded",fixtures.Single(x=>x.Opponent=="Valencia").Status);
    }

    [Fact]
    public void Fixture_persists_player_availability()
    {
        Directory.CreateDirectory(_dir);var db=new Database(Path.Combine(_dir,"fixture-availability.db"));db.Migrate();var career=db.CreateCareer("s","p","n",18,"c","l","2017/18","ST",9,"2017-08-01");
        db.UpsertFixture(career,"FIFA 18 Save","f1","2017-08-27","League","Valencia",true,90,"preview","a","Club","c","Injured");
        Assert.Equal("Injured",Assert.Single(db.GetFixtures(career)).Availability);
    }

    [Fact]
    public void Superseded_played_fixture_can_be_completed()
    {
        Directory.CreateDirectory(_dir);var db=new Database(Path.Combine(_dir,"completed-fixture.db"));db.Migrate();var career=db.CreateCareer("s","p","n",18,"c","l","2017/18","ST",9);
        db.UpsertFixture(career,"FIFA 18 Save","f1","2017-08-27","League","Valencia",true,90,"preview","a");
        db.UpsertFixture(career,"FIFA 18 Save","f2","2017-09-03","League","Levante",false,90,"preview","b");
        db.CompleteMatchingFixture(career,"2017-08-27","Valencia");
        Assert.Equal("Completed",db.GetFixtures(career).Single(x=>x.Opponent=="Valencia").Status);
    }

    [Fact]
    public void Provider_match_save_is_idempotent()
    {
        Directory.CreateDirectory(_dir);var db=new Database(Path.Combine(_dir,"provider-match.db"));db.Migrate();var career=db.CreateCareer("s","p","n",18,"c","l","2017/18","ST",9);var input=new MatchInput("2017-08-27","League","Valencia",true,2,1,true,90,1,0,7.5,false,false,false,false,"");
        var first=db.SaveProviderMatch(career,"FIFA 18 Save","event",input);var second=db.SaveProviderMatch(career,"FIFA 18 Save","event",input);
        Assert.True(first.Created);Assert.False(second.Created);Assert.Equal(first.MatchId,second.MatchId);Assert.Single(db.GetMatches(career));
    }

    [Fact]
    public void Unknown_score_match_is_persisted_without_inventing_a_result()
    {
        Directory.CreateDirectory(_dir);var db=new Database(Path.Combine(_dir,"unknown-score.db"));db.Migrate();var career=db.CreateCareer("s","p","n",18,"c","l","2017/18","ST",9);
        var input=new MatchInput("2017-08-27","Career match","Opponent unknown",true,0,0,false,66,1,0,7,false,false,false,false,"FIFA rating history",StartedKnown:false,ScoreKnown:false);
        var result=new CareerService(db).ProcessMatch(career,input,"FIFA 18 Save","unknown-score-event");
        Assert.False(result.Match.Input.ScoreKnown);Assert.Equal("U",result.Match.Result);Assert.Contains(result.Events,x=>x.Type=="MATCH_RECORDED");Assert.DoesNotContain(result.Events,x=>x.Type is "MATCH_WON" or "MATCH_LOST" or "MATCH_DRAWN");
    }

    [Fact]
    public void Provider_news_is_deduped_per_career_not_globally()
    {
        Directory.CreateDirectory(_dir);var db=new Database(Path.Combine(_dir,"news.db"));db.Migrate();var one=db.CreateCareer("a","p","n",18,"c","l","2017/18","ST",9);var two=db.CreateCareer("b","q","n",18,"d","l","2017/18","ST",9);
        Assert.True(db.AddProviderNews(one,"same","Story","Body",50,"2017-08-27"));Assert.False(db.AddProviderNews(one,"same","Story","Body",50,"2017-08-27"));Assert.True(db.AddProviderNews(two,"same","Story","Body",50,"2017-08-27"));
        Assert.Single(db.GetNews(one));Assert.Single(db.GetNews(two));
    }

    [Fact]
    public void Staff_sync_retires_replaced_provider_manager()
    {
        Directory.CreateDirectory(_dir);var db=new Database(Path.Combine(_dir,"staff.db"));db.Migrate();var career=db.CreateCareer("s","p","n",18,"Club","l","2017/18","ST",9);var state=new Fifa18SyncState(1,2,20170827,1,0,0,0,0,0,-1,0);
        Fifa18ParsedCareer Parsed(string manager)=>new("save","hash"+manager,DateTime.UtcNow,"Player",1,1,"N",18,"Club",2,3,"League","2017/18","2017-08-27","ST",9,state,null,[],null,1,[],ManagerName:manager);
        var service=new Fifa18ImportService(db);service.SyncSupportingFacts(career,Parsed("First Boss"),false);service.SyncSupportingFacts(career,Parsed("Second Boss"),false);
        var managers=db.GetCharacters(career).Where(x=>x.Type==CharacterType.Manager).ToList();Assert.Equal("Former manager",managers.Single(x=>x.Name=="First Boss").SquadRole);Assert.Equal("Manager",managers.Single(x=>x.Name=="Second Boss").SquadRole);
    }

    [Fact]
    public void Supporting_import_service_persists_normalized_squad_and_fixture()
    {
        Directory.CreateDirectory(_dir);var db=new Database(Path.Combine(_dir,"supporting.db"));db.Migrate();var career=db.CreateCareer("s","p","n",18,"Real Madrid","l","2017/18","ST",9);
        var parsed=new Fifa18ParsedCareer("save","hash",DateTime.UtcNow,"Player",99,38,"Portugal",18,"Real Madrid",243,53,"LaLiga Santander","2017/18","2017-08-27","ST",9,
            new(99,243,20170827,1,2,0,0,0,0,1,20170820),null,
            [new(1,"Teammate","Spain",25,"CM",8,82,4,false)],
            new("fixture","2017-08-27","LaLiga Santander","Valencia CF",true,90,"preview"),2,[]) with{NationalTeamId=1354,NationalTeamName="Portugal"};
        var result=new Fifa18ImportService(db).SyncSupportingFacts(career,parsed,true);
        new Fifa18ImportService(db).SyncSupportingFacts(career,parsed,true);Assert.Equal(1,result.Squad!.Added);Assert.True(result.FixtureUpdated);Assert.Equal("Teammate",db.GetCharacters(career).Single().Name);Assert.Equal("Valencia CF",db.GetFixtures(career).Single().Opponent);Assert.Single(db.GetEvents(career),x=>x.Type=="NATIONAL_TEAM_CALLED_UP");Assert.Single(db.GetNotifications(career),x=>x.Kind=="International");
    }

    [Fact]
    public void Parser_rejects_non_save_without_writing_it()
    {
        var bytes="not a fifa save"u8.ToArray();Assert.Throws<Fifa18SaveFormatException>(()=>new Fifa18SaveParser().Parse(bytes));Assert.Equal("not a fifa save"u8.ToArray(),bytes);
    }

    public void Dispose(){try{Directory.Delete(_dir,true);}catch{}}
}
