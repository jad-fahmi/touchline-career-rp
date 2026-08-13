using CareerCompanion.Core.Providers.Fifa18;
using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
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
        var fixture=Fifa18CareerNormalizer.ParseFixturePreview("LaLiga Santander Preview: Real Madrid vs Valencia CF",20170827,"Real Madrid",243);
        Assert.NotNull(fixture);Assert.True(fixture.IsHome);Assert.Equal("Valencia CF",fixture.Opponent);
        Assert.Equal("2017-08-27",fixture.Date);Assert.Equal("LaLiga Santander",fixture.Competition);
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
    public void Supporting_import_service_persists_normalized_squad_and_fixture()
    {
        Directory.CreateDirectory(_dir);var db=new Database(Path.Combine(_dir,"supporting.db"));db.Migrate();var career=db.CreateCareer("s","p","n",18,"Real Madrid","l","2017/18","ST",9);
        var parsed=new Fifa18ParsedCareer("save","hash",DateTime.UtcNow,"Player",99,38,"Portugal",18,"Real Madrid",243,53,"LaLiga Santander","2017/18","2017-08-27","ST",9,
            new(99,243,20170827,1,2,0,0,0,0,1,20170820),null,
            [new(1,"Teammate","Spain",25,"CM",8,82,4,false)],
            new("fixture","2017-08-27","LaLiga Santander","Valencia CF",true,90,"preview"),2,[]);
        var result=new Fifa18ImportService(db).SyncSupportingFacts(career,parsed,true);
        Assert.Equal(1,result.Squad!.Added);Assert.True(result.FixtureUpdated);Assert.Equal("Teammate",db.GetCharacters(career).Single().Name);Assert.Equal("Valencia CF",db.GetFixtures(career).Single().Opponent);
    }

    [Fact]
    public void Parser_rejects_non_save_without_writing_it()
    {
        var bytes="not a fifa save"u8.ToArray();Assert.Throws<Fifa18SaveFormatException>(()=>new Fifa18SaveParser().Parse(bytes));Assert.Equal("not a fifa save"u8.ToArray(),bytes);
    }

    public void Dispose(){try{Directory.Delete(_dir,true);}catch{}}
}
