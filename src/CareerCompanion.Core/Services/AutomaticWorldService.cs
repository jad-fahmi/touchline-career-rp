using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Simulation;
using CareerCompanion.Core.Providers.Fifa18;
using System.Text.Json;

namespace CareerCompanion.Core.Services;

public sealed record AutomaticWorldResult(int IncomingMessages,int Notifications);

public sealed class AutomaticWorldService(Database db)
{
    public int ApplyPreMatch(long careerId,CareerFixture fixture,Fifa18OpponentScout? scout,bool teammates=true,bool managers=true)
    {
        var career=db.GetCareer(careerId);var timestamp=DateTime.TryParse(fixture.Date,out var date)?date:DateTime.UtcNow;
        var threats=scout?.KeyPlayers.Where(x=>x.Overall>0).Take(3).Select(x=>$"{x.Name} ({x.Position}, OVR {x.Overall})").ToList()??[];
        var details=new List<string>{fixture.IsHome?$"Home fixture against {fixture.Opponent}":$"Away fixture at {fixture.Opponent}"};if(!string.IsNullOrWhiteSpace(scout?.ManagerName))details.Add($"managed by {scout.ManagerName}");if(threats.Count>0)details.Add("key threats: "+string.Join(", ",threats));if(!string.IsNullOrWhiteSpace(scout?.StadiumName)&&!fixture.IsHome)details.Add("venue: "+scout.StadiumName);
        var summary=string.Join(". ",details)+".";var metadata=JsonSerializer.Serialize(new{fixture.EventKey,scout});var evtId=db.SaveEvent(new(0,careerId,null,"PRE_MATCH_BRIEFING",timestamp,scout?.IsRival==true?72:48,"[]",metadata,summary,FactClassification.SaveFact));
        var notices=0;if(db.AddNotification(careerId,"Scouting",$"Briefing: {fixture.Opponent}",summary,"PreMatch",scout?.IsRival==true?80:60,$"prematch:{fixture.EventKey}",timestamp))notices++;
        var active=db.GetCharacters(careerId).Where(IsActive).ToList();var manager=active.FirstOrDefault(x=>x.Type==CharacterType.Manager);var teammate=active.Where(x=>x.Type==CharacterType.Teammate).OrderByDescending(Overall).ThenBy(x=>x.Id).FirstOrDefault();
        if(managers&&manager is not null)
        {
            var text=OfflineDialogueLibrary.PreMatch(manager,fixture,scout?.IsRival==true,threats.FirstOrDefault(),evtId.GetHashCode());
            if(db.AddAutomaticReaction(careerId,manager.Id,evtId,SceneType.PreMatch,JsonSerializer.Serialize(new{automatic=true,eventId=evtId,fixture.EventKey}),text,58,20,"pre-match briefing","Manager",manager.Name,$"Messages:{manager.Id}",65,$"prematch-manager:{fixture.EventKey}:{manager.Id}",timestamp))notices++;
        }
        if(teammates&&teammate is not null)
        {
            var text=OfflineDialogueLibrary.PreMatch(teammate,fixture,scout?.IsRival==true,threats.FirstOrDefault(),evtId.GetHashCode());
            if(db.AddAutomaticReaction(careerId,teammate.Id,evtId,SceneType.PreMatch,JsonSerializer.Serialize(new{automatic=true,eventId=evtId,fixture.EventKey}),text,48,30,"pre-match teammate message","Message",teammate.Name,$"Messages:{teammate.Id}",55,$"prematch-teammate:{fixture.EventKey}:{teammate.Id}",timestamp))notices++;
        }
        return notices;
    }

    public int ApplySquadChanges(long careerId,string sourceFingerprint,IReadOnlyList<Character> arrivals,IReadOnlyList<Character> departures,string careerDate,bool teammates=true)
    {
        if(arrivals.Count==0&&departures.Count==0)return 0;var timestamp=DateTime.TryParse(careerDate,out var date)?date:DateTime.UtcNow;var notices=0;
        if(arrivals.Count>0)
        {
            var names=string.Join(", ",arrivals.Take(4).Select(x=>x.Name))+(arrivals.Count>4?$" and {arrivals.Count-4} more":"");var summary=arrivals.Count==1?$"{names} joined the first-team squad at {arrivals[0].Club}.":$"First-team arrivals: {names}.";var evtId=db.SaveEvent(new(0,careerId,null,"SQUAD_ARRIVAL",timestamp,arrivals.Count>=3?70:55,JsonSerializer.Serialize(arrivals.Select(x=>x.Id)),JsonSerializer.Serialize(new{sourceFingerprint}),summary,FactClassification.SaveFact));
            if(db.AddNotification(careerId,"Squad",arrivals.Count==1?"New teammate":"Squad arrivals",summary,"Squad",arrivals.Count>=3?72:60,$"squad-arrival:{sourceFingerprint}",timestamp))notices++;
            var player=arrivals.OrderByDescending(Overall).First();if(teammates&&db.AddAutomaticReaction(careerId,player.Id,evtId,SceneType.TrainingGround,JsonSerializer.Serialize(new{automatic=true,eventId=evtId}),OfflineDialogueLibrary.SquadArrival(player,evtId.GetHashCode()),48,35,"new teammate introduction","Message",player.Name,$"Messages:{player.Id}",58,$"arrival-message:{sourceFingerprint}:{player.Id}",timestamp))notices++;
        }
        if(departures.Count>0){var names=string.Join(", ",departures.Take(4).Select(x=>x.Name))+(departures.Count>4?$" and {departures.Count-4} more":"");var summary=departures.Count==1?$"{names} left the active first-team squad.":$"First-team departures: {names}.";db.SaveEvent(new(0,careerId,null,"SQUAD_DEPARTURE",timestamp,departures.Count>=3?68:52,JsonSerializer.Serialize(departures.Select(x=>x.Id)),JsonSerializer.Serialize(new{sourceFingerprint}),summary,FactClassification.SaveFact));if(db.AddNotification(careerId,"Squad",departures.Count==1?"Squad departure":"Squad departures",summary,"Squad",departures.Count>=3?70:58,$"squad-departure:{sourceFingerprint}",timestamp))notices++;}
        return notices;
    }

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
            var text=OfflineDialogueLibrary.MatchReaction(character,career,result.Match,evt);var scene=character.Type==CharacterType.Manager?SceneType.ManagerOffice:SceneType.PostMatch;
            var context=System.Text.Json.JsonSerializer.Serialize(new{automatic=true,eventId=evt.Id,classification=FactClassification.SimulatedInterpretation.ToString()});
            if(db.AddAutomaticReaction(career.Id,character.Id,evt.Id,scene,context,text,Math.Clamp(evt.Importance,30,75),Tone(evt),"automatic post-match reaction",character.Type==CharacterType.Manager?"Manager":"Message",character.Name,$"Messages:{character.Id}",target.Priority,dedupe,evt.Timestamp)){messages++;notices++;}
        }
        if(result.Match.Input.TeamContext=="International")
        {
            var agent=characters.Values.FirstOrDefault(x=>x.Type==CharacterType.Agent&&IsActive(x));var international=result.Events.FirstOrDefault(x=>x.Type.StartsWith("INTERNATIONAL_",StringComparison.Ordinal));
            if(agent is not null&&international is not null)
            {
                var text=OfflineDialogueLibrary.International(agent,result.Match.Input.RepresentingTeam,international.Type=="INTERNATIONAL_DEBUT",international.Id.GetHashCode());
                if(db.AddAutomaticReaction(career.Id,agent.Id,international.Id,SceneType.PrivateMessage,JsonSerializer.Serialize(new{automatic=true,eventId=international.Id}),text,70,35,"international career guidance","Message",agent.Name,$"Messages:{agent.Id}",82,$"international-agent:{result.Match.Id}:{agent.Id}",international.Timestamp)){messages++;notices++;}
            }
        }
        var top=result.Events.FirstOrDefault();if(newsCreated&&top is not null&&db.AddNotification(career.Id,"News","New coverage after "+result.Match.Input.Opponent,top.Summary,"News",top.Importance,$"news:{result.Match.Id}",top.Timestamp))notices++;
        foreach(var record in result.Events.Where(x=>x.Type=="FOOTBALL_RECORD_BROKEN"))
        {
            if(db.AddNotification(career.Id,"Record","Football record broken",record.Summary,"Timeline",98,$"record:{record.Id}",record.Timestamp))notices++;
            var agent=characters.Values.FirstOrDefault(x=>x.Type==CharacterType.Agent&&IsActive(x));
            if(agent is not null&&db.AddAutomaticReaction(career.Id,agent.Id,record.Id,SceneType.PrivateMessage,JsonSerializer.Serialize(new{automatic=true,eventId=record.Id,record=true}),OfflineDialogueLibrary.Record(agent,record.Summary,record.Id.GetHashCode()),92,70,"football record reaction","Message",agent.Name,$"Messages:{agent.Id}",96,$"record-agent:{record.Id}:{agent.Id}",record.Timestamp)){messages++;notices++;}
        }
        if(socialCreated&&top is not null&&db.AddNotification(career.Id,"Social","The football world is reacting",top.Summary,"Social",Math.Max(30,top.Importance-5),$"social:{result.Match.Id}",top.Timestamp))notices++;
        if(interviewCreated&&db.AddNotification(career.Id,"Interview","Post-match interview requested","The media wants to hear from you after "+result.Match.Input.Opponent+".","Press",80,$"interview:{result.Match.Id}",top?.Timestamp))notices++;
        if(top is not null){var engine=new CharacterStateEngine();var states=characters.Values.Where(x=>x.Type is CharacterType.Manager or CharacterType.Teammate).Where(IsActive).Select(character=>engine.AfterMatch(character,db.GetCharacterState(character.Id),result.Match,top)).ToList();db.SaveCharacterStatesOnce(career.Id,$"match-state:{career.Id}:{result.Match.Id}",states);}
        var psychology=new PlayerPsychologyService(db).ApplyMatch(result,teammates,managers);messages+=psychology.SupportMessages;notices+=psychology.Notifications;
        return new(messages,notices);
    }

    public void RebuildMatchCharacterStates(long careerId)
    {
        var matches=db.GetMatches(careerId,500);var events=db.GetEvents(careerId,2000).Where(x=>x.MatchId is not null).GroupBy(x=>x.MatchId!.Value).ToDictionary(x=>x.Key,x=>x.OrderByDescending(e=>e.Importance).First());var engine=new CharacterStateEngine();
        foreach(var character in db.GetCharacters(careerId).Where(x=>x.Type is CharacterType.Manager or CharacterType.Teammate).Where(IsActive))
        {
            var state=new CharacterState(character.Id);foreach(var match in matches)if(events.TryGetValue(match.Id,out var top))state=engine.AfterMatch(character,state,match,top);db.SaveCharacterState(state);
        }
        new PlayerPsychologyService(db).Rebuild(careerId);
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
        foreach(var change in changes){var type=change.Contains("moved from",StringComparison.OrdinalIgnoreCase)?"PLAYER_TRANSFERRED":change.Contains("injured",StringComparison.OrdinalIgnoreCase)?"PLAYER_INJURY_STATUS":"PLAYER_DEVELOPMENT";var timestamp=DateTime.TryParse(current.CareerDate,out var date)?date:current.CapturedAt;var evtId=db.SaveEvent(new(0,careerId,null,type,timestamp,type=="PLAYER_TRANSFERRED"?95:70,"[]",System.Text.Json.JsonSerializer.Serialize(new{sourceFingerprint=current.SourceFingerprint}),change,FactClassification.SaveFact));if(type=="PLAYER_TRANSFERRED"){db.AddNews(careerId,evtId,"Touchline Football",$"{db.GetCareer(careerId).PlayerName} completes move to {current.Club}",change+" Attention now turns to earning a place in the new squad.","positive",90,timestamp);var agent=db.GetCharacters(careerId).FirstOrDefault(x=>x.Type==CharacterType.Agent&&IsActive(x));if(agent is not null)db.AddAutomaticReaction(careerId,agent.Id,evtId,SceneType.TransferDiscussion,System.Text.Json.JsonSerializer.Serialize(new{automatic=true,eventId=evtId}),OfflineDialogueLibrary.Transfer(agent,previous?.Club??"your previous club",current.Club,evtId.GetHashCode()),70,35,"transfer guidance","Message",agent.Name,$"Messages:{agent.Id}",88,$"transfer-agent:{current.SourceFingerprint}:{agent.Id}",timestamp);}}db.AddCareerProgressSnapshot(current);
        return changes;
    }

    public void ApplyPublicStatement(long careerId,long interviewId,int questionIndex,string answer,int importance)
    {
        var career=db.GetCareer(careerId);var timestamp=DateTime.TryParse(career.CurrentDate,out var date)?date:DateTime.UtcNow;var key=$"statement:{interviewId}:{questionIndex}";db.AddNotification(careerId,"Interview","Your statement entered the career world",answer,"Press",Math.Clamp(importance,40,90),key,timestamp);var metadata=System.Text.Json.JsonSerializer.Serialize(new{interviewId,questionIndex});var evtId=db.SaveEvent(new(0,careerId,null,"PUBLIC_STATEMENT",timestamp,Math.Clamp(importance,35,85),"[]",metadata,"Public statement: "+answer,FactClassification.HistoricalFact));if(importance>=65)db.AddSocial(careerId,evtId,"@TouchlineLive","media transcript",$"{career.PlayerName}: \"{answer}\"",timestamp);
        var teamFirst=ContainsAny(answer,"team","we ","together","teammates");var accountable=ContainsAny(answer,"my fault","my responsibility","I need to","I must");var blame=ContainsAny(answer,"their fault","blame","manager got","manager was","teammates were");var referee=ContainsAny(answer,"referee","official","decision was unfair");var boastful=ContainsAny(answer,"carried","best player","too easy","unstoppable");var delta=teamFirst?2:accountable?1:blame?-3:boastful?-1:0;var engine=new RelationshipEngine();var characters=db.GetCharacters(careerId).Where(x=>x.Type is CharacterType.Manager or CharacterType.Teammate).Where(IsActive).ToList();var relationships=characters.Select(character=>{var current=db.GetRelationship(character.Id);var characterDelta=character.Type==CharacterType.Manager&&referee?-1:delta;return characterDelta==0?current:engine.Apply(current,characterDelta,accountable?2:teamFirst?1:-1,accountable?3:teamFirst?1:-1);}).ToList();
        if(!db.ApplyStatementConsequencesOnce(careerId,$"statement-consequences:{interviewId}:{questionIndex}",evtId,relationships,$"Public statement after the match: {answer}",Math.Clamp(importance,40,75),delta*12,timestamp))return;
        if(referee||blame||boastful)db.AddNews(careerId,evtId,"The Football Desk",$"{career.PlayerName} draws attention after post-match comments",$"The player's remarks after the match are likely to remain part of the conversation around {career.Club}.","mixed",Math.Clamp(importance,55,85),timestamp);
        var manager=characters.FirstOrDefault(x=>x.Type==CharacterType.Manager);var teammate=characters.Where(x=>x.Type==CharacterType.Teammate).OrderByDescending(x=>db.GetRelationship(x.Id).Familiarity).FirstOrDefault();
        if(manager is not null&&(blame||referee||accountable)){var text=OfflineDialogueLibrary.Statement(manager,teamFirst,blame,referee,accountable,evtId.GetHashCode());db.AddAutomaticReaction(careerId,manager.Id,evtId,SceneType.ManagerOffice,JsonSerializer.Serialize(new{automatic=true,eventId=evtId}),text,65,blame?-35:15,"press statement reaction","Manager",manager.Name,$"Messages:{manager.Id}",75,$"statement-manager:{interviewId}:{questionIndex}:{manager.Id}",timestamp);}
        if(teammate is not null&&(teamFirst||blame||boastful)){var text=OfflineDialogueLibrary.Statement(teammate,teamFirst,blame,referee,accountable,evtId.GetHashCode());db.AddAutomaticReaction(careerId,teammate.Id,evtId,SceneType.PrivateMessage,JsonSerializer.Serialize(new{automatic=true,eventId=evtId}),text,58,teamFirst?35:-30,"press statement reaction","Message",teammate.Name,$"Messages:{teammate.Id}",68,$"statement-teammate:{interviewId}:{questionIndex}:{teammate.Id}",timestamp);}
    }

    private static string Reaction(Character character,Career career,CareerMatch match,CareerEvent evt)
    {
        var input=match.Input;var profile=character.Profile;var personality=profile.Personality;var communication=profile.Communication;var seed=unchecked(character.Id*397+evt.Id);var playful=personality.Humor>=60||communication.Humor>=60;var direct=personality.Diplomacy<45||communication.Directness>=75;
        if(character.Type==CharacterType.Manager)
        {
            if(input.TeamContext=="International")return input.TeamScore>input.OpponentScore?$"Good work with {input.RepresentingTeam}. Recover properly, then return ready for club duty.":$"International defeats hurt. Reset tonight and bring your focus back to the club.";
            return evt.Type switch
            {
                "PLAYER_RED_CARD"=>direct?"The sending-off put the team in trouble. I need better discipline from you.":"We will review the sending-off together. Your response now matters more than the mistake.",
                "PLAYER_HATTRICK"=>personality.Professionalism>=70?"Excellent finishing. Enjoy the achievement, then set the standard again in training.":"Three goals is a fine way to make your case. Now show me you can repeat it.",
                "LARGE_DEFEAT"=>direct?"That result was below our standard. We work tomorrow and respond next match.":"That result is difficult to accept. We will find the response together.",
                "PLAYER_BENCHED"=>personality.Patience>=60?"Keep working. Your opportunity will come if your training stays at the right level.":"You made an impact from the bench. Make selection impossible to ignore next time.",
                _=>input.TeamScore>input.OpponentScore?(playful?"Good result. Enjoy it briefly, then we move on to the next job.":"Good result. Stay focused because the next match arrives quickly."):direct?"Review that performance honestly. We need a stronger response next time.":"We need to learn from that performance and prepare a stronger response."
            };
        }
        if(input.TeamContext=="International")return input.TeamScore>input.OpponentScore?$"That win with {input.RepresentingTeam} means a lot. Enjoy the night.":$"Tough result with {input.RepresentingTeam}. We will pick you up when you get back.";
        return evt.Type switch
        {
            "PLAYER_HATTRICK"=>playful?(seed%2==0?$"Unreal, {career.PlayerName}. You were unplayable. Save one for me next time.":"Three goals? Leave some records for the rest of us."):"That was a top-class finishing display. You earned every bit of it.",
            "PLAYER_RED_CARD"=>direct?"That red card left us chasing the game. We need you on the pitch.":"Forget the noise around the sending-off. We need you back with us.",
            "LARGE_DEFEAT"=>personality.Loyalty>=65?"Tough one. We stay together and put it right next match.":"That hurt. We cannot hide from it, but we can answer on the pitch.",
            "LATE_WINNER"=>playful?"I still cannot believe that finish. The dressing room nearly lost its roof.":"That finish changed everything. What a moment.",
            _=>input.TeamScore>input.OpponentScore?(playful?"Big result. I am claiming an assist for the celebration.":"Big result today. You were important to it."):input.TeamScore<input.OpponentScore?(personality.Openness>=55?"That one hurts. Talk to me if you need to clear your head.":"We have to respond. No hiding from this one."):"A point. Feels like we left something out there."
        };
    }
    private static int Tone(CareerEvent evt)=>evt.Type.Contains("LOST")||evt.Type.Contains("RED")||evt.Type.Contains("DEFEAT")?-35:35;
    private static int Overall(Character character){try{using var json=JsonDocument.Parse(character.FactsJson);return json.RootElement.TryGetProperty("overall",out var value)?value.GetInt32():0;}catch(JsonException){return 0;}}
    private static bool ContainsAny(string text,params string[] values)=>values.Any(x=>text.Contains(x,StringComparison.OrdinalIgnoreCase));
    private static bool IsActive(Character character){try{using var json=System.Text.Json.JsonDocument.Parse(character.FactsJson);return !json.RootElement.TryGetProperty("providerActive",out var active)||active.ValueKind!=System.Text.Json.JsonValueKind.False;}catch(System.Text.Json.JsonException){return true;}}
}
