using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using System.Text.Json;

namespace CareerCompanion.Core.Services;

public sealed record PlayerPsychologyResult(PlayerState State,int Severity,int SupportMessages,int Notifications);
public sealed record PlayerRecoveryResult(PlayerState State,bool Applied,long? SupporterId,string Summary);

public sealed class PlayerPsychologyService(Database db)
{
    public PlayerPsychologyResult ApplyMatch(MatchProcessingResult result,bool teammates,bool managers)
    {
        var match=result.Match;var before=db.GetPlayerState(match.CareerId);var state=CalculateAfterMatch(before,match,result.Events,out var severity);var summary=EmotionalSummary(match,state,severity);
        var evtId=db.SaveEvent(new(0,match.CareerId,match.Id,"PLAYER_EMOTIONAL_STATE",state.UpdatedAt,Math.Clamp(30+severity*2,30,92),"[]",JsonSerializer.Serialize(new{severity,state.Confidence,state.Pressure,state.Fatigue,state.Isolation}),summary,FactClassification.SimulatedInterpretation));
        if(!db.SavePlayerStateOnce(match.CareerId,evtId,$"player-psychology:{match.CareerId}:{match.Id}",state))return new(db.GetPlayerState(match.CareerId),severity,0,0);
        var notices=(state.NeedsSupport||severity>=10)&&db.AddNotification(match.CareerId,"Wellbeing",state.NeedsSupport?"This result is weighing on you":"Player mindset",summary,"Home",Math.Clamp(35+severity*2,35,90),$"player-state:{match.Id}",state.UpdatedAt)?1:0;
        var messages=state.NeedsSupport?CreatePrivateSupport(match,state,severity,evtId,teammates,managers):0;return new(state,severity,messages,notices+messages);
    }

    public PlayerState Rebuild(long careerId,long? excludeMatchId=null)
    {
        var state=new PlayerState(careerId);var choices=db.GetGenerationJobs(careerId,"player_recovery_choice").Where(x=>x.EventId is not null).GroupBy(x=>x.EventId!.Value).ToDictionary(x=>x.Key,x=>Choice(x.Last().PayloadJson));
        foreach(var match in db.GetMatches(careerId,500).Where(x=>x.Id!=excludeMatchId).OrderBy(x=>x.Input.Date).ThenBy(x=>x.Id))
        {
            var events=db.GetEvents(careerId,3000).Where(x=>x.MatchId==match.Id).ToList();state=CalculateAfterMatch(state,match,events,out _);if(choices.TryGetValue(match.Id,out var choice))state=ApplyChoice(state,choice,match.Input.Date);
        }
        db.SavePlayerState(state);return state;
    }

    public PlayerState RebuildBeforeMatch(long careerId,long matchId)
    {
        var target=db.GetMatch(careerId,matchId);var state=new PlayerState(careerId);var choices=db.GetGenerationJobs(careerId,"player_recovery_choice").Where(x=>x.EventId is not null).GroupBy(x=>x.EventId!.Value).ToDictionary(x=>x.Key,x=>Choice(x.Last().PayloadJson));
        foreach(var match in db.GetMatches(careerId,500).Where(x=>string.CompareOrdinal(x.Input.Date,target.Input.Date)<0||x.Input.Date==target.Input.Date&&x.Id<target.Id).OrderBy(x=>x.Input.Date).ThenBy(x=>x.Id)){var events=db.GetEvents(careerId,3000).Where(x=>x.MatchId==match.Id).ToList();state=CalculateAfterMatch(state,match,events,out _);if(choices.TryGetValue(match.Id,out var choice))state=ApplyChoice(state,choice,match.Input.Date);}db.SavePlayerState(state);return state;
    }

    public PlayerRecoveryResult ChooseRecovery(long careerId,string choice)
    {
        var match=db.GetMatches(careerId,1).LastOrDefault()??throw new InvalidOperationException("Log or import a match before choosing a recovery response.");var normalized=choice.ToLowerInvariant();if(normalized is not("open_up" or "recover" or "training"))throw new ArgumentOutOfRangeException(nameof(choice));var current=db.GetPlayerState(careerId);var state=ApplyChoice(current,normalized,match.Input.Date);var label=normalized switch{"open_up"=>"You chose to speak honestly with someone in your inner circle.","recover"=>"You stepped away from football for a recovery day.",_=>"You returned to training with a simple plan and a fresh target."};if(!db.SavePlayerChoiceOnce(careerId,match.Id,normalized,state))return new(db.GetPlayerState(careerId),false,null,"You already chose how to respond after this match.");
        var evtId=db.SaveEvent(new(0,careerId,match.Id,"PLAYER_RECOVERY_CHOICE",state.UpdatedAt,55,"[]",JsonSerializer.Serialize(new{choice=normalized}),label,FactClassification.HistoricalFact));db.AddNotification(careerId,"Wellbeing","Your response",label,"Home",50,$"player-recovery:{match.Id}",state.UpdatedAt);
        long? supporterId=null;if(normalized=="open_up"){var supporter=SelectSupporters(careerId,true,true).FirstOrDefault();if(supporter is not null){supporterId=supporter.Id;db.AddMemory(careerId,supporter.Id,evtId,$"{db.GetCareer(careerId).PlayerName} opened up after a difficult result.",62,35,"private support",false,state.UpdatedAt);}}
        return new(state,true,supporterId,label);
    }

    public static PlayerState CalculateAfterMatch(PlayerState current,CareerMatch match,IReadOnlyList<CareerEvent> events,out int severity)
    {
        var date=DateTime.TryParse(match.Input.Date,out var parsed)?parsed:DateTime.UtcNow;var days=current.UpdatedAt==default?0:Math.Clamp((date.Date-current.UpdatedAt.Date).Days,0,14);var confidence=current.Confidence;var pressure=Math.Max(0,current.Pressure-days);var fatigue=Math.Max(0,current.Fatigue-days*3);var isolation=Math.Max(0,current.Isolation-days/2);var resilience=current.Resilience;severity=0;
        fatigue+=Math.Clamp(match.Input.Minutes/12,1,10);if(match.Result=="W"){confidence+=6;pressure-=5;isolation-=3;resilience+=1;}else if(match.Result=="D"){confidence+=1;pressure+=1;}else{severity+=6;confidence-=6;pressure+=7;isolation+=3;}
        var major=match.Input.IsMajorFixture||match.Input.IsDerby||match.Input.TeamContext=="International"||match.Input.Competition.Contains("Final",StringComparison.OrdinalIgnoreCase);if(match.Result=="L"&&major){severity+=8;confidence-=7;pressure+=9;isolation+=4;}if(events.Any(x=>x.Type=="LARGE_DEFEAT")){severity+=7;confidence-=7;pressure+=7;isolation+=4;}if(events.Any(x=>x.Type=="LOSING_STREAK")){severity+=8;confidence-=8;pressure+=10;isolation+=5;}if(match.Input.PenaltyMissed){severity+=9;confidence-=9;pressure+=9;isolation+=5;}if(match.Input.RedCard){severity+=8;confidence-=8;pressure+=10;isolation+=6;}if(match.Input.Rating>0&&match.Input.Rating<=5.5){severity+=5;confidence-=5;pressure+=5;}if(match.Input.Goals+match.Input.Assists>=2||match.Input.Rating>=8.5){confidence+=5;pressure-=2;}if(!match.Input.Started&&match.Input.StartedKnown){confidence-=2;isolation+=2;}var buffer=Math.Max(0,(resilience-50)/8);confidence+=Math.Min(buffer,severity/4);pressure-=Math.Min(buffer,severity/5);
        confidence=Math.Clamp(confidence,0,100);pressure=Math.Clamp(pressure,0,100);fatigue=Math.Clamp(fatigue,0,100);isolation=Math.Clamp(isolation,0,100);resilience=Math.Clamp(resilience,20,90);var needs=severity>=16||pressure>=68||isolation>=45||confidence<=30;var mood=Mood(match.Result,confidence,pressure,fatigue,isolation,needs);var trigger=Trigger(match,events,severity);return new(match.CareerId,mood,confidence,pressure,fatigue,isolation,resilience,trigger,needs,date);
    }

    private int CreatePrivateSupport(CareerMatch match,PlayerState state,int severity,long eventId,bool teammates,bool managers)
    {
        var supporters=SelectSupporters(match.CareerId,teammates,managers);var count=severity>=22?Math.Min(2,supporters.Count):Math.Min(1,supporters.Count);var created=0;foreach(var person in supporters.Take(count))
        {
            var text=SupportText(person,severity,match.Input.Opponent);var context=JsonSerializer.Serialize(new{automatic=true,eventId,privateSupport=true,playerMood=state.Mood});if(db.AddAutomaticReaction(match.CareerId,person.Id,eventId,SceneType.PrivateMessage,context,text,72,severity>=22?45:30,"private wellbeing check-in",person.Type==CharacterType.Manager?"Manager":"Message",person.Name,$"Messages:{person.Id}",Math.Clamp(65+severity,65,95),$"wellbeing-support:{match.Id}:{person.Id}",state.UpdatedAt))created++;
        }
        return created;
    }

    private List<Character> SelectSupporters(long careerId,bool teammates,bool managers)
    {
        var active=db.GetCharacters(careerId).Where(IsActive).Where(x=>x.Type==CharacterType.Agent||x.Type==CharacterType.Teammate&&teammates||x.Type==CharacterType.Manager&&managers).ToList();return active.OrderByDescending(x=>SupportScore(x,db.GetRelationship(x.Id))).ThenBy(x=>x.Id).ToList();
    }
    private static int SupportScore(Character person,Relationship relationship)=>relationship.Trust*2+relationship.Familiarity+relationship.Friendliness+relationship.Score+(person.Type==CharacterType.Agent?35:person.Type==CharacterType.Teammate?20:5);
    private static string SupportText(Character person,int severity,string opponent)=>person.Type switch
    {
        CharacterType.Agent when severity>=22=>$"I heard how badly the result against {opponent} hit you. Do not sit with this alone tonight. I am calling now, and I can come over if you need me.",
        CharacterType.Agent=>$"Forget the noise around the result for a moment. Call me when you are somewhere quiet. You do not have to carry it by yourself.",
        CharacterType.Manager when severity>=22=>"Football can wait tonight. Speak to someone you trust, get away from the noise, and check in with me tomorrow.",
        CharacterType.Manager=>"I know this result hurts. Take tonight, clear your head, and we will talk properly tomorrow.",
        _ when severity>=22=>"I know this one has hit you hard. You do not need to find the right words. I can come over, or we can just sit for a while.",
        _=>"That result is still hurting, I know. If you want company or just someone to listen, call me."
    };
    private static PlayerState ApplyChoice(PlayerState current,string choice,string dateText)
    {
        var date=DateTime.TryParse(dateText,out var parsed)?parsed:DateTime.UtcNow;var state=choice switch{"open_up"=>current with{Confidence=Math.Clamp(current.Confidence+3,0,100),Pressure=Math.Clamp(current.Pressure-8,0,100),Isolation=Math.Clamp(current.Isolation-20,0,100),Resilience=Math.Clamp(current.Resilience+2,0,100),LastTrigger="Opened up to the inner circle",UpdatedAt=date},"recover"=>current with{Pressure=Math.Clamp(current.Pressure-10,0,100),Fatigue=Math.Clamp(current.Fatigue-25,0,100),Isolation=Math.Clamp(current.Isolation-3,0,100),LastTrigger="Took time away to recover",UpdatedAt=date},_=>current with{Confidence=Math.Clamp(current.Confidence+8,0,100),Pressure=Math.Clamp(current.Pressure-4,0,100),Fatigue=Math.Clamp(current.Fatigue+7,0,100),Resilience=Math.Clamp(current.Resilience+2,0,100),LastTrigger="Reset through focused training",UpdatedAt=date}};return state with{Mood=Mood("",state.Confidence,state.Pressure,state.Fatigue,state.Isolation,false),NeedsSupport=state.Pressure>=68||state.Isolation>=45||state.Confidence<=30};
    }
    private static string Choice(string json){try{using var doc=JsonDocument.Parse(json);return doc.RootElement.GetProperty("choice").GetString()??"recover";}catch{return"recover";}}
    private static string Mood(string result,int confidence,int pressure,int fatigue,int isolation,bool needs)=>isolation>=60?"withdrawn":pressure>=82&&confidence<=25?"overwhelmed":pressure>=70?"under intense pressure":confidence<=30?"low":fatigue>=75?"drained":result=="L"&&needs?"hurting":result=="L"?"disappointed":result=="W"&&confidence>=68?"energized":confidence>=65?"confident":"steady";
    private static string Trigger(CareerMatch match,IReadOnlyList<CareerEvent> events,int severity){if(match.Input.PenaltyMissed)return$"Penalty miss against {match.Input.Opponent}";if(match.Input.RedCard)return$"Sending-off against {match.Input.Opponent}";if(events.Any(x=>x.Type=="LOSING_STREAK"))return"The losing run is becoming difficult to escape";if(events.Any(x=>x.Type=="LARGE_DEFEAT"))return$"Heavy defeat against {match.Input.Opponent}";if(match.Result=="L"&&(match.Input.IsMajorFixture||match.Input.IsDerby||match.Input.Competition.Contains("Final",StringComparison.OrdinalIgnoreCase)))return$"Major defeat against {match.Input.Opponent}";return severity>0?$"Defeat against {match.Input.Opponent}":$"Performance against {match.Input.Opponent}";}
    private static string EmotionalSummary(CareerMatch match,PlayerState state,int severity)=>severity>=22?$"The result against {match.Input.Opponent} has left you {state.Mood}. The pressure feels personal, and withdrawing from people is becoming a risk.":severity>=16?$"The result against {match.Input.Opponent} is staying with you. Confidence has taken a hit and the pressure is building.":match.Result=="L"?$"The defeat against {match.Input.Opponent} hurts, but you are still processing it without losing perspective.":match.Result=="W"?$"The result against {match.Input.Opponent} has strengthened your confidence.":$"You are processing the match against {match.Input.Opponent} and returning to a steady level.";
    private static bool IsActive(Character character){try{using var json=JsonDocument.Parse(character.FactsJson);return !json.RootElement.TryGetProperty("providerActive",out var active)||active.ValueKind!=JsonValueKind.False;}catch(JsonException){return true;}}
}
