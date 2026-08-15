using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Services;

namespace CareerCompanion.Tests;

/// <summary>
/// Covers the two promises made about generated dialogue: a reply is only discarded when nothing usable
/// is left, and nothing a character says may mention the game the career is played in.
/// </summary>
public sealed class DialogueGuardTests : IDisposable
{
    private readonly string _dir=Path.Combine(Path.GetTempPath(),"touchline-guard-tests-"+Guid.NewGuid().ToString("N"));
    private Database NewDb(){var db=new Database(Path.Combine(_dir,"guard.db"));db.Migrate();return db;}

    [Theory]
    [InlineData("{\"dialogue\":\"Keep your head up.\",\"mood\":\"steady\"}","Keep your head up.")]
    [InlineData("{\"dialogue\":\"Keep your head up.\",\"mood\":","Keep your head up.")]
    [InlineData("```json\n{\"dialogue\":\"Long week, that one.\"}\n```","Long week, that one.")]
    [InlineData("<think>He is upset</think>Take tonight off, we talk tomorrow.","Take tonight off, we talk tomorrow.")]
    [InlineData("Boss: come and see me in the morning.","Come and see me in the morning.")]
    [InlineData("\"Not your best, but not the end of it.\"","Not your best, but not the end of it.")]
    public void Damaged_formatting_is_repaired_rather_than_discarded(string raw,string expected)
    {
        Assert.True(DialogueResponseGuard.TryPrepare(raw,"Boss",out var dialogue,out var rejection));
        Assert.Equal(DialogueResponseGuard.Rejection.None,rejection);
        Assert.Equal(expected,dialogue,ignoreCase:true);
    }

    [Theory]
    [InlineData("FIFA has not decided whether you will play yet.")]
    [InlineData("The game hasn't decided if you are starting.")]
    [InlineData("Your save does not have the final score, so I cannot say.")]
    [InlineData("The data has not come through for that match.")]
    [InlineData("As an AI I cannot know the team sheet.")]
    public void Dialogue_that_steps_outside_the_football_world_is_rejected(string raw)
    {
        Assert.False(DialogueResponseGuard.TryPrepare(raw,"Boss",out _,out var rejection));
        Assert.Equal(DialogueResponseGuard.Rejection.FourthWall,rejection);
    }

    [Theory]
    [InlineData("What a save from him at the end.")]
    [InlineData("We played a back three, the system suited you.")]
    [InlineData("You are the engine room of that midfield.")]
    [InlineData("The game will decide itself in the last twenty minutes.")]
    // The career's own competition names include "FIFA WC Qualifiers", and FIFA is a real governing body.
    [InlineData("Two more FIFA WC Qualifiers before the break, so look after yourself.")]
    [InlineData("A FIFA World Cup year changes how everyone watches you.")]
    public void Ordinary_football_language_is_not_mistaken_for_a_fourth_wall_break(string raw)
    {
        Assert.True(DialogueResponseGuard.TryPrepare(raw,"Boss",out var dialogue,out _));
        Assert.Equal(raw,dialogue);
    }

    [Theory]
    // A journalist once printed "User Safety: safe" into a press conference: the model labelled its own
    // output instead of speaking, which is neither a prompt echo nor a fourth-wall break.
    [InlineData("User Safety: safe")]
    [InlineData("Sentiment: positive")]
    [InlineData("Content policy: no violation found")]
    [InlineData("Toxicity: none")]
    public void A_model_labelling_its_own_output_is_not_dialogue(string raw)
        => Assert.False(DialogueResponseGuard.TryPrepare(raw, "journalist", out _, out _));

    [Theory]
    // Real dialogue can still contain a colon, so only a bare label with no sentence is refused.
    [InlineData("Listen: you were the best player on that pitch.")]
    [InlineData("One thing: keep your head up on Saturday.")]
    public void A_colon_inside_a_real_sentence_is_left_alone(string raw)
    {
        Assert.True(DialogueResponseGuard.TryPrepare(raw, "journalist", out var dialogue, out _));
        Assert.Equal(raw, dialogue);
    }

    // The press room asked "Two goals against Cagliari. What made the difference for you today?", and the
    // model answered with that same question plus the player's reply glued on. Nothing in the reply is
    // prompt text or a fourth-wall break, so it was stored and shown back as the next question.
    [Theory]
    [InlineData("Two goals against Cagliari. What made the difference for you today? I missed a lot of shots before those two goals, my teammates were getting frustrated, so I had to score.")]
    [InlineData("Two goals against Cagliari. What made the difference for you today?")]
    [InlineData("I missed a lot of shots before those two goals, my teammates were getting frustrated, so I had to score.")]
    public void A_reply_that_hands_back_the_question_or_the_answer_is_refused(string raw)
    {
        Assert.False(DialogueResponseGuard.TryPrepare(raw,"journalist",Interview,out _,out var rejection));
        Assert.Equal(DialogueResponseGuard.Rejection.Parroted,rejection);
    }

    [Theory]
    // Quoting a phrase back is how a journalist pushes, so a short repeat under a new line must survive.
    [InlineData("You say you had to score. Does that pressure come from the dressing room or from yourself?")]
    [InlineData("Frustrated teammates, then two goals. Is that the reaction you expect of yourself now?")]
    [InlineData("So the misses did not stay in your head. What changes for the trip to Napoli?")]
    public void A_journalist_quoting_a_phrase_back_is_not_mistaken_for_a_copy(string raw)
    {
        Assert.True(DialogueResponseGuard.TryPrepare(raw,"journalist",Interview,out var dialogue,out _));
        Assert.Equal(raw,dialogue);
    }

    private static readonly string[] Interview=[
        "Two goals against Cagliari. What made the difference for you today?",
        "I missed a lot of shots before those two goals, my teammates were getting frustrated, so I had to score."
    ];

    [Fact]
    public void Leaked_prompt_or_reasoning_text_is_still_refused()
    {
        Assert.False(DialogueResponseGuard.TryPrepare("Relationship: score 10, trust 4. User: hello","Boss",out _,out var rejection));
        Assert.Equal(DialogueResponseGuard.Rejection.PromptEcho,rejection);
    }

    [Theory]
    [InlineData("hey",true)]
    [InlineData("Hi",true)]
    [InlineData("Morning mate",true)]
    [InlineData("good morning boss",true)]
    [InlineData("hey Marco",true)]
    [InlineData("Hey. Did you see the second goal?",false)]
    [InlineData("hey, quick one about Saturday",false)]
    [InlineData("morning, can we talk?",false)]
    [InlineData("I am worried about being benched again.",false)]
    [InlineData("Thanks for yesterday",false)]
    public void Only_a_bare_greeting_stays_offline(string message,bool offline)
        =>Assert.Equal(offline,OfflineDialogueLibrary.IsBareGreeting(message));

    [Fact]
    public void Two_people_checking_in_after_the_same_defeat_do_not_send_the_same_words()
    {
        var db=NewDb();
        var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","ST",9,"2017-09-01");
        db.AddCharacter(career,"First Mate",24,"","Club","CM","Key player",CharacterType.Teammate);
        db.AddCharacter(career,"Second Mate",26,"","Club","CB","Key player",CharacterType.Teammate);
        db.AddCharacter(career,"Boss",48,"","Club","Manager","Manager",CharacterType.Manager);
        var result=new CareerService(db).ProcessMatch(career,new("2017-09-02","League Final","Other",true,0,4,true,90,0,0,4.5,false,false,true,true,""));
        new AutomaticWorldService(db).ApplyMatch(result,true,true,false,false,false);
        var lines=db.GetCharacters(career).SelectMany(x=>db.GetMessages(career,x.Id)).Where(x=>x.Role=="assistant")
            .Select(x=>x.Content.Trim()).ToList();
        Assert.True(lines.Count>=2,"a heavy defeat should draw more than one voice");
        Assert.Equal(lines.Count,lines.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void The_manager_and_a_teammate_do_not_repeat_each_other_before_a_fixture()
    {
        var db=NewDb();
        var career=db.CreateCareer("Save","Player","",20,"Club","League","2017/18","ST",9,"2017-09-01");
        var mate=db.AddCharacter(career,"Mate",24,"","Club","CM","Key player",CharacterType.Teammate);
        var manager=db.AddCharacter(career,"Boss",48,"","Club","Manager","Manager",CharacterType.Manager);
        db.UpsertFixture(career,"FIFA 18 Save","fixture-1","2017-09-03","League","Rivals",false,90,"preview","fp");
        new AutomaticWorldService(db).ApplyPreMatch(career,db.GetFixtures(career).Single(),null);
        var managerLine=db.GetMessages(career,manager).Single(x=>x.Role=="assistant").Content;
        var mateLine=db.GetMessages(career,mate).Single(x=>x.Role=="assistant").Content;
        Assert.NotEqual(managerLine,mateLine);
        Assert.DoesNotContain("FIFA",managerLine,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FIFA",mateLine,StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose(){try{Directory.Delete(_dir,true);}catch{}}
}
