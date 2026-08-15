using CareerCompanion.Core.Domain;
using System.Text.Json;

namespace CareerCompanion.Core.LLM;

/// <summary>
/// Reads dialogue out of whatever a model actually returned. Providers differ in how they wrap an answer
/// and reasoning models add extra output items before the message, so every provider shares one tolerant
/// reader: find the message text in the envelope, then salvage the dialogue from strict JSON, a broken
/// JSON wrapper, a fenced block, or a plain sentence. A response is only malformed when nothing readable
/// is left, because every rejection costs the player a real reply.
/// </summary>
internal static class DialoguePayload
{
    /// <summary>Field names a provider might use for the spoken line when it ignores the requested schema.</summary>
    private static readonly string[] DialogueFields=["dialogue","message","reply","response","text","content","speech","line"];

    /// <summary>
    /// Fragments of our own instructions and of model reasoning. A provider that echoes the prompt or thinks
    /// out loud has not produced dialogue, and the player must never see either.
    /// </summary>
    public static readonly string[] PromptMarkers=[
        "we need to respond as ","we need to output ","return only valid json","you write believable football dialogue",
        "respond as this character","relationship: score ","relevant save events:","relevant memories:","recent messages:",
        "private player state:","verified/provider facts json:","current career date/season:",
        "initiate one natural private message to ","current journalist question:","verified match facts:",
        "<think>","</think>","system prompt","newmemories","personality is simulated interpretation"
    ];

    public static bool LooksLikePrompt(string text)
        =>text.StartsWith("system:",StringComparison.OrdinalIgnoreCase)||text.StartsWith("user:",StringComparison.OrdinalIgnoreCase)
        ||PromptMarkers.Any(marker=>text.Contains(marker,StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Pulls the assistant text out of a response envelope that may contain reasoning, tool, or thinking
    /// items before the message. Reasoning models put those first, so indexing item zero loses the answer.
    /// </summary>
    public static string ReadFirstText(JsonElement items)
    {
        if(items.ValueKind!=JsonValueKind.Array)return "";
        foreach(var item in items.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Object)continue;
            if(item.TryGetProperty("type",out var type)&&type.ValueKind==JsonValueKind.String&&type.GetString() is {} kind&&kind is not("message" or "text" or "output_text"))continue;
            if(item.TryGetProperty("text",out var direct)&&direct.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(direct.GetString()))return direct.GetString()!;
            if(!item.TryGetProperty("content",out var content))continue;
            if(content.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(content.GetString()))return content.GetString()!;
            var text=ReadFirstText(content);
            if(!string.IsNullOrWhiteSpace(text))return text;
        }
        return "";
    }

    /// <summary>Builds a result from model text, salvaging the dialogue when the JSON wrapper is missing or broken.</summary>
    public static GenerationResult Read(string text,int inputTokens,int outputTokens,string raw,string provider)
    {
        if(string.IsNullOrWhiteSpace(text))throw new LlmUnavailableException($"{provider} returned an empty response.");
        var body=ExtractJson(text);
        try
        {
            using var document=JsonDocument.Parse(body);
            var root=document.RootElement;
            if(root.ValueKind==JsonValueKind.Object&&TryReadDialogue(root,out var dialogue))
                return new(dialogue,Read(root,"mood","neutral"),Number(root,"relationshipDelta"),Number(root,"trustDelta"),Number(root,"respectDelta"),Memories(root),inputTokens,outputTokens,raw);
        }
        catch(JsonException){}
        // The wrapper is unusable, so fall back to the sentence inside it. A truncated
        // {"dialogue":"..." still carries a complete line, and a provider that ignored the
        // schema entirely usually returned perfectly good dialogue as plain text.
        if(TryExtractDialogueField(text,out var salvaged))return new(salvaged,"neutral",0,0,0,[],inputTokens,outputTokens,raw);
        var plain=Unfence(text).Trim();
        if(plain.Length>0&&!plain.StartsWith('{')&&!plain.StartsWith('[')&&!plain.Contains("\"dialogue\"",StringComparison.OrdinalIgnoreCase)&&!LooksLikePrompt(plain))
            return new(plain,"neutral",0,0,0,[],inputTokens,outputTokens,raw);
        throw new LlmUnavailableException($"{provider} returned malformed dialogue.");
    }

    private static bool TryReadDialogue(JsonElement root,out string dialogue)
    {
        foreach(var field in DialogueFields)
            if(root.TryGetProperty(field,out var value)&&value.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(value.GetString()))
            {
                dialogue=value.GetString()!.Trim();
                return true;
            }
        dialogue="";
        return false;
    }

    private static string Read(JsonElement root,string name,string fallback)
        =>root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(value.GetString())?value.GetString()!:fallback;

    /// <summary>Accepts a delta whether the model sent a number, a numeric string, or nothing at all.</summary>
    private static int Number(JsonElement root,string name)
    {
        if(!root.TryGetProperty(name,out var value))return 0;
        var parsed=value.ValueKind switch
        {
            JsonValueKind.Number=>value.TryGetInt32(out var number)?number:(int)Math.Round(value.GetDouble()),
            JsonValueKind.String=>int.TryParse(value.GetString(),out var number)?number:0,
            _=>0
        };
        return Math.Clamp(parsed,-5,5);
    }

    private static IReadOnlyList<string> Memories(JsonElement root)
    {
        if(!root.TryGetProperty("newMemories",out var memories))return [];
        if(memories.ValueKind==JsonValueKind.String)return string.IsNullOrWhiteSpace(memories.GetString())?[]:[memories.GetString()!];
        if(memories.ValueKind!=JsonValueKind.Array)return [];
        return memories.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()??"").Where(x=>x.Length>0).ToList();
    }

    /// <summary>Reads a complete dialogue string out of a JSON wrapper that was cut off before it closed.</summary>
    public static bool TryExtractDialogueField(string text,out string dialogue)
    {
        dialogue="";
        foreach(var field in DialogueFields)
        {
            var token="\""+field+"\"";
            var marker=text.IndexOf(token,StringComparison.OrdinalIgnoreCase);
            if(marker<0)continue;
            var colon=text.IndexOf(':',marker+token.Length);
            if(colon<0)continue;
            var start=colon+1;
            while(start<text.Length&&char.IsWhiteSpace(text[start]))start++;
            if(start>=text.Length||text[start]!='"')continue;
            for(var i=start+1;i<text.Length;i++)
            {
                if(text[i]!='"'||text[i-1]=='\\')continue;
                try
                {
                    var value=JsonSerializer.Deserialize<string>(text[start..(i+1)]);
                    if(string.IsNullOrWhiteSpace(value))break;
                    dialogue=value.Trim();
                    return true;
                }
                catch(JsonException){break;}
            }
        }
        return false;
    }

    /// <summary>Narrows a response to the JSON object inside it, ignoring fences and any commentary around it.</summary>
    public static string ExtractJson(string text)
    {
        var value=Unfence(text).Trim();
        var start=value.IndexOf('{');
        var end=value.LastIndexOf('}');
        return start>=0&&end>start?value[start..(end+1)]:value;
    }

    /// <summary>Removes markdown code fences, including the language tag some providers add.</summary>
    public static string Unfence(string text)
    {
        var value=text.Trim();
        if(!value.Contains("```",StringComparison.Ordinal))return value;
        var open=value.IndexOf("```",StringComparison.Ordinal);
        var body=value[(open+3)..];
        var newline=body.IndexOf('\n');
        if(newline>=0&&body[..newline].Trim().Length<=12)body=body[(newline+1)..];
        var close=body.IndexOf("```",StringComparison.Ordinal);
        return (close>=0?body[..close]:body).Trim();
    }
}
