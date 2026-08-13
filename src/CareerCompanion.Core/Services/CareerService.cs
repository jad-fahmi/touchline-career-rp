using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Simulation;

namespace CareerCompanion.Core.Services;

public sealed record MatchProcessingResult(CareerMatch Match, IReadOnlyList<CareerEvent> Events,
    IReadOnlyList<ReactionTarget> Reactions, IReadOnlyList<string> Narratives);

public sealed class CareerService(Database db)
{
    private readonly EventEngine _events = new();
    private readonly ReactionEngine _reactions = new();

    public MatchProcessingResult ProcessMatch(long careerId, MatchInput input,string? provider=null,string? providerEventKey=null)
    {
        var prior = db.GetMatches(careerId);
        var matchId=!string.IsNullOrWhiteSpace(provider)&&!string.IsNullOrWhiteSpace(providerEventKey)?db.SaveProviderMatch(careerId,provider,providerEventKey,input).MatchId:db.SaveMatch(careerId,input);
        var match = db.GetMatch(careerId,matchId);prior=prior.Where(x=>x.Id!=matchId).ToList();
        var detected = _events.Detect(careerId, matchId, match.Input, prior).Select(e => e with { Id = db.SaveEvent(e) }).ToList();
        foreach(var breakthrough in FootballRecordDetector.Detect(match,prior))
        {
            var summary=$"RECORD BROKEN: {breakthrough.Record.Name}. {breakthrough.Summary}";
            var recordEvent=new CareerEvent(0,careerId,matchId,"FOOTBALL_RECORD_BROKEN",DateTime.TryParse(match.Input.Date,out var recordDate)?recordDate:DateTime.UtcNow,96,
                "[]",System.Text.Json.JsonSerializer.Serialize(new{breakthrough.Record.Key,breakthrough.Record.Name,breakthrough.NewValue,breakthrough.Record.Benchmark,breakthrough.Record.Holder,breakthrough.Record.SourceUrl,breakthrough.Record.Evidence}),summary,FactClassification.HistoricalFact);
            detected.Add(recordEvent with {Id=db.SaveEvent(recordEvent)});
        }
        var chars = db.GetCharacters(careerId);
        var rels = chars.ToDictionary(x => x.Id, x => db.GetRelationship(x.Id));
        var reactions = detected.SelectMany(e => _reactions.Select(e, chars, rels)).DistinctBy(x => (x.CharacterId,x.Channel)).ToList();
        var narratives = UpdateNarratives(careerId,matchId, detected, prior.Append(match).ToList());
        if (!string.IsNullOrWhiteSpace(match.Input.NextOpponent)) { db.SetSetting($"career:{careerId}:next", match.Input.NextOpponent!); }
        db.Log("match", $"Processed match {matchId}; events={detected.Count}; reactions={reactions.Count}");
        return new(match, detected, reactions, narratives);
    }

    public MatchProcessingResult UpdateMatch(long careerId,long matchId,MatchInput input)
    {
        _=db.GetMatch(careerId,matchId);db.ClearGeneratedMatchWorld(careerId,matchId);db.UpdateMatch(careerId,matchId,input);
        var match=db.GetMatch(careerId,matchId);var prior=db.GetMatches(careerId,500).Where(x=>x.Id!=matchId&&string.CompareOrdinal(x.Input.Date,match.Input.Date)<=0).ToList();
        var detected=_events.Detect(careerId,matchId,match.Input,prior).Select(e=>e with{Id=db.SaveEvent(e)}).ToList();
        var chars=db.GetCharacters(careerId);var rels=chars.ToDictionary(x=>x.Id,x=>db.GetRelationship(x.Id));
        var reactions=detected.SelectMany(e=>_reactions.Select(e,chars,rels)).DistinctBy(x=>(x.CharacterId,x.Channel)).ToList();
        var narratives=UpdateNarratives(careerId,matchId,detected,prior.Append(match).OrderBy(x=>x.Input.Date).ToList());
        db.Log("match",$"Corrected match {matchId}; events={detected.Count}; reactions={reactions.Count}");return new(match,detected,reactions,narratives);
    }

    private IReadOnlyList<string> UpdateNarratives(long careerId,long matchId, IReadOnlyList<CareerEvent> events, IReadOnlyList<CareerMatch> matches)
    {
        var active = new List<string>();
        if (events.Any(e => e.Type == "WINNING_STREAK")) active.Add("STRONG_FORM");
        if (events.Any(e => e.Type == "LOSING_STREAK")) active.Add("POOR_FORM");
        if (matches.TakeLast(5).Sum(m => m.Input.Goals) >= 6) active.Add("BREAKOUT_SEASON");
        if (matches.TakeLast(4).Sum(m => m.Input.Goals) == 0) active.Add("GOAL_DROUGHT");
        db.ApplyNarrativesOnce(careerId,matchId,active);return active;
    }
}
