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
    bool StartedKnown = false,
    string TeamContext = "Club",
    string RepresentingTeam = "",
    bool ScoreKnown = true,
    bool IsDerby = false,
    int OpponentTeamId = -1,
    IReadOnlyList<Fifa18SquadPerformance>? TeamPerformances = null,
    bool IsHomeKnown = false,
    int AppearanceKey = -1)
{
    public string ScoreLabel => ScoreKnown ? $"{TeamScore}-{OpponentScore}" : "Score unknown";
    public bool OpponentKnown => !string.IsNullOrWhiteSpace(Opponent) && Opponent != "Opponent unknown";
    public MatchInput ToMatchInput() => new(Date, Competition, Opponent, IsHome,
        TeamScore, OpponentScore, Started, Minutes, Goals, Assists, Rating,
        YellowCard, RedCard, false, false, Evidence,IsDerby:IsDerby,StartedKnown:StartedKnown,
        TeamContext:TeamContext,RepresentingTeam:RepresentingTeam,ScoreKnown:ScoreKnown,IsHomeKnown:IsHomeKnown);
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
    string Evidence,
    string TeamContext = "Club",
    string RepresentingTeam = "",
    string Availability = "Unknown",
    bool IsHomeKnown = true);

/// <summary>A final score FIFA published for a date the career already has an appearance for.</summary>
public sealed record Fifa18ResolvedResult(string Date,string Opponent,int TeamScore,int OpponentScore,string Evidence);
public sealed record Fifa18WorldNews(string EventKey,string Date,string Title,string Body,int Importance,
    bool AboutPlayer=false,bool AboutClub=false);
public sealed record Fifa18TransferRequestSignal(string EventKey,string Date,string Status,string Evidence);
public sealed record Fifa18ScoutPlayer(string Name,string Position,int Overall);
public sealed record Fifa18OpponentScout(string TeamName,string ManagerName,string StadiumName,bool IsRival,
    IReadOnlyList<Fifa18ScoutPlayer> KeyPlayers,string SourceEvidence);

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
    string NationalTeamName = "",
    Fifa18OpponentScout? OpponentScout = null,
    string PlayerAvailability = "Unknown",
    Fifa18TransferRequestSignal? TransferRequest = null,
    IReadOnlyList<Fifa18DetectedMatch>? NewMatches = null,
    IReadOnlyList<Fifa18Appearance>? Appearances = null,
    int MissedClubMatches = 0,
    IReadOnlyList<CachedProviderArticle>? ArticleCache = null,
    IReadOnlyList<Fifa18ResolvedResult>? ResolvedResults = null)
{
    /// <summary>Every appearance detected since the last import, oldest first.</summary>
    public IReadOnlyList<Fifa18DetectedMatch> PendingMatches => NewMatches ?? (LatestMatch is null ? [] : [LatestMatch]);
}

public enum Fifa18ScanDisposition { MatchDetected, MatchAutoImported, NoNewMatch, CareerMismatch, NoCareerSelected }
public sealed record Fifa18ScanResult(Fifa18ScanDisposition Disposition, Fifa18ParsedCareer Parsed, string Message);
