namespace CareerCompanion.App;

/// <summary>A team-mate's line in the latest match report, formatted for display.</summary>
public sealed record MatchPerformanceView(string Name, string Position, string Role, string Minutes, string Rating);
