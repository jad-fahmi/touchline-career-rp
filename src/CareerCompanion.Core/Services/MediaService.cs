using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;

namespace CareerCompanion.Core.Services;

public sealed record MediaGenerationResult(int NewsItems, int SocialPosts);

/// <summary>
/// Offline coverage of a match. Each outlet has its own angle and each headline is written from the
/// recorded facts, so the news page reads like reporting rather than a list of internal event names.
/// </summary>
public sealed class MediaService(Database db)
{
    private sealed record Outlet(string Name, string Voice);
    private static readonly Outlet[] Outlets =
    [
        new("The Football Desk", "broadsheet"),
        new("The Daily Strike", "tabloid"),
        new("Terrace Review", "club")
    ];

    public MediaGenerationResult GenerateDeterministic(long careerId, IEnumerable<CareerEvent> events,
        bool generateNews = true, bool generateSocial = true)
    {
        var list = events.ToList();
        if (list.Count == 0) return new(0, 0);
        var career = db.GetCareer(careerId);
        var player = career.PlayerName;
        var surname = player.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? player;
        var news = 0;
        var social = 0;

        foreach (var e in generateNews ? list.Where(x => x.Importance >= 48).Take(2) : [])
        {
            var outlet = Outlets[(int)(Math.Abs(e.Id) % Outlets.Length)];
            var headline = Headline(outlet, e, career, surname);
            var body = Body(outlet, e, career, player);
            db.AddNews(careerId, e.Id, outlet.Name, headline, body, Sentiment(e.Type), e.Importance, e.Timestamp);
            news++;
        }

        foreach (var e in generateSocial ? list.Where(x => x.Importance >= 55).Take(2) : [])
        {
            db.AddSocial(careerId, e.Id, "@MatchdayWire", "football account", Wire(e, surname), e.Timestamp);
            social++;
            if (e.Importance >= 75)
            {
                db.AddSocial(careerId, e.Id, "North Stand Voice", "supporter", Supporter(e, surname, career.Club), e.Timestamp);
                social++;
            }
        }
        return new(news, social);
    }

    private static string Sentiment(string type)
        => type.Contains("LOST") || type.Contains("RED") || type.Contains("DEFEAT") || type.Contains("LOSING") ? "negative"
            : type.Contains("DRAWN") || type.Contains("BENCHED") || type.Contains("RECORDED") ? "mixed" : "positive";

    private static string Headline(Outlet outlet, CareerEvent e, Career career, string surname) => e.Type switch
    {
        "PLAYER_HATTRICK" => outlet.Voice switch
        {
            "tabloid" => $"UNSTOPPABLE: three for {surname}",
            "club" => $"An afternoon that belongs to {surname}",
            _ => $"{surname} hat-trick decides it for {career.Club}"
        },
        "PLAYER_BRACE" => outlet.Voice == "tabloid" ? $"DOUBLE TROUBLE: {surname} strikes twice" : $"{surname} double lifts {career.Club}",
        "PLAYER_SCORED" => outlet.Voice == "club" ? $"{surname} on the scoresheet again" : $"{surname} goal shapes the result",
        "PLAYER_RED_CARD" => outlet.Voice == "tabloid" ? $"SEEING RED: {surname} sent off" : $"{surname} dismissal leaves {career.Club} short",
        "PLAYER_MISSED_PENALTY" => outlet.Voice == "tabloid" ? $"AGONY: {surname} misses from the spot" : $"Penalty miss costs {career.Club}",
        "LARGE_DEFEAT" => outlet.Voice == "tabloid" ? $"HUMBLED: questions for {career.Club}" : $"Heavy defeat raises questions at {career.Club}",
        "LATE_WINNER" => outlet.Voice == "tabloid" ? "SNATCHED IT: late drama" : $"Late winner rescues {career.Club}",
        "WINNING_STREAK" => outlet.Voice == "tabloid" ? $"ON A ROLL: {career.Club} do it again"
            : outlet.Voice == "club" ? "Another one, and the belief is growing" : $"{career.Club} extend the run",
        "LOSING_STREAK" => outlet.Voice == "tabloid" ? $"CRISIS TALK AT {career.Club.ToUpperInvariant()}"
            : outlet.Voice == "club" ? "This cannot go on much longer" : $"The run is becoming a problem for {career.Club}",
        "INTERNATIONAL_DEBUT" => $"{surname} wins a first senior cap",
        "INTERNATIONAL_GOAL" => $"{surname} scores on international duty",
        "FOOTBALL_RECORD_BROKEN" => outlet.Voice == "tabloid" ? $"HISTORY: {surname} into the record books" : $"{surname} enters the record books",
        "PLAYER_TRANSFERRED" => $"{surname} completes the move",
        "MATCH_WON" => outlet.Voice == "club" ? "Three points, and the mood lifts" : $"{career.Club} take the points",
        "MATCH_LOST" => outlet.Voice == "tabloid" ? $"FLAT: {career.Club} come up short" : $"{career.Club} beaten",
        "MATCH_DRAWN" => $"{career.Club} share the spoils",
        "MATCH_RECORDED" => $"{surname} adds another appearance",
        "RIVAL_MATCH" => outlet.Voice == "tabloid" ? "DERBY DAY DELIVERS" : $"Rivalry night for {career.Club}",
        _ => outlet.Voice == "tabloid" ? $"DRAMA: {e.Summary.TrimEnd('.')}" : e.Summary.TrimEnd('.')
    };

    private static string Body(Outlet outlet, CareerEvent e, Career career, string player)
    {
        var angle = outlet.Voice switch
        {
            "tabloid" => $"The talking points will follow {player} into the week, whatever the manager says publicly.",
            "club" => $"Supporters of {career.Club} will judge this one on what comes next rather than on a single afternoon.",
            _ => $"{career.Club} will review the detail before the next fixture in {career.League}."
        };
        return $"{e.Summary} {angle}";
    }

    private static string Wire(CareerEvent e, string surname) => e.Type switch
    {
        "PLAYER_HATTRICK" => $"THREE for {surname}. {e.Summary}",
        "PLAYER_BRACE" => $"Two goals for {surname} today. {e.Summary}",
        "PLAYER_RED_CARD" => $"Early bath for {surname}. {e.Summary}",
        "LARGE_DEFEAT" => $"That is a chastening afternoon. {e.Summary}",
        "LATE_WINNER" => $"Absolute scenes at the end of that one. {e.Summary}",
        "LOSING_STREAK" => $"The run goes on. {e.Summary}",
        "WINNING_STREAK" => $"They cannot stop winning. {e.Summary}",
        "FOOTBALL_RECORD_BROKEN" => $"One for the history books. {e.Summary}",
        _ => $"{e.Summary} Big moment in this career."
    };

    private static string Supporter(CareerEvent e, string surname, string club) => e.Type switch
    {
        "PLAYER_RED_CARD" => "We cannot keep giving teams a man advantage. Sort it out.",
        "LARGE_DEFEAT" => "That one hurts. The response matters now.",
        "LOSING_STREAK" => "Something has to change, and quickly.",
        "PLAYER_MISSED_PENALTY" => $"Still glad {surname} had the courage to take it.",
        "PLAYER_HATTRICK" => $"{surname} is ours and I will not be taking questions.",
        "LATE_WINNER" => "I have lost my voice and I regret nothing.",
        "INTERNATIONAL_DEBUT" => $"One of our own on the international stage. Proud day for {club}.",
        _ => e.Type.Contains("LOST") ? "That one hurts. The response matters now." : "What a night. That will be remembered."
    };
}
