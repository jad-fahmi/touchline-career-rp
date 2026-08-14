namespace CareerCompanion.Core.Services;

public static class DialogueResponseGuard
{
    private static readonly string[] PromptLeakMarkers=[
        "We need to respond as ",
        "We need to output ",
        "Return only valid JSON",
        "Current journalist question:",
        "Verified match facts:",
        "system prompt",
        "newMemories"
    ];

    public static bool IsUsable(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return false;
        var value=text.Trim();
        if(value.Length>1800||value.StartsWith("{")||value.StartsWith("["))return false;
        return !PromptLeakMarkers.Any(marker=>value.Contains(marker,StringComparison.OrdinalIgnoreCase));
    }
}
