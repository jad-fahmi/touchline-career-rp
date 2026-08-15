using CareerCompanion.Core.LLM;
using System.Text.RegularExpressions;

namespace CareerCompanion.Core.Services;

/// <summary>
/// Turns whatever a model returned into something a character can actually say, or rejects it.
/// Formatting damage (JSON wrappers, code fences, reasoning blocks, speaker labels, stray quotes) is
/// repaired rather than thrown away, because every discarded reply becomes an offline fallback the
/// player did not ask for. Only two things are genuinely unusable: leaked prompt or reasoning text,
/// and language that steps outside the football world.
/// </summary>
public static partial class DialogueResponseGuard
{
    /// <summary>
    /// Language that would tell the player they are inside a video game. Characters live in the football
    /// world: a manager picks the team, a physio decides a return date, a club negotiates a transfer.
    /// Nothing is decided by FIFA, a save file, or a piece of software, and no character has ever heard
    /// of any of them.
    /// </summary>
    // Football words are deliberately absent from this list. A keeper makes "a save", a midfield is "the
    // engine room", and a squad plays "a system", so only phrasing that could not belong to football is here.
    private static readonly string[] FourthWallMarkers=[
        "fifa","ea sports","career mode","save file","save game","savegame",
        "video game","videogame","in-game","game data","match data","the data","player data","my stats screen",
        "the simulation","simulated","the simulator","this app","the app ","the database","the parser",
        "the provider","provider data","not been imported","the import","the algorithm","the developer",
        "hasn't been confirmed by","has not been confirmed by","not confirmed by",
        "as an ai","language model","my programming"
    ];

    /// <summary>
    /// The meta phrasings that share their words with ordinary football talk. A keeper makes "a save" and a
    /// match can "decide itself", so these need the surrounding words before they mean anything is wrong.
    /// </summary>
    [GeneratedRegex(@"\b(?:in|from|on|to|inside)\s+(?:your|my|the|this)\s+save\b|\b(?:your|my|the|this)\s+save\s+(?:file|game|data|does|doesn't|has|hasn't|shows|says|lists|contains)\b|\b(?:the game|the system|the app|the software|the data)\s+(?:has not|hasn't|have not|haven't|will|would|does not|doesn't|did not|didn't|only)?\s*(?:decide[sd]?|pick[s|ed]?|select[s|ed]?|name[sd]?|confirm[s|ed]?)\s+(?:if|whether|who|you|your|the team|the side|the squad|the line|yet)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]
    private static partial Regex FourthWallPhrase();

    /// <summary>Speaker labels a model sometimes prefixes, such as "Marco:" or "Assistant (manager):".</summary>
    [GeneratedRegex(@"^\s*[A-Za-z][\w'\-. ]{0,40}(\([^)]{0,30}\))?\s*[:\-]\s+",RegexOptions.CultureInvariant)]
    private static partial Regex SpeakerLabel();

    [GeneratedRegex(@"<think>.*?</think>|<thinking>.*?</thinking>|<reasoning>.*?</reasoning>",RegexOptions.Singleline|RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]
    private static partial Regex ReasoningBlock();

    [GeneratedRegex(@"\s{2,}",RegexOptions.CultureInvariant)]
    private static partial Regex ExtraSpace();

    /// <summary>
    /// FIFA is also the governing body, and the career's own competition names include "FIFA WC Qualifiers".
    /// A character discussing World Cup qualification is inside the football world, not outside it, so those
    /// phrases are removed before the bare word is treated as a leak.
    /// </summary>
    [GeneratedRegex(@"\bfifa(\s+(club\s+)?(world\s+cup|wc|confederations\s+cup|rankings?|world\s+rankings?)(\s+qualifiers?)?)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]
    private static partial Regex FootballGoverningBody();

    /// <summary>
    /// Labels a model attaches to its own output instead of speaking. "User Safety: safe" reached a press
    /// conference this way: it is not prompt text and not a fourth-wall break, so nothing else catches it.
    /// A whole reply that is one short "Label: value" with no sentence is never dialogue.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z][A-Za-z ]{2,24}:\s*[A-Za-z0-9 _/-]{1,40}$",RegexOptions.CultureInvariant)]
    private static partial Regex BareLabel();

    private static readonly string[] MetadataMarkers=[
        "user safety","content policy","policy violation","safety rating","flagged as","moderation",
        "system note","internal note","confidence score","sentiment:","toxicity"
    ];

    /// <summary>Why a reply could not be used, so the caller can ask the model for a corrected one.</summary>
    public enum Rejection { None, Empty, PromptEcho, FourthWall }

    public static bool IsUsable(string? text)=>TryPrepare(text,null,out _,out _);

    /// <summary>
    /// Repairs a model reply and reports whether it can be shown. <paramref name="speakerName"/> lets the
    /// guard strip a "Name:" prefix the model added in front of its own line.
    /// </summary>
    public static bool TryPrepare(string? text,string? speakerName,out string dialogue,out Rejection rejection)
    {
        dialogue="";
        rejection=Rejection.Empty;
        var value=Repair(text,speakerName);
        if(value.Length==0)return false;
        if(DialoguePayload.LooksLikePrompt(value)||IsModelMetadata(value)){rejection=Rejection.PromptEcho;return false;}
        if(BreaksFourthWall(value)){rejection=Rejection.FourthWall;return false;}
        dialogue=value;
        rejection=Rejection.None;
        return true;
    }

    /// <summary>True when the model annotated its output instead of speaking as the character.</summary>
    public static bool IsModelMetadata(string text)
    {
        if(BareLabel().IsMatch(text.Trim()))return true;
        var lower=text.ToLowerInvariant();
        return MetadataMarkers.Any(marker=>lower.Contains(marker,StringComparison.Ordinal));
    }

    /// <summary>True when the line mentions the game, the save, or the software behind the career.</summary>
    public static bool BreaksFourthWall(string text)
    {
        var padded=" "+ExtraSpace().Replace(text.ToLowerInvariant().Replace('\n',' ')," ")+" ";
        padded=FootballGoverningBody().Replace(padded,m=>m.Groups[1].Value);
        return FourthWallMarkers.Any(marker=>padded.Contains(marker,StringComparison.Ordinal))||FourthWallPhrase().IsMatch(padded);
    }

    private static string Repair(string? text,string? speakerName)
    {
        if(string.IsNullOrWhiteSpace(text))return "";
        var value=ReasoningBlock().Replace(text,"");
        var openThought=value.IndexOf("<think>",StringComparison.OrdinalIgnoreCase);
        if(openThought>=0)value=value[..openThought];                       // reasoning that was never closed
        value=DialoguePayload.Unfence(value).Trim();
        value=Unwrap(value);
        value=StripLabel(value,speakerName);
        value=value.Trim().Trim('"','“','”','\'').Trim();
        value=ExtraSpace().Replace(value.Replace("\r\n","\n"),m=>m.Value.Contains('\n')?"\n":" ").Trim();
        if(value.Contains('{')||value.Contains('}'))return "";              // a wrapper we could not open
        return value.Length>1800?Shorten(value):value;
    }

    /// <summary>Takes the spoken line out of a JSON wrapper, whether the wrapper is complete or truncated.</summary>
    private static string Unwrap(string value)
    {
        if(!value.StartsWith('{')&&!value.Contains("\"dialogue\"",StringComparison.OrdinalIgnoreCase))return value;
        return DialoguePayload.TryExtractDialogueField(value,out var dialogue)?dialogue:value;
    }

    private static string StripLabel(string value,string? speakerName)
    {
        var match=SpeakerLabel().Match(value);
        if(!match.Success)return value;
        var label=match.Value.TrimEnd(':','-',' ').Trim();
        var names=new List<string>{"assistant","ai","character","reply","response","dialogue","answer"};
        if(!string.IsNullOrWhiteSpace(speakerName))
        {
            names.Add(speakerName.Trim());
            names.AddRange(speakerName.Split(' ',StringSplitOptions.RemoveEmptyEntries));
        }
        // Only a label that names the speaker is removed. "Listen: you were poor today" must survive.
        var head=label.Split('(')[0].Trim();
        return names.Any(name=>string.Equals(name,head,StringComparison.OrdinalIgnoreCase))?value[match.Length..].Trim():value;
    }

    /// <summary>Trims an over-long reply back to its last complete sentence rather than mid-word.</summary>
    private static string Shorten(string value)
    {
        var window=value[..1800];
        var cut=window.LastIndexOfAny(['.','!','?']);
        return cut>200?window[..(cut+1)]:window.TrimEnd()+"...";
    }
}
