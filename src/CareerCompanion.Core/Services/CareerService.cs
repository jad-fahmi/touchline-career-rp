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

    public MatchProcessingResult ProcessMatch(long careerId, MatchInput input)
    {
        var prior = db.GetMatches(careerId);
        var matchId = db.SaveMatch(careerId, input);
        var match = new CareerMatch(matchId, careerId, input, input.TeamScore > input.OpponentScore ? "W" : input.TeamScore < input.OpponentScore ? "L" : "D", DateTime.UtcNow);
        var detected = _events.Detect(careerId, matchId, input, prior).Select(e => e with { Id = db.SaveEvent(e) }).ToList();
        var chars = db.GetCharacters(careerId);
        var rels = chars.ToDictionary(x => x.Id, x => db.GetRelationship(x.Id));
        var reactions = detected.SelectMany(e => _reactions.Select(e, chars, rels)).DistinctBy(x => (x.CharacterId,x.Channel)).ToList();
        var narratives = UpdateNarratives(careerId, detected, prior.Append(match).ToList());
        if (!string.IsNullOrWhiteSpace(input.NextOpponent)) { db.SetSetting($"career:{careerId}:next", input.NextOpponent!); }
        db.Log("match", $"Processed match {matchId}; events={detected.Count}; reactions={reactions.Count}");
        return new(match, detected, reactions, narratives);
    }

    private IReadOnlyList<string> UpdateNarratives(long careerId, IReadOnlyList<CareerEvent> events, IReadOnlyList<CareerMatch> matches)
    {
        var active = new List<string>();
        if (events.Any(e => e.Type == "WINNING_STREAK")) active.Add("STRONG_FORM");
        if (events.Any(e => e.Type == "LOSING_STREAK")) active.Add("POOR_FORM");
        if (matches.TakeLast(5).Sum(m => m.Input.Goals) >= 6) active.Add("BREAKOUT_SEASON");
        if (matches.TakeLast(4).Sum(m => m.Input.Goals) == 0) active.Add("GOAL_DROUGHT");
        using var conn=db.Open();using(var decay=conn.CreateCommand()){decay.CommandText="UPDATE narratives SET strength=max(0,strength-5),status=CASE WHEN strength-5<=15 THEN 'faded' ELSE status END WHERE career_id=$c";decay.Parameters.AddWithValue("$c",careerId);decay.ExecuteNonQuery();}foreach(var type in active){using var cmd=conn.CreateCommand();cmd.CommandText="""INSERT INTO narratives(career_id,type,strength,status,last_updated,evidence_json) VALUES($c,$t,50,'active',$now,'[]') ON CONFLICT(career_id,type) DO UPDATE SET strength=min(100,strength+10),status='active',last_updated=$now""";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$t",type);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.ExecuteNonQuery();} return active;
    }
}
