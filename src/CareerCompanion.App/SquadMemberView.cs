using CareerCompanion.Core.Domain;
using System.Text.Json;

namespace CareerCompanion.App;

public sealed record SquadMemberView(
    Character Character,
    int? PlayerId,
    int? ShirtNumber,
    int? Overall,
    int? Form,
    bool Injured,
    bool Active,
    bool IsFifaFact)
{
    public long Id => Character.Id;
    public string Name => Character.Name;
    public string Position => Character.Position;
    public string Nationality => Character.Nationality;
    public int Age => Character.Age;
    public string SquadRole => Character.SquadRole;
    public string ShirtLabel => ShirtNumber is null ? "No number" : $"#{ShirtNumber}";
    public string OverallLabel => Overall is null ? "Not rated" : $"OVR {Overall}";
    public string FormLabel => Form is null ? "Form unavailable" : $"Form {Form}";
    public string Availability => Injured ? "INJURED" : Active ? IsFifaFact ? "NO INJURY FLAG" : "ACTIVE" : "FORMER TEAMMATE";
    public string SourceLabel => IsFifaFact ? "FIFA SAVE FACT" : "MANUAL CHARACTER";

    public static SquadMemberView From(Character character)
    {
        int? playerId=null,number=null,overall=null,form=null;var injured=false;var active=character.SquadRole!="Former teammate";var fifa=false;
        try
        {
            using var document=JsonDocument.Parse(character.FactsJson);var root=document.RootElement;
            playerId=ReadInt(root,"playerId");number=ReadInt(root,"shirtNumber");overall=ReadInt(root,"overall");form=ReadInt(root,"form");
            injured=root.TryGetProperty("injured",out var injury)&&injury.ValueKind is JsonValueKind.True;
            if(root.TryGetProperty("providerActive",out var state)&&state.ValueKind is JsonValueKind.True or JsonValueKind.False)active=state.GetBoolean();
            fifa=root.TryGetProperty("provider",out var provider)&&provider.GetString()=="FIFA 18 Save";
        }
        catch(JsonException){}
        return new(character,playerId,number,overall,form,injured,active,fifa);
    }

    private static int? ReadInt(JsonElement root,string name)
        => root.TryGetProperty(name,out var value)&&value.TryGetInt32(out var parsed)?parsed:null;
}
