using CareerCompanion.Core.Domain;
using CareerCompanion.Core.LLM;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Services;

namespace CareerCompanion.Tests;

public sealed class AutomaticWorldTests : IDisposable
{
    private readonly string _dir=Path.Combine(Path.GetTempPath(),"touchline-world-tests-"+Guid.NewGuid().ToString("N"));
    private Database NewDb(){var db=new Database(Path.Combine(_dir,"world.db"));db.Migrate();return db;}
    private sealed class ExplodingLlm:ILlmProvider{public string Name=>"Test";public Task<GenerationResult> GenerateAsync(LlmRequest request,CancellationToken cancellationToken=default)=>throw new UriFormatException("the configured endpoint is not a valid address");}

    [Fact] public async Task A_broken_model_endpoint_falls_back_to_offline_dialogue_without_losing_the_message()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8,"2017-09-01");
        var mate=db.AddCharacter(career,"Mate",24,"","Club","ST","Squad member",CharacterType.Teammate);
        var reply=await new ConversationService(db,new ExplodingLlm()).SendAsync(career,mate,SceneType.PrivateMessage,
            "What do you actually think of the manager's tactics this season?","compatible:broken");
        Assert.False(string.IsNullOrWhiteSpace(reply.Text));
        Assert.StartsWith("offline-library:",reply.Raw);
        var messages=db.GetMessages(career,mate);
        Assert.Contains(messages,x=>x.Role=="user");
        Assert.Contains(messages,x=>x.Role=="assistant");
    }

    private sealed class CountingLlm:ILlmProvider{public string Name=>"Test";public int Calls;public Task<GenerationResult> GenerateAsync(LlmRequest request,CancellationToken cancellationToken=default){Calls++;return Task.FromResult(new GenerationResult("A considered answer.","steady",0,0,0,[],4,5));}}

    [Fact] public void Important_match_creates_incoming_character_reactions_once()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","ST",9,"2017-09-01");var mate=db.AddCharacter(career,"Mate",24,"","Club","ST","Key player",CharacterType.Teammate);var manager=db.AddCharacter(career,"Boss",50,"","Club","Manager","Manager",CharacterType.Manager);var result=new CareerService(db).ProcessMatch(career,new("2017-09-02","League Final","Other",true,3,0,true,90,3,0,9.5,false,false,false,false,"",null,false,true));var service=new AutomaticWorldService(db);var first=service.ApplyMatch(result,true,true,true,true,true);var second=service.ApplyMatch(result,true,true,true,true,true);Assert.True(first.IncomingMessages>=2);Assert.NotEmpty(db.GetMessages(career,mate));Assert.NotEmpty(db.GetMessages(career,manager));Assert.Equal(first.Notifications,db.GetNotifications(career).Count);Assert.Equal(0,second.Notifications);
    }

    [Fact] public void Post_match_message_volume_is_proportional_to_the_event()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","ST",9,"2017-09-01");db.AddCharacter(career,"Boss",50,"","Club","Manager","Manager",CharacterType.Manager);for(var i=0;i<6;i++)db.AddCharacter(career,$"Mate {i}",22+i,"","Club","ST","Squad member",CharacterType.Teammate);
        var service=new AutomaticWorldService(db);var careers=new CareerService(db);
        var routine=careers.ProcessMatch(career,new("2017-09-02","League","Other",true,1,0,true,90,0,0,7,false,false,false,false,""));
        var routineApplied=service.ApplyMatch(routine,true,true,false,false,false);
        var hattrick=careers.ProcessMatch(career,new("2017-09-09","League","Another",true,3,0,true,90,3,0,9,false,false,false,false,""));
        var hattrickApplied=service.ApplyMatch(hattrick,true,true,false,false,false);
        Assert.InRange(routineApplied.IncomingMessages,1,2);
        Assert.True(hattrickApplied.IncomingMessages>routineApplied.IncomingMessages,
            $"a hat-trick should draw more voices than a routine win ({hattrickApplied.IncomingMessages} vs {routineApplied.IncomingMessages})");
    }

    [Fact] public void Progress_snapshot_detects_grounded_changes()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Old Club","League","2017/18","CM",8);var old=new CareerProgressSnapshot(0,career,DateTime.UtcNow.AddDays(-1),"2017-09-01","Old Club","League","CM",8,70,3,false,2,0,0,0,0,"old");var current=old with{CapturedAt=DateTime.UtcNow,CareerDate="2017-09-08",Club="New Club",Overall=71,ShirtNumber=10,SourceFingerprint="new"};var service=new AutomaticWorldService(db);service.ApplyProgress(career,old,null);var changes=service.ApplyProgress(career,current,old);Assert.Contains(changes,x=>x.Contains("completed a move"));Assert.Contains(changes,x=>x.Contains("overall rating"));Assert.DoesNotContain(changes,x=>x.Contains("FIFA",StringComparison.OrdinalIgnoreCase));Assert.Contains(db.GetEvents(career),x=>x.Type=="PLAYER_TRANSFERRED");
    }

    [Fact] public void Transfer_request_creates_manager_teammate_and_agent_conversations_once()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8,"2017-09-01");var mate=db.AddCharacter(career,"Mate",24,"","Club","ST","Squad member",CharacterType.Teammate);var secondMate=db.AddCharacter(career,"Second Mate",25,"","Club","CM","Squad member",CharacterType.Teammate);var manager=db.AddCharacter(career,"Boss",45,"","Club","Manager","Manager",CharacterType.Manager);var agent=db.AddCharacter(career,"Agent",40,"","","Agent","Representative",CharacterType.Agent);var signal=new CareerCompanion.Core.Providers.Fifa18.Fifa18TransferRequestSignal("request-1","2017-09-05","Requested","FIFA news: player handed in a transfer request.");var service=new AutomaticWorldService(db);var first=service.ApplyTransferRequest(career,signal);var second=service.ApplyTransferRequest(career,signal);Assert.True(first>=5);Assert.Equal(0,second);Assert.NotEmpty(db.GetMessages(career,manager));Assert.NotEmpty(db.GetMessages(career,mate));Assert.NotEmpty(db.GetMessages(career,secondMate));Assert.NotEmpty(db.GetMessages(career,agent));Assert.Contains(db.GetEvents(career),x=>x.Type=="PLAYER_TRANSFER_REQUESTED");
    }

    [Fact] public void Public_statement_consequences_are_idempotent()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8,"2017-09-01");var manager=db.AddCharacter(career,"Boss",45,"","Club","Manager","Manager",CharacterType.Manager);var service=new AutomaticWorldService(db);service.ApplyPublicStatement(career,7,0,"We did it as a team",70);var first=db.GetRelationship(manager);service.ApplyPublicStatement(career,7,0,"We did it as a team",70);var second=db.GetRelationship(manager);Assert.Equal(first,second);Assert.Single(db.GetMemories(manager),x=>x.Topic=="public statement");Assert.Single(db.GetEvents(career),x=>x.Type=="PUBLIC_STATEMENT");
    }

    [Fact] public void Retried_provider_match_reuses_match_and_events()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8,"2017-09-01");var service=new CareerService(db);var input=new MatchInput("2017-09-02","League","Other",true,2,0,true,90,1,0,8,false,false,false,false,"");var first=service.ProcessMatch(career,input,"FIFA 18 Save","event-1");var second=service.ProcessMatch(career,input with{Goals=3},"FIFA 18 Save","event-1");Assert.Equal(first.Match.Id,second.Match.Id);Assert.Equal(first.Events.Select(x=>x.Id),second.Events.Select(x=>x.Id));Assert.Single(db.GetMatches(career));Assert.Equal(1,db.GetMatch(career,first.Match.Id).Input.Goals);
    }

    [Fact] public void Automatic_reactions_stay_offline_and_never_queue_a_model_request()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8,"2017-09-01");var mate=db.AddCharacter(career,"Mate",24,"","Club","ST","Squad member",CharacterType.Teammate);
        var result=new CareerService(db).ProcessMatch(career,new("2017-09-02","Final","Other",true,3,0,true,90,3,0,9.5,false,false,false,false,""));
        new AutomaticWorldService(db).ApplyMatch(result,true,false,false,false,false);
        Assert.Empty(db.GetPendingGenerationJobs(career,"automatic_reaction_llm",20));
        Assert.NotEmpty(db.GetMessages(career,mate));
    }

    [Fact] public async Task Offline_conversations_use_character_and_intent_specific_fallbacks()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8,"2017-09-01");var manager=db.AddCharacter(career,"Boss",45,"","Club","Manager","Manager",CharacterType.Manager);var mate=db.AddCharacter(career,"Mate",24,"","Club","ST","Squad member",CharacterType.Teammate);var service=new ConversationService(db,new OfflineLlmProvider());
        var managerReply=await service.SendAsync(career,manager,SceneType.ManagerOffice,"I am worried about being benched again.","offline");var mateReply=await service.SendAsync(career,mate,SceneType.PrivateMessage,"I am worried about being benched again.","offline");
        Assert.NotEqual(managerReply.Text,mateReply.Text);Assert.Contains(db.GetMessages(career,manager),x=>x.Role=="assistant");Assert.Contains(db.GetMessages(career,mate),x=>x.Role=="assistant");
    }

    [Fact] public async Task Conversation_routing_keeps_only_bare_greetings_offline()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8,"2017-09-01");var mate=db.AddCharacter(career,"Mate",24,"","Club","ST","Squad member",CharacterType.Teammate);var llm=new CountingLlm();var service=new ConversationService(db,llm);
        var greeting=await service.SendAsync(career,mate,SceneType.PrivateMessage,"Hi","test");Assert.Equal(0,llm.Calls);Assert.StartsWith("offline-library:",greeting.Raw,StringComparison.Ordinal);
        var named=await service.SendAsync(career,mate,SceneType.PrivateMessage,"Morning mate","test");Assert.Equal(0,llm.Calls);Assert.StartsWith("offline-library:",named.Raw,StringComparison.Ordinal);
        // A greeting that carries a second line is a real message, so it goes to the model.
        var opener=await service.SendAsync(career,mate,SceneType.PrivateMessage,"Hey. Did you see the second goal?","test");Assert.Equal(1,llm.Calls);Assert.Equal("A considered answer.",opener.Text);
        var known=await service.SendAsync(career,mate,SceneType.PrivateMessage,"Can you help me with training?","test");Assert.Equal(2,llm.Calls);Assert.Equal("A considered answer.",known.Text);
        var statement=await service.SendAsync(career,mate,SceneType.PrivateMessage,"I am worried about being benched again.","test");Assert.Equal(3,llm.Calls);Assert.Equal("A considered answer.",statement.Text);
        var open=await service.SendAsync(career,mate,SceneType.PrivateMessage,"What do you think of Cristiano?","test");Assert.Equal(4,llm.Calls);Assert.Equal("A considered answer.",open.Text);Assert.Equal(9,open.InputTokens+open.OutputTokens);
    }

    private sealed class ScriptedLlm(params GenerationResult[] replies):ILlmProvider
    {
        public string Name=>"Test";public int Calls;
        public Task<GenerationResult> GenerateAsync(LlmRequest request,CancellationToken cancellationToken=default)
        {
            var reply=replies[Math.Min(Calls,replies.Length-1)];Calls++;Prompts.Add(request.SystemPrompt);return Task.FromResult(reply);
        }
        public List<string> Prompts{get;}=[];
    }

    [Fact] public async Task A_badly_formatted_first_reply_is_retried_instead_of_falling_back_offline()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8,"2017-09-01");var mate=db.AddCharacter(career,"Mate",24,"","Club","ST","Squad member",CharacterType.Teammate);
        var llm=new ScriptedLlm(new("<think>They want reassurance</think>","steady",0,0,0,[],3,3),new("Mate: keep your head up, we go again Saturday.","steady",0,0,0,[],3,4));
        var reply=await new ConversationService(db,llm).SendAsync(career,mate,SceneType.PrivateMessage,"I am worried about being benched again.","test");
        Assert.Equal(2,llm.Calls);
        Assert.DoesNotContain("offline-library:",reply.Raw,StringComparison.Ordinal);
        Assert.Equal("keep your head up, we go again Saturday.",reply.Text);
        Assert.Contains("CORRECTION",llm.Prompts[1],StringComparison.Ordinal);
    }

    [Fact] public async Task A_reply_that_mentions_the_game_is_rejected_and_regenerated()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8,"2017-09-01");var manager=db.AddCharacter(career,"Boss",45,"","Club","Manager","Manager",CharacterType.Manager);
        var llm=new ScriptedLlm(new("FIFA has not decided whether you will play yet.","steady",0,0,0,[],2,2),new("I name the side on Friday. Train like you are starting.","steady",0,0,0,[],2,3));
        var reply=await new ConversationService(db,llm).SendAsync(career,manager,SceneType.ManagerOffice,"Am I playing on Saturday?","test");
        Assert.Equal(2,llm.Calls);
        Assert.Equal("I name the side on Friday. Train like you are starting.",reply.Text);
        Assert.DoesNotContain("FIFA",reply.Text,StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void Pre_match_briefing_creates_scouting_and_proactive_messages_once()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8,"2017-09-01");var mate=db.AddCharacter(career,"Mate",24,"","Club","ST","Squad member",CharacterType.Teammate);var manager=db.AddCharacter(career,"Boss",45,"","Club","Manager","Manager",CharacterType.Manager);db.UpsertFixture(career,"FIFA 18 Save","fixture-1","2017-09-03","League","Rivals",false,90,"FIFA preview","fp");var fixture=db.GetFixtures(career).Single();var scout=new CareerCompanion.Core.Providers.Fifa18.Fifa18OpponentScout("Rivals","Other Boss","Away Ground",true,[new("Threat","ST",88)],"FIFA save squad snapshot");var service=new AutomaticWorldService(db);var first=service.ApplyPreMatch(career,fixture,scout);var second=service.ApplyPreMatch(career,fixture,scout);Assert.True(first>=3);Assert.Equal(0,second);Assert.NotEmpty(db.GetMessages(career,mate));Assert.NotEmpty(db.GetMessages(career,manager));Assert.Contains(db.GetEvents(career),x=>x.Type=="PRE_MATCH_BRIEFING");
    }

    [Fact] public void Routine_loss_hurts_without_forcing_a_crisis_scene()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8,"2017-09-01");var result=new CareerService(db).ProcessMatch(career,new("2017-09-02","League","Other",true,0,1,true,90,0,0,6.5,false,false,false,false,""));var psychology=new PlayerPsychologyService(db).ApplyMatch(result,true,true);Assert.Equal("disappointed",psychology.State.Mood);Assert.False(psychology.State.NeedsSupport);Assert.Equal(0,psychology.SupportMessages);Assert.True(psychology.State.Confidence<55);Assert.True(psychology.State.Pressure>25);
    }

    [Fact] public void Devastating_loss_triggers_private_relationship_aware_support()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","ST",9,"2017-09-01");var agent=db.AddCharacter(career,"Agent",40,"","","Agent","Representative",CharacterType.Agent);var mate=db.AddCharacter(career,"Close Mate",24,"","Club","CM","Squad member",CharacterType.Teammate);db.SaveRelationship(new(mate,Score:35,Trust:40,Friendliness:35,Familiarity:30));var result=new CareerService(db).ProcessMatch(career,new("2017-09-02","Cup Final","Rivals",true,0,1,true,90,0,0,4.5,false,false,false,true,"",null,true,true));var psychology=new PlayerPsychologyService(db).ApplyMatch(result,true,true);Assert.True(psychology.State.NeedsSupport);Assert.True(psychology.Severity>=22);Assert.Equal(2,psychology.SupportMessages);Assert.Contains(db.GetMessages(career,agent),x=>x.Content.Contains("Do not sit with this alone"));Assert.NotEmpty(db.GetMessages(career,mate));Assert.Contains(db.GetEvents(career),x=>x.Type=="PLAYER_EMOTIONAL_STATE"&&x.Classification==FactClassification.SimulatedInterpretation);
    }

    [Fact] public void Recovery_choice_is_persistent_and_only_available_once_per_match()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","ST",9,"2017-09-01");var agent=db.AddCharacter(career,"Agent",40,"","","Agent","Representative",CharacterType.Agent);var result=new CareerService(db).ProcessMatch(career,new("2017-09-02","Final","Rivals",true,0,2,true,90,0,0,4.5,false,false,false,true,"",null,false,true));new PlayerPsychologyService(db).ApplyMatch(result,true,true);var before=db.GetPlayerState(career);var first=new PlayerPsychologyService(db).ChooseRecovery(career,"open_up");var second=new PlayerPsychologyService(db).ChooseRecovery(career,"training");Assert.True(first.Applied);Assert.Equal(agent,first.SupporterId);Assert.True(first.State.Isolation<before.Isolation);Assert.False(second.Applied);Assert.Equal(first.State,new Database(Path.Combine(_dir,"world.db")).GetPlayerState(career));
    }

    public void Dispose(){try{Directory.Delete(_dir,true);}catch{}}
}
