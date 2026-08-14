namespace CareerCompanion.Core.Services;

public static class DialogueResponseGuard
{
    private static readonly string[] PromptLeakMarkers=[
        "We need to respond as ",
        "We need to output ",
        "Return only valid JSON",
        "You write believable football dialogue",
        "Respond as this character",
        "Relationship: score ",
        "Relevant save events:",
        "Relevant memories:",
        "Recent messages:",
        "Private player state:",
        "Verified/provider facts JSON:",
        "Current career date/season:",
        "Initiate one natural private message to ",
        "Current journalist question:",
        "Verified match facts:",
        "<think>",
        "</think>",
        "system prompt",
        "newMemories"
    ];

    public static bool IsUsable(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return false;
        var value=text.Trim();
        if(value.Length>1800||value.StartsWith("{")||value.StartsWith("["))return false;
        if(value.StartsWith("system:",StringComparison.OrdinalIgnoreCase)||value.StartsWith("user:",StringComparison.OrdinalIgnoreCase))return false;
        return !PromptLeakMarkers.Any(marker=>value.Contains(marker,StringComparison.OrdinalIgnoreCase));
    }
}
