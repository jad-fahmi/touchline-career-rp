using CareerCompanion.Core.Domain;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CareerCompanion.Core.LLM;

public sealed record LlmRequest(string SystemPrompt,string UserPrompt,string Model,int MaxOutputTokens=350,double Creativity=.7);
public interface ILlmProvider { string Name { get; } Task<GenerationResult> GenerateAsync(LlmRequest request,CancellationToken cancellationToken=default); }
public sealed class LlmUnavailableException(string message,Exception? inner=null):Exception(message,inner);
public sealed class LlmRateLimitException(string message):Exception(message);

public sealed class OpenAIProvider(HttpClient client,Func<string?> keyProvider):ILlmProvider
{
    public string Name=>"OpenAI";
    public async Task<GenerationResult> GenerateAsync(LlmRequest request,CancellationToken cancellationToken=default)
    {
        var key=keyProvider(); if(string.IsNullOrWhiteSpace(key)) throw new LlmUnavailableException("No OpenAI API key is configured. Offline features remain available.");
        var schema=new { type="object",properties=new { dialogue=new{type="string"},mood=new{type="string"},relationshipDelta=new{type="integer",minimum=-5,maximum=5},trustDelta=new{type="integer",minimum=-5,maximum=5},respectDelta=new{type="integer",minimum=-5,maximum=5},newMemories=new{type="array",items=new{type="string"}}},required=new[]{"dialogue","mood","relationshipDelta","trustDelta","respectDelta","newMemories"},additionalProperties=false };
        var body=new { model=request.Model,input=new object[]{new{role="system",content=request.SystemPrompt},new{role="user",content=request.UserPrompt}},max_output_tokens=request.MaxOutputTokens,text=new{format=new{type="json_schema",name="career_response",strict=true,schema}} };
        using var message=new HttpRequestMessage(HttpMethod.Post,"https://api.openai.com/v1/responses");message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);message.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        HttpResponseMessage response;try{response=await client.SendAsync(message,cancellationToken);}catch(OperationCanceledException e){throw new LlmUnavailableException("The generation timed out or was cancelled.",e);}catch(HttpRequestException e){throw new LlmUnavailableException("Could not reach OpenAI.",e);}
        var raw=await response.Content.ReadAsStringAsync(cancellationToken);if(response.StatusCode==(HttpStatusCode)429)throw new LlmRateLimitException("OpenAI rate limit reached. Try again later.");if(!response.IsSuccessStatusCode)throw new LlmUnavailableException($"OpenAI returned {(int)response.StatusCode}. Your career data was not changed.");
        try{using var doc=JsonDocument.Parse(raw);var root=doc.RootElement;var outputText=root.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString()!;using var result=JsonDocument.Parse(outputText);var x=result.RootElement;var usage=root.TryGetProperty("usage",out var u)?u:default;return new(x.GetProperty("dialogue").GetString()??"",x.GetProperty("mood").GetString()??"neutral",x.GetProperty("relationshipDelta").GetInt32(),x.GetProperty("trustDelta").GetInt32(),x.GetProperty("respectDelta").GetInt32(),x.GetProperty("newMemories").EnumerateArray().Select(e=>e.GetString()??"").Where(s=>s.Length>0).ToList(),usage.ValueKind==JsonValueKind.Object&&usage.TryGetProperty("input_tokens",out var it)?it.GetInt32():0,usage.ValueKind==JsonValueKind.Object&&usage.TryGetProperty("output_tokens",out var ot)?ot.GetInt32():0,raw);}catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentOutOfRangeException){throw new LlmUnavailableException("OpenAI returned malformed structured output.",e);}
    }
}

public sealed class ClaudeProvider(HttpClient client,Func<string?> keyProvider):ILlmProvider
{
    public string Name=>"Claude";
    public async Task<GenerationResult> GenerateAsync(LlmRequest request,CancellationToken cancellationToken=default)
    {
        var key=keyProvider();if(string.IsNullOrWhiteSpace(key))throw new LlmUnavailableException("No Anthropic API key is configured. Offline features remain available.");
        var instruction="Return only valid JSON with exactly these fields: dialogue (string), mood (string), relationshipDelta (integer from -5 to 5), trustDelta (integer from -5 to 5), respectDelta (integer from -5 to 5), newMemories (array of strings). Do not use markdown fences.";
        var body=new{model=request.Model,max_tokens=request.MaxOutputTokens,temperature=request.Creativity,system=request.SystemPrompt+"\n\n"+instruction,messages=new[]{new{role="user",content=request.UserPrompt}}};
        using var message=new HttpRequestMessage(HttpMethod.Post,"https://api.anthropic.com/v1/messages");message.Headers.Add("x-api-key",key);message.Headers.Add("anthropic-version","2023-06-01");message.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        HttpResponseMessage response;try{response=await client.SendAsync(message,cancellationToken);}catch(OperationCanceledException e){throw new LlmUnavailableException("The generation timed out or was cancelled.",e);}catch(HttpRequestException e){throw new LlmUnavailableException("Could not reach Anthropic.",e);}
        var raw=await response.Content.ReadAsStringAsync(cancellationToken);if(response.StatusCode==(HttpStatusCode)429)throw new LlmRateLimitException("Anthropic rate limit reached. Try again later.");if(!response.IsSuccessStatusCode)throw new LlmUnavailableException($"Anthropic returned {(int)response.StatusCode}. Your career data was not changed.");
        try{using var doc=JsonDocument.Parse(raw);var text=doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString()??"";using var result=JsonDocument.Parse(text);var x=result.RootElement;var usage=doc.RootElement.TryGetProperty("usage",out var u)?u:default;return new(x.GetProperty("dialogue").GetString()??"",x.GetProperty("mood").GetString()??"neutral",x.GetProperty("relationshipDelta").GetInt32(),x.GetProperty("trustDelta").GetInt32(),x.GetProperty("respectDelta").GetInt32(),x.GetProperty("newMemories").EnumerateArray().Select(e=>e.GetString()??"").Where(s=>s.Length>0).ToList(),usage.ValueKind==JsonValueKind.Object&&usage.TryGetProperty("input_tokens",out var it)?it.GetInt32():0,usage.ValueKind==JsonValueKind.Object&&usage.TryGetProperty("output_tokens",out var ot)?ot.GetInt32():0,raw);}catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentOutOfRangeException){throw new LlmUnavailableException("Anthropic returned malformed structured output.",e);}
    }
}

public sealed class OllamaProvider(HttpClient client,string model):ILlmProvider
{
    public string Name=>"Ollama";
    public async Task<GenerationResult> GenerateAsync(LlmRequest request,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(model))throw new LlmUnavailableException("No Ollama model is configured.");
        var body=new{model,stream=false,format="json",options=new{temperature=request.Creativity},messages=new[]{new{role="system",content=request.SystemPrompt+"\n\nReturn only valid JSON with exactly these fields: dialogue (string), mood (string), relationshipDelta (integer from -5 to 5), trustDelta (integer from -5 to 5), respectDelta (integer from -5 to 5), newMemories (array of strings). Do not use markdown fences."},new{role="user",content=request.UserPrompt}}};
        HttpResponseMessage response;try{response=await client.PostAsync("http://localhost:11434/api/chat",new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json"),cancellationToken);}catch(OperationCanceledException e){throw new LlmUnavailableException("Ollama generation timed out or was cancelled.",e);}catch(HttpRequestException e){throw new LlmUnavailableException("Ollama is not running. Start Ollama and try again.",e);}
        var raw=await response.Content.ReadAsStringAsync(cancellationToken);if(!response.IsSuccessStatusCode)throw new LlmUnavailableException($"Ollama returned {(int)response.StatusCode}. Check that model '{model}' is installed.");
        try{using var doc=JsonDocument.Parse(raw);var text=doc.RootElement.GetProperty("message").GetProperty("content").GetString()??"";using var result=JsonDocument.Parse(text);var x=result.RootElement;var eval=doc.RootElement.TryGetProperty("eval_count",out var e)?e.GetInt32():0;return new(x.GetProperty("dialogue").GetString()??"",x.GetProperty("mood").GetString()??"neutral",x.GetProperty("relationshipDelta").GetInt32(),x.GetProperty("trustDelta").GetInt32(),x.GetProperty("respectDelta").GetInt32(),x.GetProperty("newMemories").EnumerateArray().Select(v=>v.GetString()??"").Where(v=>v.Length>0).ToList(),0,eval,raw);}catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentOutOfRangeException){throw new LlmUnavailableException("Ollama returned malformed structured output.",e);}
    }
}

public sealed class OpenAICompatibleProvider(HttpClient client,string endpoint,Func<string?> keyProvider):ILlmProvider
{
    public string Name=>"OpenAI-compatible";
    public async Task<GenerationResult> GenerateAsync(LlmRequest request,CancellationToken cancellationToken=default)
    {
        var url=NormalizeEndpoint(endpoint);if(string.IsNullOrWhiteSpace(url))throw new LlmUnavailableException("No compatible API endpoint is configured.");
        var key=keyProvider();var instruction="Return only valid JSON with exactly these fields: dialogue (string), mood (string), relationshipDelta (integer from -5 to 5), trustDelta (integer from -5 to 5), respectDelta (integer from -5 to 5), newMemories (array of strings). Do not use markdown fences.";
        var model=request.Model.StartsWith("compatible:",StringComparison.OrdinalIgnoreCase)?request.Model["compatible:".Length..].Trim():request.Model;var body=new{model,messages=new[]{new{role="system",content=request.SystemPrompt+"\n\n"+instruction},new{role="user",content=request.UserPrompt}},max_tokens=request.MaxOutputTokens,temperature=request.Creativity};
        using var message=new HttpRequestMessage(HttpMethod.Post,url);if(!string.IsNullOrWhiteSpace(key))message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);message.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        HttpResponseMessage response;try{response=await client.SendAsync(message,cancellationToken);}catch(OperationCanceledException e){throw new LlmUnavailableException("Compatible API generation timed out or was cancelled.",e);}catch(HttpRequestException e){throw new LlmUnavailableException("Could not reach the compatible API endpoint.",e);}
        var raw=await response.Content.ReadAsStringAsync(cancellationToken);if(response.StatusCode==(HttpStatusCode)429)throw new LlmRateLimitException("Compatible API rate limit reached. Try again later.");if(!response.IsSuccessStatusCode)throw new LlmUnavailableException($"Compatible API returned {(int)response.StatusCode}. Check the endpoint, key, and model.");
        try
        {
            using var doc=JsonDocument.Parse(raw);var root=doc.RootElement;
            if(root.TryGetProperty("error",out var apiError))throw new LlmUnavailableException($"Compatible API error: {ReadError(apiError)}");
            if(!root.TryGetProperty("choices",out var choices)||choices.ValueKind!=JsonValueKind.Array||choices.GetArrayLength()==0)throw new LlmUnavailableException("Compatible API returned no choices.");
            var choice=choices[0];var text=ReadContent(choice);
            if(string.IsNullOrWhiteSpace(text))throw new LlmUnavailableException($"Compatible API returned empty content{(choice.TryGetProperty("finish_reason",out var finish)?$" (finish reason: {finish.GetString()})":"") }.");
            var usage=root.TryGetProperty("usage",out var u)?u:default;var inputTokens=usage.ValueKind==JsonValueKind.Object&&usage.TryGetProperty("prompt_tokens",out var pt)?pt.GetInt32():0;var outputTokens=usage.ValueKind==JsonValueKind.Object&&usage.TryGetProperty("completion_tokens",out var ct)?ct.GetInt32():0;
            try
            {
                using var result=JsonDocument.Parse(ExtractJson(text));var x=result.RootElement;return new(x.GetProperty("dialogue").GetString()??"",x.TryGetProperty("mood",out var mood)?mood.GetString()??"neutral":"neutral",x.TryGetProperty("relationshipDelta",out var relationship)?relationship.GetInt32():0,x.TryGetProperty("trustDelta",out var trust)?trust.GetInt32():0,x.TryGetProperty("respectDelta",out var respect)?respect.GetInt32():0,x.TryGetProperty("newMemories",out var memories)&&memories.ValueKind==JsonValueKind.Array?memories.EnumerateArray().Select(v=>v.GetString()??"").Where(v=>v.Length>0).ToList():[],inputTokens,outputTokens,raw);
            }
            catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                // Many OpenAI-compatible/free endpoints ignore the JSON
                // instruction and return a perfectly usable sentence. Keep
                // that sentence instead of needlessly falling back, but never
                // expose an echoed prompt or a JSON/code-fence fragment.
                var plain=text.Trim();
                if(TryExtractDialogue(plain,out var extractedDialogue))return new(extractedDialogue,"neutral",0,0,0,[],inputTokens,outputTokens,raw);
                if(!LooksLikeJsonOrPrompt(plain))return new(plain,"neutral",0,0,0,[],inputTokens,outputTokens,raw);
                throw new LlmUnavailableException("Compatible API returned malformed dialogue JSON.",e);
            }
        }
        catch(LlmUnavailableException){throw;}
        catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentOutOfRangeException){throw new LlmUnavailableException("Compatible API returned an unreadable response envelope.",e);}
    }
    private static string ReadContent(JsonElement choice){if(choice.TryGetProperty("message",out var message)&&message.TryGetProperty("content",out var content)){if(content.ValueKind==JsonValueKind.String)return content.GetString()??"";if(content.ValueKind==JsonValueKind.Array)return string.Join("",content.EnumerateArray().Where(x=>x.TryGetProperty("text",out _)&&(!x.TryGetProperty("type",out var type)||type.ValueKind!=JsonValueKind.String||type.GetString() is "text" or "output_text")).Select(x=>x.GetProperty("text").GetString()??""));}return choice.TryGetProperty("text",out var text)&&text.ValueKind==JsonValueKind.String?text.GetString()??"":"";}
    private static string ReadError(JsonElement error){if(error.ValueKind==JsonValueKind.String)return error.GetString()??"unknown error";return error.TryGetProperty("message",out var message)?message.GetString()??"unknown error":error.ToString();}
    private static bool LooksLikeJsonOrPrompt(string text)
    {
        if(text.Contains('{')||text.Contains('}')||text.Contains('[')||text.Contains(']')||text.Contains("```")||text.Contains("\"dialogue\"",StringComparison.OrdinalIgnoreCase))return true;
        return new[]{"system:","user:","return only valid json","we need to respond as ","we need to output ","you write believable football dialogue","respond as this character","relationship: score ","relevant save events:","relevant memories:","recent messages:","private player state:","verified/provider facts json:","current career date/season:","initiate one natural private message to ","current journalist question:","verified match facts:","<think>","</think>","system prompt","newmemories"}.Any(marker=>text.Contains(marker,StringComparison.OrdinalIgnoreCase));
    }
    private static bool TryExtractDialogue(string text,out string dialogue)
    {
        dialogue="";var marker=text.IndexOf("\"dialogue\"",StringComparison.OrdinalIgnoreCase);if(marker<0)return false;var colon=text.IndexOf(':',marker+"\"dialogue\"".Length);if(colon<0)return false;var start=colon+1;while(start<text.Length&&char.IsWhiteSpace(text[start]))start++;if(start>=text.Length||text[start]!='"')return false;
        for(var i=start+1;i<text.Length;i++)
        {
            if(text[i]!='"'||text[i-1]=='\\')continue;
            try{var value=JsonSerializer.Deserialize<string>(text[start..(i+1)]);if(!string.IsNullOrWhiteSpace(value)){dialogue=value.Trim();return true;}}catch(JsonException){return false;}
        }
        return false;
    }
    private static string NormalizeEndpoint(string endpoint){var value=endpoint.Trim().TrimEnd('/');if(value.EndsWith("/chat/completions",StringComparison.OrdinalIgnoreCase))return value;if(value.EndsWith("/v1",StringComparison.OrdinalIgnoreCase))return value+"/chat/completions";return value+"/v1/chat/completions";}
    private static string ExtractJson(string text){var value=text.Trim();if(value.StartsWith("```")&&value.EndsWith("```")){var first=value.IndexOf('\n');value=first>=0?value[(first+1)..^3].Trim():value.Trim('`');}var start=value.IndexOf('{');var end=value.LastIndexOf('}');return start>=0&&end>start?value[start..(end+1)]:value;}
}

public sealed class OfflineLlmProvider:ILlmProvider
{
    public string Name=>"Offline";
    public Task<GenerationResult> GenerateAsync(LlmRequest request,CancellationToken cancellationToken=default)
        => throw new LlmUnavailableException("AI generation is offline. Add an API key in Settings; match logging and simulation still work.");
}
