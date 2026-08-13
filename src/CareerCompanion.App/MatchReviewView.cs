using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Providers.Fifa18;
using System.IO;
using System.Text.Json;

namespace CareerCompanion.App;

public sealed class MatchReviewView
{
    public MatchReview Review { get; }
    public Fifa18DetectedMatch Match { get; }
    public Fifa18ParsedCareer Snapshot { get; }
    public long Id => Review.Id;
    public string Score => $"{Match.TeamScore} - {Match.OpponentScore}";
    public string Opponent => Match.Opponent;
    public string Date => Match.Date;
    public string Competition => Match.Competition;
    public string Venue => Match.IsHome ? "HOME" : "AWAY";
    public string Confidence => $"{Match.Confidence}% CONFIDENCE";
    public string Evidence => Match.Evidence;
    public string Captured => $"Detected {Review.CapturedAt.ToLocalTime():dd MMM yyyy, HH:mm}";
    public string Source => Path.GetFileName(Review.SourcePath);

    private MatchReviewView(MatchReview review,Fifa18DetectedMatch match,Fifa18ParsedCareer snapshot)
        => (Review,Match,Snapshot)=(review,match,snapshot);

    public static MatchReviewView From(MatchReview review)
    {
        var match=JsonSerializer.Deserialize<Fifa18DetectedMatch>(review.MatchJson)
            ?? throw new InvalidDataException("The staged match payload is invalid.");
        var snapshot=JsonSerializer.Deserialize<Fifa18ParsedCareer>(review.SnapshotJson)
            ?? throw new InvalidDataException("The staged FIFA snapshot is invalid.");
        return new(review,match,snapshot);
    }
}
