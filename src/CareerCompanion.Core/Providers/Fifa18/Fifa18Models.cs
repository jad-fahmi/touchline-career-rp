using CareerCompanion.Core.Domain;

namespace CareerCompanion.Core.Providers.Fifa18;

public sealed record Fifa18SyncState(
    int PlayerId,
    int ClubTeamId,
    int CareerDate,
    int Season,
    int Appearances,
    int Goals,
    int Assists,
    int YellowCards,
    int RedCards,
    int LatestRatingKey,
    int LatestRatingDate);

public sealed record Fifa18DetectedMatch(
    string EventKey,
    string Date,
    string Competition,
    string Opponent,
    bool IsHome,
    int TeamScore,
    int OpponentScore,
    bool Started,
    int Minutes,
    int Goals,
    int Assists,
    double Rating,
    bool YellowCard,
    bool RedCard,
    int Confidence,
    string Evidence,
    bool RequiresReview,
    bool StartedKnown = false)
{
    public MatchInput ToMatchInput() => new(Date, Competition, Opponent, IsHome,
        TeamScore, OpponentScore, Started, Minutes, Goals, Assists, Rating,
        YellowCard, RedCard, false, false, Evidence,StartedKnown:StartedKnown);
}

public sealed record Fifa18SquadMember(
    int PlayerId,
    string Name,
    string Nationality,
    int Age,
    string Position,
    int ShirtNumber,
    int Overall,
    int Form,
    bool Injured);

public sealed record Fifa18DetectedFixture(
    string EventKey,
    string Date,
    string Competition,
    string Opponent,
    bool IsHome,
    int Confidence,
    string Evidence);

public sealed record Fifa18WorldNews(string EventKey,string Date,string Title,string Body,int Importance);

public sealed record Fifa18ParsedCareer(
    string SourcePath,
    string FileFingerprint,
    DateTime CapturedAt,
    string PlayerName,
    int PlayerId,
    int NationalityId,
    string NationalityName,
    int Age,
    string ClubName,
    int ClubTeamId,
    int LeagueId,
    string LeagueName,
    string Season,
    string CurrentDate,
    string Position,
    int ShirtNumber,
    Fifa18SyncState State,
    Fifa18DetectedMatch? LatestMatch,
    IReadOnlyList<Fifa18SquadMember> Squad,
    Fifa18DetectedFixture? NextFixture,
    int ParsedSquadMembers,
    IReadOnlyList<string> Diagnostics,
    int PlayerOverall = 0,
    int PlayerForm = 0,
    bool PlayerInjured = false,
    string ManagerName = "",
    string AgentName = "",
    IReadOnlyList<Fifa18WorldNews>? WorldNews = null,
    bool PlayerInjuryKnown = false,
    int NationalTeamId = -1,
    string NationalTeamName = "");

public enum Fifa18ScanDisposition { MatchDetected, MatchAutoImported, NoNewMatch, CareerMismatch, NoCareerSelected }
public sealed record Fifa18ScanResult(Fifa18ScanDisposition Disposition, Fifa18ParsedCareer Parsed, string Message);
