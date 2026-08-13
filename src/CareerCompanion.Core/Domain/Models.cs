using System.Text.Json;

namespace CareerCompanion.Core.Domain;

public enum FactClassification { HistoricalFact, SaveFact, SimulatedInterpretation }
public enum CharacterType { Teammate, Manager, Agent, Opponent, Journalist, Pundit, MediaPersonality, Other }
public enum SceneType { PrivateMessage, DressingRoom, TrainingGround, PostMatch, PreMatch, PressConference, ManagerOffice, TransferDiscussion, Celebration, Conflict, Casual }

public sealed record Career(
    long Id, string SaveName, string PlayerName, string Nationality, int Age, string Club,
    string League, string Season, string CurrentDate, string Position, int ShirtNumber,
    DateTime CreatedAt, DateTime UpdatedAt, string? NextOpponent = null);

public sealed record Character(
    long Id, long CareerId, string Name, int Age, string Nationality, string Club,
    string Position, string SquadRole, CharacterType Type, string FactsJson,
    string PersonalityJson, string CommunicationJson, string HistoricalNotes,
    bool IsPublic = false)
{
    public CharacterProfile Profile => new(
        JsonSerializer.Deserialize<Personality>(PersonalityJson) ?? Personality.Balanced,
        JsonSerializer.Deserialize<CommunicationStyle>(CommunicationJson) ?? CommunicationStyle.Balanced);
}

public sealed record Personality(int Confidence, int Competitiveness, int Humor, int Openness,
    int Aggression, int Diplomacy, int Ambition, int Leadership, int Patience,
    int MediaComfort, int Loyalty, int Professionalism)
{
    public static Personality Balanced => new(55, 65, 45, 45, 35, 55, 65, 45, 55, 50, 60, 70);
}

public sealed record CommunicationStyle(string ResponseLength, int Directness, int Slang,
    int Humor, int Expressiveness, int Formality, int Avoidance, int PublicCriticism)
{
    public static CommunicationStyle Balanced => new("brief", 60, 25, 35, 45, 35, 25, 15);
}
public sealed record CharacterProfile(Personality Personality, CommunicationStyle Communication);

public sealed record Relationship(long CharacterId, int Score = 0, int Trust = 0, int Respect = 0,
    int Friendliness = 0, int Rivalry = 0, int Tension = 0, int Familiarity = 0);
public sealed record CharacterState(long CharacterId,string Mood="neutral",string Concerns="",string Ambitions="",
    int Satisfaction=50,string ReactionState="",DateTime UpdatedAt=default);

public sealed record MatchInput(string Date, string Competition, string Opponent, bool IsHome,
    int TeamScore, int OpponentScore, bool Started, int Minutes, int Goals, int Assists,
    double Rating, bool YellowCard, bool RedCard, bool PenaltyScored, bool PenaltyMissed,
    string Notes, string? NextOpponent = null, bool IsDerby = false, bool IsMajorFixture = false,
    bool StartedKnown = true);

public sealed record CareerMatch(long Id, long CareerId, MatchInput Input, string Result, DateTime CreatedAt);
public sealed record CareerFixture(long Id, long CareerId, string Provider, string EventKey, string Date,
    string Competition, string Opponent, bool IsHome, string Status, int Confidence, string Evidence,
    DateTime UpdatedAt);
public sealed record MatchReview(long Id, long CareerId, string Provider, string EventKey, string SourcePath,
    string FileFingerprint, DateTime CapturedAt, string MatchJson, string SnapshotJson, string Status,
    DateTime CreatedAt, DateTime UpdatedAt);
public sealed record PostMatchInterview(long Id, long CareerId, long MatchId, string TriggerType, int Importance,
    string QuestionsJson, string AnswersJson, int CurrentQuestion, string Status, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record InterviewTurn(string Question,string Answer,string JournalistResponse,bool AiGenerated);
public sealed record InterviewReply(string JournalistResponse,bool AiGenerated,int InputTokens=0,int OutputTokens=0);
public sealed record CareerNotification(long Id,long CareerId,string Kind,string Title,string Body,string Action,
    int Priority,bool IsRead,string DedupeKey,DateTime CreatedAt);
public sealed record CareerProgressSnapshot(long Id,long CareerId,DateTime CapturedAt,string CareerDate,string Club,
    string League,string Position,int ShirtNumber,int Overall,int Form,bool Injured,int Appearances,int Goals,
    int Assists,int YellowCards,int RedCards,string SourceFingerprint);
public sealed record ProviderCharacterFact(string ExternalId, string Name, int Age, string Nationality,
    string Club, string Position, string SquadRole, CharacterType Type, string FactsJson, string PayloadJson);
public sealed record ProviderCharacterSyncResult(int Added, int Updated, int MarkedInactive);
public sealed record CareerEvent(long Id, long CareerId, long? MatchId, string Type, DateTime Timestamp,
    int Importance, string EntitiesJson, string MetadataJson, string Summary,
    FactClassification Classification = FactClassification.SaveFact);
public sealed record Memory(long Id, long CareerId, long CharacterId, long? EventId, string Text,
    DateTime Timestamp, int Importance, int Valence, string Topic, bool Resolved,
    DateTime? LastRecalled, bool IsCompressed = false);
public sealed record Narrative(long Id, long CareerId, string Type, int Strength, string Status,
    DateTime LastUpdated, string EvidenceJson);
public sealed record ConversationMessage(string Role, string Content, DateTime Timestamp)
{
    public string DisplayRole=>Role.ToLowerInvariant() switch{"user"=>"YOU","assistant"=>"INCOMING","journalist"=>"JOURNALIST",_=>Role.ToUpperInvariant()};
}
public sealed record NewsItem(long Id, long CareerId, long? EventId, string Outlet, string Headline,
    string Body, string Sentiment, int Importance, DateTime PublishedAt);
public sealed record SocialPost(long Id, long CareerId, long? EventId, string Author, string Persona,
    string Content, DateTime PublishedAt);
public sealed record GenerationJob(long Id,long CareerId,long? EventId,string Kind,string DedupeKey,string Status,
    int Attempts,string? Error,string PayloadJson,DateTime CreatedAt,DateTime UpdatedAt);
public sealed record GenerationResult(string Text, string Mood, int RelationshipDelta, int TrustDelta,
    int RespectDelta, IReadOnlyList<string> Memories, int InputTokens = 0, int OutputTokens = 0,
    string Raw = "");

public sealed record CareerSnapshot(Career Career, IReadOnlyList<Character> Squad,
    IReadOnlyList<CareerMatch> RecentMatches, DateTime CapturedAt, string Provider);
