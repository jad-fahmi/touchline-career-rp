using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using System.Text.Json;

namespace CareerCompanion.Core.Providers.Fifa18;

public sealed record Fifa18SupportingSyncResult(ProviderCharacterSyncResult? Squad, bool FixtureUpdated,int WorldUpdates=0);

public sealed class Fifa18ImportService(Database db)
{
    public const string ProviderName = "FIFA 18 Save";

    public Fifa18SupportingSyncResult SyncSupportingFacts(long careerId,Fifa18ParsedCareer parsed,bool syncSquad,bool autoTeammates=true,bool autoManager=true)
    {
        ProviderCharacterSyncResult? squadResult=null;var worldUpdates=0;var before=db.GetCharacters(careerId).Where(x=>x.Type==CharacterType.Teammate&&IsProviderCharacter(x)&&IsActive(x)).ToList();var hadProviderSquad=before.Count>0;
        if(syncSquad)
        {
            var facts=parsed.Squad.Where(x=>x.PlayerId!=parsed.PlayerId).Select(x=>new ProviderCharacterFact(x.PlayerId.ToString(),x.Name,x.Age,x.Nationality,
                parsed.ClubName,x.Position,"Squad member",CharacterType.Teammate,
                JsonSerializer.Serialize(new{provider=ProviderName,playerId=x.PlayerId,shirtNumber=x.ShirtNumber,
                    overall=x.Overall,form=x.Form,injured=x.Injured,simulatedSquadRole=x.Overall>=85?"Key player":x.Overall>=78?"First team":"Squad member",classification=FactClassification.SaveFact.ToString()}),
                JsonSerializer.Serialize(x)));
            squadResult=db.SyncProviderCharacters(careerId,ProviderName,facts);
            if(hadProviderSquad)
            {
                var after=db.GetCharacters(careerId).Where(x=>x.Type==CharacterType.Teammate&&IsProviderCharacter(x)).ToList();var oldIds=before.Select(ProviderPlayerId).Where(x=>x is not null).ToHashSet();var currentIds=parsed.Squad.Select(x=>x.PlayerId.ToString()).ToHashSet();var arrivals=after.Where(x=>IsActive(x)&&ProviderPlayerId(x) is { } id&&!oldIds.Contains(id)).ToList();var departures=before.Where(x=>ProviderPlayerId(x) is { } id&&!currentIds.Contains(id)).ToList();worldUpdates+=new Services.AutomaticWorldService(db).ApplySquadChanges(careerId,parsed.FileFingerprint,arrivals,departures,parsed.CurrentDate,autoTeammates);
            }
        }
        var fixtureUpdated=false;CareerFixture? storedFixture=null;
        if(parsed.NextFixture is { } fixture)
        {
            db.UpsertFixture(careerId,ProviderName,fixture.EventKey,fixture.Date,fixture.Competition,fixture.Opponent,
                fixture.IsHome,fixture.Confidence,fixture.Evidence,parsed.FileFingerprint,fixture.TeamContext,fixture.RepresentingTeam,fixture.Availability);
            db.SetSetting($"career:{careerId}:next",fixture.Opponent);
            db.SetSetting($"career:{careerId}:player_availability",parsed.PlayerAvailability);
            db.SetSetting($"career:{careerId}:opponent_scout",JsonSerializer.Serialize(parsed.OpponentScout));
            storedFixture=db.GetFixtures(careerId).First(x=>x.EventKey==fixture.EventKey);
            fixtureUpdated=true;
        }
        var characters=db.GetCharacters(careerId);
        if(!string.IsNullOrWhiteSpace(parsed.ManagerName))
        {
            var providerManagers=characters.Where(x=>x.Type==CharacterType.Manager&&IsProviderStaff(x)).ToList();var existing=characters.FirstOrDefault(x=>x.Type==CharacterType.Manager&&x.Name.Equals(parsed.ManagerName,StringComparison.OrdinalIgnoreCase));
            foreach(var old in providerManagers.Where(x=>x.Id!=existing?.Id))db.UpdateProviderStaff(old.Id,ProviderName,old.Club,"Former manager",false);
            if(existing is null){var id=db.AddCharacter(careerId,parsed.ManagerName,0,"",parsed.ClubName,"Manager","Manager",CharacterType.Manager,StablePersonality(parsed.ManagerName),CommunicationStyle.Balanced);db.UpdateProviderStaff(id,ProviderName,parsed.ClubName,"Manager",true);}else db.UpdateProviderStaff(existing.Id,ProviderName,parsed.ClubName,"Manager",true);
        }
        if(!string.IsNullOrWhiteSpace(parsed.AgentName))
        {
            characters=db.GetCharacters(careerId);var providerAgents=characters.Where(x=>x.Type==CharacterType.Agent&&IsProviderStaff(x)).ToList();var existing=characters.FirstOrDefault(x=>x.Type==CharacterType.Agent&&x.Name.Equals(parsed.AgentName,StringComparison.OrdinalIgnoreCase));
            foreach(var old in providerAgents.Where(x=>x.Id!=existing?.Id))db.UpdateProviderStaff(old.Id,ProviderName,old.Club,"Former representative",false);
            if(existing is null){var id=db.AddCharacter(careerId,parsed.AgentName,0,"","","Agent","Representative",CharacterType.Agent,StablePersonality(parsed.AgentName),new("brief",70,5,10,35,65,20,10));db.UpdateProviderStaff(id,ProviderName,"","Representative",true);}else db.UpdateProviderStaff(existing.Id,ProviderName,"","Representative",true);
        }
        if(storedFixture is not null)worldUpdates+=new Services.AutomaticWorldService(db).ApplyPreMatch(careerId,storedFixture,parsed.OpponentScout,autoTeammates,autoManager);
        var worldNews=parsed.WorldNews??Array.Empty<Fifa18WorldNews>();
        for(var index=0;index<worldNews.Count;index++)
        {
            var item=worldNews[index];db.AddProviderNews(careerId,item.EventKey,item.Title,string.IsNullOrWhiteSpace(item.Body)?item.Title:item.Body,item.Importance,item.Date,index<12);
        }
        var nationalKey=$"career:{careerId}:fifa_national_team_id";var previousNational=db.GetSetting(nationalKey);if(parsed.NationalTeamId>0&&previousNational!=parsed.NationalTeamId.ToString())
        {
            var team=string.IsNullOrWhiteSpace(parsed.NationalTeamName)?parsed.NationalityName:parsed.NationalTeamName;var timestamp=DateTime.TryParse(parsed.CurrentDate,out var date)?date:parsed.CapturedAt;var summary=$"Selected for international duty with {team}.";db.AddNotification(careerId,"International","International call-up",summary,"Timeline",90,$"national-callup:{parsed.FileFingerprint}:{parsed.NationalTeamId}",timestamp);db.SaveEvent(new(0,careerId,null,"NATIONAL_TEAM_CALLED_UP",timestamp,90,"[]",JsonSerializer.Serialize(new{parsed.NationalTeamId,TeamName=team,parsed.FileFingerprint}),summary,FactClassification.SaveFact));
        }
        db.SetSetting(nationalKey,parsed.NationalTeamId.ToString());
        return new(squadResult,fixtureUpdated,worldUpdates);
    }

    private static Personality StablePersonality(string seed)
    {
        var hash=StableHash(seed);int V(int shift,int min=35,int span=45)=>min+(hash>>shift&0x7fffffff)%span;return new(V(0),V(2),V(4,15,45),V(6),V(8,20,45),V(10),V(12),V(14),V(16),V(18),V(20),V(22,55,35));
    }
    private static bool IsProviderStaff(Character character){try{using var json=JsonDocument.Parse(character.FactsJson);return json.RootElement.TryGetProperty("provider",out var provider)&&provider.GetString()==ProviderName;}catch(JsonException){return false;}}
    private static bool IsProviderCharacter(Character character)=>IsProviderStaff(character);
    private static bool IsActive(Character character){try{using var json=JsonDocument.Parse(character.FactsJson);return !json.RootElement.TryGetProperty("providerActive",out var active)||active.ValueKind!=JsonValueKind.False;}catch(JsonException){return true;}}
    private static string? ProviderPlayerId(Character character){try{using var json=JsonDocument.Parse(character.FactsJson);return json.RootElement.TryGetProperty("playerId",out var value)?value.ToString():null;}catch(JsonException){return null;}}
    private static int StableHash(string seed){unchecked{uint hash=2166136261;foreach(var c in seed){hash^=c;hash*=16777619;}return(int)(hash&0x7fffffff);}}
}
