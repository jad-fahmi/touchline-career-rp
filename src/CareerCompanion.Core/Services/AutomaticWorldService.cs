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
        var availability=NormalizeAvailability(fixture.Availability);var availabilityText=AvailabilityText(availability);
        var details=new List<string>{fixture.IsHome?$"Home fixture against {fixture.Opponent}":$"Away fixture at {fixture.Opponent}",availabilityText};if(!string.IsNullOrWhiteSpace(scout?.ManagerName))details.Add($"managed by {scout.ManagerName}");if(threats.Count>0)details.Add("key threats: "+string.Join(", ",threats));if(!string.IsNullOrWhiteSpace(scout?.StadiumName)&&!fixture.IsHome)details.Add("venue: "+scout.StadiumName);
        var summary=string.Join(". ",details)+".";var metadata=JsonSerializer.Serialize(new{fixture.EventKey,fixture.Availability,scout});var evtId=db.SaveEvent(new(0,careerId,null,"PRE_MATCH_BRIEFING",timestamp,scout?.IsRival==true?72:48,"[]",metadata,summary,FactClassification.SaveFact));
        var statusKey=availability.ToLowerInvariant();var notices=0;if(db.AddNotification(careerId,"Scouting",$"Briefing: {fixture.Opponent}",summary,"PreMatch",scout?.IsRival==true?80:60,$"prematch:{fixture.EventKey}:{statusKey}",timestamp))notices++;
        var active=db.GetCharacters(careerId).Where(IsActive).ToList();var manager=active.FirstOrDefault(x=>x.Type==CharacterType.Manager);var teammate=active.Where(x=>x.Type==CharacterType.Teammate).OrderByDescending(Overall).ThenBy(x=>x.Id).FirstOrDefault();
        // Everyone reacting to this fixture draws from the same set, so nobody repeats a line already sent.
        var spoken=new HashSet<string>(StringComparer.Ordinal);
        if(managers&&manager is not null)
        {
            var text=OfflineDialogueLibrary.PreMatch(manager,fixture,scout?.IsRival==true,threats.FirstOrDefault(),Seed(evtId,manager.Id),availability,spoken);
            if(db.AddAutomaticReaction(careerId,manager.Id,evtId,SceneType.PreMatch,JsonSerializer.Serialize(new{automatic=true,eventId=evtId,fixture.EventKey,availability}),text,58,20,"pre-match briefing","Manager",manager.Name,$"Messages:{manager.Id}",65,$"prematch-manager:{fixture.EventKey}:{manager.Id}:{statusKey}",timestamp))notices++;
        }
        if(teammates&&teammate is not null)
        {
            var text=OfflineDialogueLibrary.PreMatch(teammate,fixture,scout?.IsRival==true,threats.FirstOrDefault(),Seed(evtId,teammate.Id),availability,spoken);
            if(db.AddAutomaticReaction(careerId,teammate.Id,evtId,SceneType.PreMatch,JsonSerializer.Serialize(new{automatic=true,eventId=evtId,fixture.EventKey,availability}),text,48,30,"pre-match teammate message","Message",teammate.Name,$"Messages:{teammate.Id}",55,$"prematch-teammate:{fixture.EventKey}:{teammate.Id}:{statusKey}",timestamp))notices++;
        }
        return notices;
    }

    private static string NormalizeAvailability(string value)=>value switch
    {
        "Selected" or "Benched" or "NotSelected" or "Injured" or "Suspended" or "Unavailable"=>value,
        _=>"Unknown"
    };
    /// <summary>
    /// Characters read this back, so it stays inside the football world. Selection is the manager's call and
    /// an unnamed team is simply a team that has not been named yet, never a fact a piece of software withheld.
    /// </summary>
    private static string AvailabilityText(string value)=>value switch
    {
        "Selected"=>"the player is in the squad for this fixture",
        "Benched"=>"the player is among the substitutes and is not confirmed to start",
        "NotSelected"=>"the player is not in the squad for this fixture",
        "Injured"=>"the player is unavailable through injury",
        "Suspended"=>"the player is unavailable through suspension",
        "Unavailable"=>"the player is unavailable for this fixture",
        _=>"the manager has not named the team yet, so playing time must not be assumed"
    };

    public int ApplyTransferRequest(long careerId,Fifa18TransferRequestSignal signal,bool teammates=true,bool managers=true)
    {
        var career=db.GetCareer(careerId);var timestamp=DateTime.TryParse(signal.Date,out var date)?date:DateTime.UtcNow;
        var status=signal.Status is "Accepted" or "Rejected"?signal.Status:"Requested";
        var type=status switch{"Accepted"=>"PLAYER_TRANSFER_REQUEST_ACCEPTED","Rejected"=>"PLAYER_TRANSFER_REQUEST_REJECTED",_=>"PLAYER_TRANSFER_REQUESTED"};
        var summary=status switch
        {
            "Accepted"=>$"The club accepted {career.PlayerName}'s transfer request.",
            "Rejected"=>$"The club rejected {career.PlayerName}'s transfer request.",
            _=>$"{career.PlayerName} handed in a transfer request at {career.Club}."
        };
        var importance=status=="Requested"?86:82;var metadata=JsonSerializer.Serialize(new{signal.EventKey,signal.Status,signal.Evidence});var evtId=db.SaveEvent(new(0,careerId,null,type,timestamp,importance,"[]",metadata,summary,FactClassification.SaveFact));var notices=0;
        if(db.AddNotification(careerId,"Transfer",status=="Requested"?"Transfer request submitted":$"Transfer request {status.ToLowerInvariant()}",summary,"Timeline",importance,$"transfer-request:{signal.EventKey}:{status}",timestamp))notices++;
        var active=db.GetCharacters(careerId).Where(IsActive).ToList();var manager=active.FirstOrDefault(x=>x.Type==CharacterType.Manager);var mates=active.Where(x=>x.Type==CharacterType.Teammate).OrderByDescending(Overall).ThenBy(x=>x.Id).Take(3).ToList();var agent=active.FirstOrDefault(x=>x.Type==CharacterType.Agent);var spoken=new HashSet<string>(StringComparer.Ordinal);
        if(managers&&manager is not null)
        {
            var text=OfflineDialogueLibrary.TransferRequest(manager,status,Seed(evtId,manager.Id),spoken);
            if(db.AddAutomaticReaction(careerId,manager.Id,evtId,SceneType.TransferDiscussion,JsonSerializer.Serialize(new{automatic=true,eventId=evtId,transferRequest=status}),text,importance, status=="Rejected"?-10:0,"manager transfer-request conversation","Manager",manager.Name,$"Messages:{manager.Id}",importance,$"transfer-request-manager:{signal.EventKey}:{status}",timestamp))notices++;
        }
        if(teammates)
            foreach(var teammate in mates)
            {
                var text=OfflineDialogueLibrary.TransferRequest(teammate,status,Seed(evtId,teammate.Id),spoken);
                if(db.AddAutomaticReaction(careerId,teammate.Id,evtId,SceneType.PrivateMessage,JsonSerializer.Serialize(new{automatic=true,eventId=evtId,transferRequest=status}),text,Math.Max(55,importance-12),status=="Rejected"?-5:5,"teammate transfer-request conversation","Message",teammate.Name,$"Messages:{teammate.Id}",Math.Max(55,importance-10),$"transfer-request-teammate:{signal.EventKey}:{status}:{teammate.Id}",timestamp))notices++;
            }
        if(agent is not null)
        {
            var text=OfflineDialogueLibrary.TransferRequest(agent,status,Seed(evtId,agent.Id),spoken);
            if(db.AddAutomaticReaction(careerId,agent.Id,evtId,SceneType.TransferDiscussion,JsonSerializer.Serialize(new{automatic=true,eventId=evtId,transferRequest=status}),text,Math.Max(65,importance-5),10,"agent transfer-request guidance","Message",agent.Name,$"Messages:{agent.Id}",Math.Max(70,importance-3),$"transfer-request-agent:{signal.EventKey}:{status}",timestamp))notices++;
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
            var player=arrivals.OrderByDescending(Overall).First();if(teammates&&db.AddAutomaticReaction(careerId,player.Id,evtId,SceneType.TrainingGround,JsonSerializer.Serialize(new{automatic=true,eventId=evtId}),OfflineDialogueLibrary.SquadArrival(player,Seed(evtId,player.Id)),48,35,"new teammate introduction","Message",player.Name,$"Messages:{player.Id}",58,$"arrival-message:{sourceFingerprint}:{player.Id}",timestamp))notices++;
        }
        if(departures.Count>0){var names=string.Join(", ",departures.Take(4).Select(x=>x.Name))+(departures.Count>4?$" and {departures.Count-4} more":"");var summary=departures.Count==1?$"{names} left the active first-team squad.":$"First-team departures: {names}.";db.SaveEvent(new(0,careerId,null,"SQUAD_DEPARTURE",timestamp,departures.Count>=3?68:52,JsonSerializer.Serialize(departures.Select(x=>x.Id)),JsonSerializer.Serialize(new{sourceFingerprint}),summary,FactClassification.SaveFact));if(db.AddNotification(careerId,"Squad",departures.Count==1?"Squad departure":"Squad departures",summary,"Squad",departures.Count>=3?70:58,$"squad-departure:{sourceFingerprint}",timestamp))notices++;}
        return notices;
    }

    public AutomaticWorldResult ApplyMatch(MatchProcessingResult result,bool teammates,bool managers,bool interviewCreated,bool newsCreated,bool socialCreated)
    {
        var career=db.GetCareer(result.Match.CareerId);var characters=db.GetCharacters(career.Id).ToDictionary(x=>x.Id);var messages=0;var notices=0;
        // Keep a match readable. A manager and one relevant teammate are
        // enough for a normal result. Defeats are handled by the wellbeing
        // service below, so do not stack a generic post-match chorus on top.
        var eligible=result.Reactions.Where(x=>x.CharacterId is not null).DistinctBy(x=>x.CharacterId).OrderByDescending(x=>x.Priority).Where(target=>
        {
            var character=characters.GetValueOrDefault(target.CharacterId!.Value);return character is not null&&IsActive(character)&&
                (character.Type==CharacterType.Manager&&managers||character.Type==CharacterType.Teammate&&teammates);
        }).ToList();
        var narrative=new MatchNarrativeBuilder(db).Build(career,result.Match);
        var composer=new ReactionComposer(db);var saidThisMatch=new HashSet<string>(StringComparer.Ordinal);var textsThisMatch=new HashSet<string>(StringComparer.Ordinal);
        foreach(var speaker in ChooseSpeakers(narrative,characters,eligible,teammates,managers))
        {
            var character=speaker.Character;
            var evt=result.Events.FirstOrDefault(x=>x.Importance+(character.Type==CharacterType.Manager?18:7)>=speaker.Priority)??result.Events.First();
            var dedupe=$"reaction:{evt.Id}:{character.Id}";
            var composed=composer.Compose(character,narrative,evt.Id,saidThisMatch);
            // Last line of defence. The composer avoids repeated phrases by key; if two people still arrive
            // at the same sentence, the second one says nothing rather than echoing the first.
            if(!textsThisMatch.Add(OfflineDialogueLibrary.Normalize(composed.Text)))continue;
            var scene=character.Type==CharacterType.Manager?SceneType.ManagerOffice:SceneType.PostMatch;
            var context=System.Text.Json.JsonSerializer.Serialize(new{automatic=true,eventId=evt.Id,stance=composed.Stance.ToString(),classification=FactClassification.SimulatedInterpretation.ToString()});
            if(db.AddAutomaticReaction(career.Id,character.Id,evt.Id,scene,context,composed.Text,composed.Importance,composed.Valence,
                "automatic post-match reaction",character.Type==CharacterType.Manager?"Manager":"Message",character.Name,
                $"Messages:{character.Id}",speaker.Priority,dedupe,evt.Timestamp))
            {
                composer.Remember(career.Id,character.Id,composed.PhraseKeys);saidThisMatch.UnionWith(composed.PhraseKeys);messages++;notices++;
            }
        }
        if(result.Match.Input.TeamContext=="International")
        {
            var agent=characters.Values.FirstOrDefault(x=>x.Type==CharacterType.Agent&&IsActive(x));var international=result.Events.FirstOrDefault(x=>x.Type.StartsWith("INTERNATIONAL_",StringComparison.Ordinal));
            if(agent is not null&&international is not null)
            {
                var text=OfflineDialogueLibrary.International(agent,result.Match.Input.RepresentingTeam,international.Type=="INTERNATIONAL_DEBUT",Seed(international.Id,agent.Id));
                if(db.AddAutomaticReaction(career.Id,agent.Id,international.Id,SceneType.PrivateMessage,JsonSerializer.Serialize(new{automatic=true,eventId=international.Id}),text,70,35,"international career guidance","Message",agent.Name,$"Messages:{agent.Id}",82,$"international-agent:{result.Match.Id}:{agent.Id}",international.Timestamp)){messages++;notices++;}
            }
        }
        var top=result.Events.FirstOrDefault();if(newsCreated&&top is not null&&db.AddNotification(career.Id,"News","New coverage after "+result.Match.Input.Opponent,top.Summary,"News",top.Importance,$"news:{result.Match.Id}",top.Timestamp))notices++;
        foreach(var record in result.Events.Where(x=>x.Type=="FOOTBALL_RECORD_BROKEN"))
        {
            if(db.AddNotification(career.Id,"Record","Football record broken",record.Summary,"Timeline",98,$"record:{record.Id}",record.Timestamp))notices++;
            var agent=characters.Values.FirstOrDefault(x=>x.Type==CharacterType.Agent&&IsActive(x));
            if(agent is not null&&db.AddAutomaticReaction(career.Id,agent.Id,record.Id,SceneType.PrivateMessage,JsonSerializer.Serialize(new{automatic=true,eventId=record.Id,record=true}),OfflineDialogueLibrary.Record(agent,record.Summary,Seed(record.Id,agent.Id)),92,70,"football record reaction","Message",agent.Name,$"Messages:{agent.Id}",96,$"record-agent:{record.Id}:{agent.Id}",record.Timestamp)){messages++;notices++;}
        }
        if(socialCreated&&top is not null&&db.AddNotification(career.Id,"Social","The football world is reacting",top.Summary,"Social",Math.Max(30,top.Importance-5),$"social:{result.Match.Id}",top.Timestamp))notices++;
        if(interviewCreated&&db.AddNotification(career.Id,"Interview","Post-match interview requested","The media wants to hear from you after "+result.Match.Input.Opponent+".","Press",80,$"interview:{result.Match.Id}",top?.Timestamp))notices++;
        ApplyMatchRelationships(career.Id,narrative,characters);
        if(top is not null){var engine=new CharacterStateEngine();var states=characters.Values.Where(x=>x.Type is CharacterType.Manager or CharacterType.Teammate).Where(IsActive).Select(character=>engine.AfterMatch(character,db.GetCharacterState(character.Id),result.Match,top)).ToList();db.SaveCharacterStatesOnce(career.Id,$"match-state:{career.Id}:{result.Match.Id}",states);}
        var psychology=new PlayerPsychologyService(db).ApplyMatch(result,teammates,managers);messages+=psychology.SupportMessages;notices+=psychology.Notifications;
        return new(messages,notices);
    }

    /// <summary>
    /// Teammates who shared the pitch form an opinion of how the player performed. The movement is small and
    /// applied once per match, so it accumulates into a real standing over a season without ever spiking.
    /// </summary>
    private void ApplyMatchRelationships(long careerId,MatchNarrative narrative,IReadOnlyDictionary<long,Character> characters)
    {
        var input=narrative.Input;
        var played=narrative.Squad.Select(x=>x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var engine=new RelationshipEngine();
        var updates=new List<Relationship>();
        foreach(var character in characters.Values.Where(x=>x.Type is CharacterType.Manager or CharacterType.Teammate).Where(IsActive))
        {
            var sharedPitch=character.Type==CharacterType.Manager||played.Contains(character.Name);
            if(!sharedPitch&&narrative.Intensity<70)continue;
            var respect=0;var score=0;var tension=0;
            if(input.Minutes>=25&&input.Rating>0)
            {
                if(input.Rating>=8.5)respect+=2;else if(input.Rating>=7.5)respect+=1;
                else if(input.Rating<=5.5)respect-=2;else if(input.Rating<=6.2)respect-=1;
            }
            if(input.Goals>0)respect+=1;
            if(input.RedCard){score-=1;tension+=1;}
            if(input.ScoreKnown&&narrative.Match.Result=="W"&&input.Minutes>0)score+=1;
            if(respect==0&&score==0&&tension==0&&!sharedPitch)continue;
            updates.Add(engine.Apply(db.GetRelationship(character.Id),score,0,respect,0,0,tension,sharedPitch?1:0));
        }
        if(updates.Count>0)db.SaveRelationshipsOnce(careerId,narrative.Match.Id,updates);
    }

    private sealed record Speaker(Character Character,int Priority);

    /// <summary>
    /// Decides who actually says something. A routine afternoon gets one voice, a sending-off or a derby gets
    /// several, and the people who speak are the ones with a reason to: the manager, someone who played in the
    /// match, a close friend, or a team-mate who is not shy about a bad performance.
    /// </summary>
    private IReadOnlyList<Speaker> ChooseSpeakers(MatchNarrative narrative,IReadOnlyDictionary<long,Character> characters,
        IReadOnlyList<ReactionTarget> eligible,bool teammates,bool managers)
    {
        var intensity=narrative.Intensity;
        var capacity=intensity>=80?3:intensity>=55?2:1;
        var priorities=eligible.Where(x=>x.CharacterId is not null).GroupBy(x=>x.CharacterId!.Value)
            .ToDictionary(x=>x.Key,x=>x.Max(t=>t.Priority));
        int PriorityOf(Character c)=>priorities.TryGetValue(c.Id,out var value)?value:Math.Clamp(intensity,30,90);
        var active=characters.Values.Where(IsActive).ToList();
        var speakers=new List<Speaker>();
        var rotation=(int)(narrative.Match.Id%7);

        var manager=active.FirstOrDefault(x=>x.Type==CharacterType.Manager);
        var managerHasReason=intensity>=55||narrative.Has("bench_streak")||narrative.Has("red_card")||narrative.Has("poor_display");
        if(managers&&manager is not null&&(managerHasReason||rotation%3==0))speakers.Add(new(manager,PriorityOf(manager)));

        if(teammates&&speakers.Count<capacity)
        {
            var played=narrative.Squad.ToDictionary(x=>x.Name,StringComparer.OrdinalIgnoreCase);
            var negative=narrative.Headline.Tone==FactTone.Negative;
            var ranked=active.Where(x=>x.Type==CharacterType.Teammate).Select(character=>
            {
                var relationship=db.GetRelationship(character.Id);
                var score=0;
                if(played.TryGetValue(character.Name,out var performance))score+=30+Math.Min(15,performance.Minutes/8);
                score+=Math.Clamp((relationship.Friendliness+relationship.Familiarity)/4,0,30);
                score+=Math.Clamp(relationship.Trust/6,-10,15);
                if(negative)score+=Math.Clamp((relationship.Tension+relationship.Rivalry)/4,0,25);
                else score-=Math.Clamp((relationship.Tension+relationship.Rivalry)/6,0,15);
                if(intensity>=80)score+=Math.Clamp(Overall(character)-78,0,12);
                score+=(int)((character.Id+rotation)%5)*4; // keeps the same voice from speaking every week
                return (Character:character,Score:score);
            }).OrderByDescending(x=>x.Score).ThenBy(x=>x.Character.Id).ToList();
            foreach(var candidate in ranked.Take(capacity-speakers.Count))
                speakers.Add(new(candidate.Character,PriorityOf(candidate.Character)));
        }
        return speakers;
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
        if(!string.Equals(previous.Club,current.Club,StringComparison.OrdinalIgnoreCase))Notice("Career","Transfer confirmed",$"You completed a move from {previous.Club} to {current.Club}.",95,"club");
        if(previous.Overall>0&&current.Overall>previous.Overall)Notice("Development","Overall increased",$"Your overall rating rose from {previous.Overall} to {current.Overall}.",70,"overall");
        if(current.Position!=previous.Position)Notice("Development","Position changed",$"Your listed position changed from {previous.Position} to {current.Position}.",65,"position");
        if(current.ShirtNumber!=previous.ShirtNumber)Notice("Career","New shirt number",$"Your shirt number changed from {previous.ShirtNumber} to {current.ShirtNumber}.",45,"number");
        if(current.Injured&&!previous.Injured)Notice("Fitness","Injury detected","You picked up an injury and are unavailable until the medical staff clear you.",85,"injury");
        if(!current.Injured&&previous.Injured)Notice("Fitness","Return from injury","You have recovered from the injury and are available for selection again.",75,"return");
        foreach(var change in changes){var type=change.Contains("completed a move",StringComparison.OrdinalIgnoreCase)?"PLAYER_TRANSFERRED":change.Contains("injury",StringComparison.OrdinalIgnoreCase)?"PLAYER_INJURY_STATUS":"PLAYER_DEVELOPMENT";var timestamp=DateTime.TryParse(current.CareerDate,out var date)?date:current.CapturedAt;var evtId=db.SaveEvent(new(0,careerId,null,type,timestamp,type=="PLAYER_TRANSFERRED"?95:70,"[]",System.Text.Json.JsonSerializer.Serialize(new{sourceFingerprint=current.SourceFingerprint}),change,FactClassification.SaveFact));if(type=="PLAYER_TRANSFERRED"){db.AddNews(careerId,evtId,"Touchline Football",$"{db.GetCareer(careerId).PlayerName} completes move to {current.Club}",change+" Attention now turns to earning a place in the new squad.","positive",90,timestamp);var agent=db.GetCharacters(careerId).FirstOrDefault(x=>x.Type==CharacterType.Agent&&IsActive(x));if(agent is not null)db.AddAutomaticReaction(careerId,agent.Id,evtId,SceneType.TransferDiscussion,System.Text.Json.JsonSerializer.Serialize(new{automatic=true,eventId=evtId}),OfflineDialogueLibrary.Transfer(agent,previous?.Club??"your previous club",current.Club,Seed(evtId,agent.Id)),70,35,"transfer guidance","Message",agent.Name,$"Messages:{agent.Id}",88,$"transfer-agent:{current.SourceFingerprint}:{agent.Id}",timestamp);}}db.AddCareerProgressSnapshot(current);
        return changes;
    }

    public void ApplyPublicStatement(long careerId,long interviewId,int questionIndex,string answer,int importance)
    {
        var career=db.GetCareer(careerId);var timestamp=DateTime.TryParse(career.CurrentDate,out var date)?date:DateTime.UtcNow;var key=$"statement:{interviewId}:{questionIndex}";db.AddNotification(careerId,"Interview","Your statement entered the career world",answer,"Press",Math.Clamp(importance,40,90),key,timestamp);var metadata=System.Text.Json.JsonSerializer.Serialize(new{interviewId,questionIndex});var evtId=db.SaveEvent(new(0,careerId,null,"PUBLIC_STATEMENT",timestamp,Math.Clamp(importance,35,85),"[]",metadata,"Public statement: "+answer,FactClassification.HistoricalFact));if(importance>=65)db.AddSocial(careerId,evtId,"@TouchlineLive","media transcript",$"{career.PlayerName}: \"{answer}\"",timestamp);
        var teamFirst=ContainsAny(answer,"team","we ","together","teammates");var accountable=ContainsAny(answer,"my fault","my responsibility","I need to","I must");var blame=ContainsAny(answer,"their fault","blame","manager got","manager was","teammates were");var referee=ContainsAny(answer,"referee","official","decision was unfair");var boastful=ContainsAny(answer,"carried","best player","too easy","unstoppable");var delta=teamFirst?2:accountable?1:blame?-3:boastful?-1:0;var engine=new RelationshipEngine();var characters=db.GetCharacters(careerId).Where(x=>x.Type is CharacterType.Manager or CharacterType.Teammate).Where(IsActive).ToList();var relationships=characters.Select(character=>{var current=db.GetRelationship(character.Id);var characterDelta=character.Type==CharacterType.Manager&&referee?-1:delta;return characterDelta==0?current:engine.Apply(current,characterDelta,accountable?2:teamFirst?1:-1,accountable?3:teamFirst?1:-1);}).ToList();
        if(!db.ApplyStatementConsequencesOnce(careerId,$"statement-consequences:{interviewId}:{questionIndex}",evtId,relationships,$"Public statement after the match: {answer}",Math.Clamp(importance,40,75),delta*12,timestamp))return;
        if(referee||blame||boastful)db.AddNews(careerId,evtId,"The Football Desk",$"{career.PlayerName} draws attention after post-match comments",$"The player's remarks after the match are likely to remain part of the conversation around {career.Club}.","mixed",Math.Clamp(importance,55,85),timestamp);
        var manager=characters.FirstOrDefault(x=>x.Type==CharacterType.Manager);var teammate=characters.Where(x=>x.Type==CharacterType.Teammate).OrderByDescending(x=>db.GetRelationship(x.Id).Familiarity).FirstOrDefault();
        var spoken=new HashSet<string>(StringComparer.Ordinal);
        if(manager is not null&&(blame||referee||accountable)){var text=OfflineDialogueLibrary.Statement(manager,teamFirst,blame,referee,accountable,Seed(evtId,manager.Id),spoken);db.AddAutomaticReaction(careerId,manager.Id,evtId,SceneType.ManagerOffice,JsonSerializer.Serialize(new{automatic=true,eventId=evtId}),text,65,blame?-35:15,"press statement reaction","Manager",manager.Name,$"Messages:{manager.Id}",75,$"statement-manager:{interviewId}:{questionIndex}:{manager.Id}",timestamp);}
        if(teammate is not null&&(teamFirst||blame||boastful)){var text=OfflineDialogueLibrary.Statement(teammate,teamFirst,blame,referee,accountable,Seed(evtId,teammate.Id),spoken);db.AddAutomaticReaction(careerId,teammate.Id,evtId,SceneType.PrivateMessage,JsonSerializer.Serialize(new{automatic=true,eventId=evtId}),text,58,teamFirst?35:-30,"press statement reaction","Message",teammate.Name,$"Messages:{teammate.Id}",68,$"statement-teammate:{interviewId}:{questionIndex}:{teammate.Id}",timestamp);}
    }

    /// <summary>Mixes the event with the speaker, so two characters reacting to one event start in different places.</summary>
    private static int Seed(long eventId,long characterId){unchecked{var hash=(int)(eventId*397)^(int)(characterId*31)^(int)(characterId>>32);return hash&int.MaxValue;}}
    private static int Overall(Character character){try{using var json=JsonDocument.Parse(character.FactsJson);return json.RootElement.TryGetProperty("overall",out var value)?value.GetInt32():0;}catch(JsonException){return 0;}}
    private static bool ContainsAny(string text,params string[] values)=>values.Any(x=>text.Contains(x,StringComparison.OrdinalIgnoreCase));
    private static bool IsActive(Character character){try{using var json=System.Text.Json.JsonDocument.Parse(character.FactsJson);return !json.RootElement.TryGetProperty("providerActive",out var active)||active.ValueKind!=System.Text.Json.JsonValueKind.False;}catch(System.Text.Json.JsonException){return true;}}
}
