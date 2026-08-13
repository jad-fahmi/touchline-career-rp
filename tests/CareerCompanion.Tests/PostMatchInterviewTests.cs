using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Services;
using CareerCompanion.Core.LLM;
using System.Text.Json;

namespace CareerCompanion.Tests;

public sealed class PostMatchInterviewTests : IDisposable
{
    private sealed class JournalistProvider:ILlmProvider
    {
        public string Name=>"Test";public LlmRequest? Request{get;private set;}
        public Task<GenerationResult> GenerateAsync(LlmRequest request,CancellationToken cancellationToken=default){Request=request;return Task.FromResult(new GenerationResult("You credit the team, but what changed after half-time?","neutral",0,0,0,[],12,8));}
    }
    private readonly string _dir=Path.Combine(Path.GetTempPath(),"touchline-interview-tests-"+Guid.NewGuid().ToString("N"));
    private Database NewDb(){var db=new Database(Path.Combine(_dir,"world.db"));db.Migrate();return db;}
    private static MatchInput Match(double rating=7,bool major=false,int goals=0,bool red=false)=>new("2017-09-01","League","Opponent",true,1,0,true,90,goals,0,rating,false,red,false,false,"",null,false,major);

    [Fact] public void Routine_match_does_not_trigger_interview()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8);var result=new CareerService(db).ProcessMatch(career,Match());Assert.Null(new PostMatchInterviewService(db).CreateIfEligible(career,result,true));
    }

    [Fact] public void Exceptional_performance_creates_persistent_multi_question_interview()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","ST",9);var result=new CareerService(db).ProcessMatch(career,Match(9.2,goals:2));var interview=Assert.IsType<PostMatchInterview>(new PostMatchInterviewService(db).CreateIfEligible(career,result,true));Assert.Equal(3,(JsonSerializer.Deserialize<List<string>>(interview.QuestionsJson)??[]).Count);Assert.Equal(interview.Id,NewDb().GetPendingPostMatchInterview(career)!.Id);
    }

    [Fact] public void Completed_interview_is_removed_from_pending_queue()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","CM",8);var result=new CareerService(db).ProcessMatch(career,Match(major:true));var interview=new PostMatchInterviewService(db).CreateIfEligible(career,result,true)!;db.UpdatePostMatchInterview(interview.Id,"[\"Answer\"]",3,"Completed");Assert.Null(db.GetPendingPostMatchInterview(career));
    }

    [Fact] public async Task Journalist_reacts_to_answer_with_verified_match_context()
    {
        var db=NewDb();var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","ST",9);var result=new CareerService(db).ProcessMatch(career,Match(9.2,goals:2));var service=new PostMatchInterviewService(db);var interview=service.CreateIfEligible(career,result,true)!;var provider=new JournalistProvider();var reply=await service.GenerateReplyAsync(interview,"What made the difference?","The team created space for me.","Can you maintain this form?",provider,"test-model");Assert.True(reply.AiGenerated);Assert.Contains("what changed",reply.JournalistResponse);Assert.Contains("Opponent",provider.Request!.UserPrompt);Assert.Contains("team created space",provider.Request.UserPrompt);
    }

    public void Dispose(){try{Directory.Delete(_dir,true);}catch{}}
}
