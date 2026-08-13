using CareerCompanion.Core.Domain;
using CareerCompanion.Core.LLM;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Services;

namespace CareerCompanion.Tests;

public sealed class AutomaticWorldTests : IDisposable
{
    private readonly string _dir=Path.Combine(Path.GetTempPath(),"touchline-world-tests-"+Guid.NewGuid().ToString("N"));
    private Database NewDb(){var db=new Database(Path.Combine(_dir,"world.db"));db.Migrate();return db;}
    private sealed class ReactionLlm:ILlmProvider{public string Name=>"Test";public Task<GenerationResult> GenerateAsync(LlmRequest request,CancellationToken cancellationToken=default)=>Task.FromResult(new GenerationResult("That was a massive performance. Keep setting the standard.","impressed",0,0,0,[],12,8));}

    [Fact] public void Important_match_creates_incoming_character_reactions_once()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","ST",9,"2017-09-01");var mate=db.AddCharacter(career,"Mate",24,"","Club","ST","Key player",CharacterType.Teammate);var manager=db.AddCharacter(career,"Boss",50,"","Club","Manager","Manager",CharacterType.Manager);var result=new CareerService(db).ProcessMatch(career,new("2017-09-02","League Final","Other",true,3,0,true,90,3,0,9.5,false,false,false,false,"",null,false,true));var service=new AutomaticWorldService(db);var first=service.ApplyMatch(result,true,true,true,true,true);var second=service.ApplyMatch(result,true,true,true,true,true);Assert.True(first.IncomingMessages>=2);Assert.NotEmpty(db.GetMessages(career,mate));Assert.NotEmpty(db.GetMessages(career,manager));Assert.Equal(first.Notifications,db.GetNotifications(career).Count);Assert.Equal(0,second.Notifications);
    }

    [Fact] public void Progress_snapshot_detects_grounded_changes()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Old Club","League","2017/18","CM",8);var old=new CareerProgressSnapshot(0,career,DateTime.UtcNow.AddDays(-1),"2017-09-01","Old Club","League","CM",8,70,3,false,2,0,0,0,0,"old");var current=old with{CapturedAt=DateTime.UtcNow,CareerDate="2017-09-08",Club="New Club",Overall=71,ShirtNumber=10,SourceFingerprint="new"};var service=new AutomaticWorldService(db);service.ApplyProgress(career,old,null);var changes=service.ApplyProgress(career,current,old);Assert.Contains(changes,x=>x.Contains("moved from"));Assert.Contains(changes,x=>x.Contains("overall rating"));Assert.Contains(db.GetEvents(career),x=>x.Type=="PLAYER_TRANSFERRED");
    }

    [Fact] public void Public_statement_consequences_are_idempotent()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8,"2017-09-01");var manager=db.AddCharacter(career,"Boss",45,"","Club","Manager","Manager",CharacterType.Manager);var service=new AutomaticWorldService(db);service.ApplyPublicStatement(career,7,0,"We did it as a team",70);var first=db.GetRelationship(manager);service.ApplyPublicStatement(career,7,0,"We did it as a team",70);var second=db.GetRelationship(manager);Assert.Equal(first,second);Assert.Single(db.GetMemories(manager),x=>x.Topic=="public statement");Assert.Single(db.GetEvents(career),x=>x.Type=="PUBLIC_STATEMENT");
    }

    [Fact] public void Retried_provider_match_reuses_match_and_events()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8,"2017-09-01");var service=new CareerService(db);var input=new MatchInput("2017-09-02","League","Other",true,2,0,true,90,1,0,8,false,false,false,false,"");var first=service.ProcessMatch(career,input,"FIFA 18 Save","event-1");var second=service.ProcessMatch(career,input with{Goals=3},"FIFA 18 Save","event-1");Assert.Equal(first.Match.Id,second.Match.Id);Assert.Equal(first.Events.Select(x=>x.Id),second.Events.Select(x=>x.Id));Assert.Single(db.GetMatches(career));Assert.Equal(1,db.GetMatch(career,first.Match.Id).Input.Goals);
    }

    [Fact] public async Task Pending_automatic_reaction_is_rewritten_by_llm()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8,"2017-09-01");var mate=db.AddCharacter(career,"Mate",24,"","Club","ST","Squad member",CharacterType.Teammate);var result=new CareerService(db).ProcessMatch(career,new("2017-09-02","Final","Other",true,3,0,true,90,3,0,9.5,false,false,false,false,""));new AutomaticWorldService(db).ApplyMatch(result,true,false,false,false,false);Assert.NotEmpty(db.GetPendingGenerationJobs(career,"automatic_reaction_llm",20));var generated=await new AutomaticDialogueService(db,new ReactionLlm()).ProcessPendingAsync(career,"routine","premium",true,20);Assert.True(generated.Completed>0);Assert.Equal("That was a massive performance. Keep setting the standard.",db.GetMessages(career,mate).Last().Content);Assert.Contains(db.GetNotifications(career),x=>x.Title=="Mate"&&x.Body.StartsWith("That was a massive"));Assert.Empty(db.GetPendingGenerationJobs(career,"automatic_reaction_llm",20));
    }

    public void Dispose(){try{Directory.Delete(_dir,true);}catch{}}
}
