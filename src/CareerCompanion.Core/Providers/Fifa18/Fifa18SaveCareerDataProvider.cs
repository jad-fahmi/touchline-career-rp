using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using System.Text.Json;

namespace CareerCompanion.Core.Providers.Fifa18;

public sealed class Fifa18SaveCareerDataProvider(Database db) : ICareerDataProvider
{
    private readonly Fifa18SaveLocator _locator = new();
    private readonly Fifa18SaveParser _parser = new();
    private readonly Fifa18CareerNormalizer _normalizer = new();
    public string Name => "FIFA 18 Save";

    public async Task<Fifa18ParsedCareer> ParseLatestAsync(long? careerId,string? settingsDirectory=null,CancellationToken ct=default)
    {
        var path=_locator.FindLatestCareer(settingsDirectory)??throw new FileNotFoundException("No Career* save was found in the FIFA 18 settings directory.");
        var (data,fingerprint)=await _parser.ParseFileAsync(path,ct);
        Fifa18SyncState? prior=null;var payload=careerId is null?null:db.GetLatestProviderPayload(careerId.Value,Name);
        if(!string.IsNullOrWhiteSpace(payload)){try{prior=JsonSerializer.Deserialize<Fifa18SyncState>(payload);}catch(JsonException){db.Log("fifa_sync","Ignored malformed previous FIFA sync state.");}}
        return _normalizer.Normalize(data,path,fingerprint,prior);
    }

    public async Task<CareerSnapshot> GetSnapshotAsync(long careerId,CancellationToken cancellationToken=default)
    {
        var parsed=await ParseLatestAsync(careerId,null,cancellationToken);var local=db.GetCareer(careerId);
        var career=local with{PlayerName=parsed.PlayerName,Nationality=parsed.NationalityName,Club=parsed.ClubName,League=parsed.LeagueName,CurrentDate=parsed.CurrentDate,Position=parsed.Position,ShirtNumber=parsed.ShirtNumber,UpdatedAt=DateTime.UtcNow};
        IReadOnlyList<Character> squad=parsed.Squad.Select(x=>new Character(0,careerId,x.Name,x.Age,x.Nationality,parsed.ClubName,
            x.Position,"Squad member",CharacterType.Teammate,JsonSerializer.Serialize(new{provider=Name,playerId=x.PlayerId,
                overall=x.Overall,form=x.Form,injured=x.Injured,shirtNumber=x.ShirtNumber,classification=FactClassification.SaveFact.ToString()}),
            JsonSerializer.Serialize(Personality.Balanced),JsonSerializer.Serialize(CommunicationStyle.Balanced),"")).ToList();
        IReadOnlyList<CareerMatch> matches=parsed.LatestMatch is null?Array.Empty<CareerMatch>():[new CareerMatch(0,careerId,parsed.LatestMatch.ToMatchInput(),parsed.LatestMatch.TeamScore>parsed.LatestMatch.OpponentScore?"W":parsed.LatestMatch.TeamScore<parsed.LatestMatch.OpponentScore?"L":"D",parsed.CapturedAt)];
        return new(career,squad,matches,parsed.CapturedAt,Name);
    }
}
