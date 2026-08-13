using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Simulation;

namespace CareerCompanion.Core.Services;

public sealed record AutomaticWorldResult(int IncomingMessages,int Notifications);

public sealed class AutomaticWorldService(Database db)
{
    public AutomaticWorldResult ApplyMatch(MatchProcessingResult result,bool teammates,bool managers,bool interviewCreated,bool newsCreated,bool socialCreated)
    {
        var career=db.GetCareer(result.Match.CareerId);var characters=db.GetCharacters(career.Id).ToDictionary(x=>x.Id);var messages=0;var notices=0;
        foreach(var target in result.Reactions.Where(x=>x.CharacterId is not null).DistinctBy(x=>x.CharacterId).OrderByDescending(x=>x.Priority))
        {
            if(!characters.TryGetValue(target.CharacterId!.Value,out var character)||!IsActive(character))continue;
            if(character.Type==CharacterType.Manager&&!managers||character.Type==CharacterType.Teammate&&!teammates)continue;
            if(character.Type is not(CharacterType.Manager or CharacterType.Teammate))continue;
            var evt=result.Events.FirstOrDefault(x=>x.Importance+((character.Type==CharacterType.Manager)?18:7)>=target.Priority)??result.Events.First();
            var dedupe=$"reaction:{evt.Id}:{character.Id}";
            var text=Reaction(character,career,result.Match,evt);var scene=character.Type==CharacterType.Manager?SceneType.ManagerOffice:SceneType.PostMatch;
            var context=System.Text.Json.JsonSerializer.Serialize(new{automatic=true,eventId=evt.Id,classification=FactClassification.SimulatedInterpretation.ToString()});
            if(db.AddAutomaticReaction(career.Id,character.Id,evt.Id,scene,context,text,Math.Clamp(evt.Importance,30,75),Tone(evt),"automatic post-match reaction",character.Type==CharacterType.Manager?"Manager":"Message",character.Name,$"Messages:{character.Id}",target.Priority,dedupe,evt.Timestamp)){messages++;notices++;}
        }
        var top=result.Events.FirstOrDefault();if(newsCreated&&top is not null&&db.AddNotification(career.Id,"News","New coverage after "+result.Match.Input.Opponent,top.Summary,"News",top.Importance,$"news:{result.Match.Id}",top.Timestamp))notices++;
        if(socialCreated&&top is not null&&db.AddNotification(career.Id,"Social","The football world is reacting",top.Summary,"Social",Math.Max(30,top.Importance-5),$"social:{result.Match.Id}",top.Timestamp))notices++;
        if(interviewCreated&&db.AddNotification(career.Id,"Interview","Post-match interview requested","The media wants to hear from you after "+result.Match.Input.Opponent+".","Press",80,$"interview:{result.Match.Id}",top?.Timestamp))notices++;
        if(top is not null){var engine=new CharacterStateEngine();var states=characters.Values.Where(x=>x.Type is CharacterType.Manager or CharacterType.Teammate).Where(IsActive).Select(character=>engine.AfterMatch(character,db.GetCharacterState(character.Id),result.Match,top)).ToList();db.SaveCharacterStatesOnce(career.Id,$"match-state:{career.Id}:{result.Match.Id}",states);}
        return new(messages,notices);
    }

    public IReadOnlyList<string> ApplyProgress(long careerId,CareerProgressSnapshot current,CareerProgressSnapshot? previous)
    {
        var changes=new List<string>();if(previous is null){db.AddCareerProgressSnapshot(current);return changes;}if(previous.SourceFingerprint==current.SourceFingerprint)return changes;
        void Notice(string kind,string title,string body,int priority,string suffix){db.AddNotification(careerId,kind,title,body,"Timeline",priority,$"progress:{current.SourceFingerprint}:{suffix}",DateTime.TryParse(current.CareerDate,out var date)?date:current.CapturedAt);changes.Add(body);}
        if(!string.Equals(previous.Club,current.Club,StringComparison.OrdinalIgnoreCase))Notice("Career","Transfer confirmed",$"Your FIFA career moved from {previous.Club} to {current.Club}.",95,"club");
        if(previous.Overall>0&&current.Overall>previous.Overall)Notice("Development","Overall increased",$"Your overall rating rose from {previous.Overall} to {current.Overall}.",70,"overall");
        if(current.Position!=previous.Position)Notice("Development","Position changed",$"Your FIFA position changed from {previous.Position} to {current.Position}.",65,"position");
        if(current.ShirtNumber!=previous.ShirtNumber)Notice("Career","New shirt number",$"Your shirt number changed from {previous.ShirtNumber} to {current.ShirtNumber}.",45,"number");
        if(current.Injured&&!previous.Injured)Notice("Fitness","Injury detected","FIFA now lists you as injured.",85,"injury");
        if(!current.Injured&&previous.Injured)Notice("Fitness","Return from injury","FIFA no longer lists you as injured.",75,"return");
        foreach(var change in changes){var type=change.Contains("moved from",StringComparison.OrdinalIgnoreCase)?"PLAYER_TRANSFERRED":change.Contains("injured",StringComparison.OrdinalIgnoreCase)?"PLAYER_INJURY_STATUS":"PLAYER_DEVELOPMENT";db.SaveEvent(new(0,careerId,null,type,DateTime.TryParse(current.CareerDate,out var date)?date:current.CapturedAt,70,"[]",System.Text.Json.JsonSerializer.Serialize(new{sourceFingerprint=current.SourceFingerprint}),change,FactClassification.SaveFact));}db.AddCareerProgressSnapshot(current);
        return changes;
    }

    public void ApplyPublicStatement(long careerId,long interviewId,int questionIndex,string answer,int importance)
    {
        var career=db.GetCareer(careerId);var timestamp=DateTime.TryParse(career.CurrentDate,out var date)?date:DateTime.UtcNow;var key=$"statement:{interviewId}:{questionIndex}";db.AddNotification(careerId,"Interview","Your statement entered the career world",answer,"Press",Math.Clamp(importance,40,90),key,timestamp);var metadata=System.Text.Json.JsonSerializer.Serialize(new{interviewId,questionIndex});var evtId=db.SaveEvent(new(0,careerId,null,"PUBLIC_STATEMENT",timestamp,Math.Clamp(importance,35,85),"[]",metadata,"Public statement: "+answer,FactClassification.HistoricalFact));if(importance>=65)db.AddSocial(careerId,evtId,"@TouchlineLive","media transcript",$"{career.PlayerName}: \"{answer}\"",timestamp);var teamFirst=answer.Contains("team",StringComparison.OrdinalIgnoreCase)||answer.Contains("we ",StringComparison.OrdinalIgnoreCase);var blame=answer.Contains("blame",StringComparison.OrdinalIgnoreCase)||answer.Contains("fault",StringComparison.OrdinalIgnoreCase);var delta=teamFirst?2:blame?-2:0;var engine=new RelationshipEngine();var relationships=db.GetCharacters(careerId).Where(x=>x.Type is CharacterType.Manager or CharacterType.Teammate).Where(IsActive).Select(character=>{var current=db.GetRelationship(character.Id);return delta==0?current:engine.Apply(current,delta,teamFirst?1:-1,teamFirst?1:-1);}).ToList();db.ApplyStatementConsequencesOnce(careerId,$"statement-consequences:{interviewId}:{questionIndex}",evtId,relationships,$"Public statement after the match: {answer}",Math.Clamp(importance,40,75),delta*10,timestamp);
    }

    private static string Reaction(Character character,Career career,CareerMatch match,CareerEvent evt)
    {
        var input=match.Input;if(character.Type==CharacterType.Manager)return evt.Type switch{"PLAYER_RED_CARD"=>"We need to talk about the sending-off. Discipline matters, and I expect a response.","PLAYER_HATTRICK"=>"Outstanding finishing today. Enjoy it, then prepare to meet that standard again.","LARGE_DEFEAT"=>"That result was not acceptable. We reset, work, and show a response in the next match.","PLAYER_BENCHED"=>"Keep working. Selection follows what I see in training and matches.",_=>input.TeamScore>input.OpponentScore?"Good result. Stay focused because the next match arrives quickly.":"Review your performance honestly. We need a stronger response next time."};
        var seed=unchecked(character.Id*397+evt.Id);return evt.Type switch{"PLAYER_HATTRICK"=>seed%2==0?$"Unreal performance, {career.PlayerName}. You were unplayable today.":"Three goals. Save some for the rest of us next time.","PLAYER_RED_CARD"=>"That was a costly moment. We need you on the pitch, not watching from the tunnel.","LARGE_DEFEAT"=>"Tough one. No excuses, but we stay together and respond.","LATE_WINNER"=>"I still cannot believe that finish. The dressing room erupted.",_=>input.TeamScore>input.OpponentScore?"Big result today. Well played.":input.TeamScore<input.OpponentScore?"That one hurts. We have to respond together.":"A point, but it feels like there was more there for us."};
    }
    private static int Tone(CareerEvent evt)=>evt.Type.Contains("LOST")||evt.Type.Contains("RED")||evt.Type.Contains("DEFEAT")?-35:35;
    private static bool IsActive(Character character){try{using var json=System.Text.Json.JsonDocument.Parse(character.FactsJson);return !json.RootElement.TryGetProperty("providerActive",out var active)||active.ValueKind!=System.Text.Json.JsonValueKind.False;}catch(System.Text.Json.JsonException){return true;}}
}
