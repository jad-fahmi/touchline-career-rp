using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using System.Text.Json;

namespace CareerCompanion.Core.Providers.Fifa18;

public sealed record Fifa18SupportingSyncResult(ProviderCharacterSyncResult? Squad, bool FixtureUpdated);

public sealed class Fifa18ImportService(Database db)
{
    public const string ProviderName = "FIFA 18 Save";

    public Fifa18SupportingSyncResult SyncSupportingFacts(long careerId,Fifa18ParsedCareer parsed,bool syncSquad)
    {
        ProviderCharacterSyncResult? squadResult=null;
        if(syncSquad)
        {
            var facts=parsed.Squad.Select(x=>new ProviderCharacterFact(x.PlayerId.ToString(),x.Name,x.Age,x.Nationality,
                parsed.ClubName,x.Position,"Squad member",CharacterType.Teammate,
                JsonSerializer.Serialize(new{provider=ProviderName,playerId=x.PlayerId,shirtNumber=x.ShirtNumber,
                    overall=x.Overall,form=x.Form,injured=x.Injured,classification=FactClassification.SaveFact.ToString()}),
                JsonSerializer.Serialize(x)));
            squadResult=db.SyncProviderCharacters(careerId,ProviderName,facts);
        }
        var fixtureUpdated=false;
        if(parsed.NextFixture is { } fixture)
        {
            db.UpsertFixture(careerId,ProviderName,fixture.EventKey,fixture.Date,fixture.Competition,fixture.Opponent,
                fixture.IsHome,fixture.Confidence,fixture.Evidence,parsed.FileFingerprint);
            db.SetSetting($"career:{careerId}:next",fixture.Opponent);
            fixtureUpdated=true;
        }
        return new(squadResult,fixtureUpdated);
    }
}
