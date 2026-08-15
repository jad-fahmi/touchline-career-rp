namespace CareerCompanion.Core.Services;

/// <summary>
/// Ages phrased the way a person would say them. The player looking at a sync indicator is asking whether
/// what they just played has arrived, and "a minute ago" answers that; a timestamp makes them do the
/// subtraction themselves.
/// </summary>
public static class RelativeTime
{
    public static string Since(DateTime moment, DateTime now)
    {
        var age = now - moment;
        // A clock that is slightly behind, or a save written by a machine in another time zone, must not
        // produce "in 3 minutes" on a sync indicator.
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        return age switch
        {
            { TotalSeconds: < 45 } => "just now",
            { TotalMinutes: < 2 } => "a minute ago",
            { TotalMinutes: < 60 } => $"{(int)age.TotalMinutes} minutes ago",
            { TotalHours: < 2 } => "an hour ago",
            { TotalHours: < 24 } => $"{(int)age.TotalHours} hours ago",
            { TotalDays: < 2 } => "yesterday",
            _ => $"{(int)age.TotalDays} days ago"
        };
    }
}
