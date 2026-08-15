using CareerCompanion.Core.Domain;
using CareerCompanion.Core.LLM;

namespace CareerCompanion.Core.Services;

/// <summary>What the model produced, how many attempts it took, and why it failed if it did.</summary>
public sealed record DialogueOutcome(GenerationResult? Result,int Attempts,int InputTokens,int OutputTokens,string? Failure)
{
    public bool Succeeded=>Result is not null;
}

/// <summary>
/// Runs a generation until it produces usable dialogue. A first response that arrives wrapped in JSON, cut
/// short, or written from outside the football world is a formatting problem, not a reason to abandon the
/// model: the request is sent again with a correction aimed at exactly what went wrong. Offline dialogue is
/// only reached when the model is unreachable or every attempt failed the same way.
/// </summary>
public sealed class DialogueGenerator(ILlmProvider provider,int maxAttempts=3)
{
    public string ProviderName=>provider.Name;

    /// <summary>
    /// <paramref name="spokenAlready"/> holds lines the model was given rather than asked to write, such as
    /// the question a journalist just asked and the answer they just heard. A reply that hands those back is
    /// treated like any other formatting failure and corrected.
    /// </summary>
    public async Task<DialogueOutcome> GenerateAsync(LlmRequest request,string? speakerName,IReadOnlyList<string>? spokenAlready=null,CancellationToken ct=default)
    {
        var input=0;var output=0;string? failure=null;var correction=DialogueResponseGuard.Rejection.None;
        for(var attempt=1;attempt<=Math.Max(1,maxAttempts);attempt++)
        {
            try
            {
                var result=await provider.GenerateAsync(Correct(request,attempt,correction),ct);
                input+=result.InputTokens;output+=result.OutputTokens;
                if(DialogueResponseGuard.TryPrepare(result.Text,speakerName,spokenAlready,out var dialogue,out var rejection))
                    return new(result with{Text=dialogue},attempt,input,output,null);
                correction=rejection;
                failure=Describe(rejection);
            }
            // A rate limit or a missing key will not fix itself on the next request, so those stop here.
            catch(LlmRateLimitException e){return new(null,attempt,input,output,e.Message);}
            catch(Exception e)
            {
                failure=e is LlmUnavailableException?e.Message:"the model could not be reached";
                if(IsConfiguration(e))return new(null,attempt,input,output,failure);
                correction=e.Message.Contains("malformed",StringComparison.OrdinalIgnoreCase)?DialogueResponseGuard.Rejection.PromptEcho:correction;
            }
        }
        return new(null,Math.Max(1,maxAttempts),input,output,failure);
    }

    /// <summary>Adds a correction to the retry, phrased around the specific thing the last answer got wrong.</summary>
    private static LlmRequest Correct(LlmRequest request,int attempt,DialogueResponseGuard.Rejection rejection)
    {
        if(attempt==1)return request;
        var note=rejection switch
        {
            DialogueResponseGuard.Rejection.FourthWall=>"CORRECTION: your last answer referred to FIFA, a save, data, or software. Those do not exist in this world. Team selection is the manager's decision, fitness is the medical staff's, and transfers are the club's. If something is not settled yet, say so in ordinary football words.",
            DialogueResponseGuard.Rejection.PromptEcho=>"CORRECTION: your last answer leaked instructions, reasoning, or an unfinished wrapper. Write only what the character says out loud, in one JSON object, with no thinking, no labels, and no text outside the JSON.",
            DialogueResponseGuard.Rejection.Parroted=>"CORRECTION: your last answer handed back the question you were given and the words you were answered with. Do not repeat either. Write a new line in your own words that reacts to what was said, and ask something you have not asked yet.",
            _=>"CORRECTION: your last answer could not be read. Return one JSON object with a complete dialogue string and nothing before or after it."
        };
        var force=attempt>=3?" Keep the dialogue under sixty words and finish every sentence." : "";
        // A reply cut off mid-sentence, or a reasoning model that spent its budget before answering, needs
        // more room rather than another identical request.
        return request with{SystemPrompt=request.SystemPrompt+"\n\n"+note+force,
            Creativity=Math.Max(.35,request.Creativity-.15*(attempt-1)),
            MaxOutputTokens=Math.Min(1200,request.MaxOutputTokens+300*(attempt-1))};
    }

    private static string Describe(DialogueResponseGuard.Rejection rejection)=>rejection switch
    {
        DialogueResponseGuard.Rejection.FourthWall=>"the model kept referring to the game instead of the football world",
        DialogueResponseGuard.Rejection.PromptEcho=>"the model kept leaking prompt or reasoning text",
        DialogueResponseGuard.Rejection.Parroted=>"the model kept repeating the question and the player's own answer instead of replying",
        _=>"the model returned nothing usable"
    };

    /// <summary>A missing key, model, or endpoint is a settings problem; repeating the request cannot help.</summary>
    private static bool IsConfiguration(Exception e)
        =>e is LlmUnavailableException&&(e.Message.Contains("API key",StringComparison.OrdinalIgnoreCase)
            ||e.Message.Contains("Ollama model",StringComparison.OrdinalIgnoreCase)
            ||e.Message.Contains("endpoint is configured",StringComparison.OrdinalIgnoreCase)
            ||e.Message.Contains("AI generation is offline",StringComparison.OrdinalIgnoreCase));
}
