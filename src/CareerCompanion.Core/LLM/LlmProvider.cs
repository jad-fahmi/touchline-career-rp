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

public sealed class OfflineLlmProvider:ILlmProvider
{
    public string Name=>"Offline";
    public Task<GenerationResult> GenerateAsync(LlmRequest request,CancellationToken cancellationToken=default)
        => throw new LlmUnavailableException("AI generation is offline. Add an API key in Settings; match logging and simulation still work.");
}
