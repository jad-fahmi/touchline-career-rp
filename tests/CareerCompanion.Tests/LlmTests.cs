using CareerCompanion.Core.LLM;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CareerCompanion.Tests;

public sealed class LlmTests
{
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>send(request,cancellationToken);}
    private static LlmRequest Request=>new("system","user","test-model");
    [Fact] public async Task Parses_successful_structured_response(){var data=JsonSerializer.Serialize(new{dialogue="Well played.",mood="pleased",relationshipDelta=2,trustDelta=1,respectDelta=1,newMemories=new[]{"A good exchange"}});var response=JsonSerializer.Serialize(new{output=new[]{new{content=new[]{new{text=data}}}},usage=new{input_tokens=20,output_tokens=12}});var provider=new OpenAIProvider(new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(response,Encoding.UTF8,"application/json")}))),()=>"fake");var result=await provider.GenerateAsync(Request);Assert.Equal("Well played.",result.Text);Assert.Equal(32,result.InputTokens+result.OutputTokens);}
    [Fact] public async Task Reports_rate_limit(){var provider=new OpenAIProvider(new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)429){Content=new StringContent("{}") }))),()=>"fake");await Assert.ThrowsAsync<LlmRateLimitException>(()=>provider.GenerateAsync(Request));}
    [Fact] public async Task Rejects_malformed_output(){var provider=new OpenAIProvider(new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"output\":[]}")}))),()=>"fake");await Assert.ThrowsAsync<LlmUnavailableException>(()=>provider.GenerateAsync(Request));}
    [Fact] public async Task Reports_timeout(){var provider=new OpenAIProvider(new HttpClient(new Handler((_,ct)=>Task.FromCanceled<HttpResponseMessage>(ct))),()=>"fake");using var cts=new CancellationTokenSource();cts.Cancel();await Assert.ThrowsAsync<LlmUnavailableException>(()=>provider.GenerateAsync(Request,cts.Token));}
    [Fact] public async Task Missing_key_is_non_destructive_error(){var provider=new OpenAIProvider(new HttpClient(),()=>null);await Assert.ThrowsAsync<LlmUnavailableException>(()=>provider.GenerateAsync(Request));}
    [Fact] public async Task Parses_openai_compatible_chat_response(){var data=JsonSerializer.Serialize(new{dialogue="Keep your head up.",mood="supportive",relationshipDelta=1,trustDelta=1,respectDelta=0,newMemories=new[]{"A supportive exchange"}});var response=JsonSerializer.Serialize(new{choices=new[]{new{message=new{content=data}}},usage=new{prompt_tokens=3,completion_tokens=4}});var provider=new OpenAICompatibleProvider(new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(response,Encoding.UTF8,"application/json")}))),"http://localhost:1234/v1",()=>"test");var result=await provider.GenerateAsync(Request with{Model="compatible:test-model"});Assert.Equal("Keep your head up.",result.Text);Assert.Equal(7,result.InputTokens+result.OutputTokens);}
}
