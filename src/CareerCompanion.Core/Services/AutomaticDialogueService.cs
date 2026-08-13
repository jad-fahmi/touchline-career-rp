using CareerCompanion.Core.Domain;
using CareerCompanion.Core.LLM;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Simulation;
using System.Text.Json;

namespace CareerCompanion.Core.Services;

public sealed record AutomaticDialogueResult(int Completed,int DeferredOrFailed);

public sealed class AutomaticDialogueService(Database db,ILlmProvider llm)
{
    public async Task<AutomaticDialogueResult> ProcessPendingAsync(long careerId,string defaultModel,string premiumModel,
        bool premiumRouting,int limit=4,CancellationToken ct=default)
    {
        var completed=0;var failed=0;
        foreach(var job in db.GetPendingGenerationJobs(careerId,"automatic_reaction_llm",limit))
        {
            try
            {
                using var payload=JsonDocument.Parse(job.PayloadJson);var root=payload.RootElement;
                var conversationId=root.GetProperty("conversationId").GetInt64();var characterId=root.GetProperty("characterId").GetInt64();
                var eventId=root.GetProperty("eventId").GetInt64();var notificationKey=root.GetProperty("notificationDedupeKey").GetString()??throw new JsonException("Missing notification key.");
                var career=db.GetCareer(careerId);var character=db.GetCharacters(careerId).Single(x=>x.Id==characterId);var evt=db.GetEvent(careerId,eventId);
                var scene=root.TryGetProperty("scene",out var sceneValue)&&Enum.TryParse<SceneType>(sceneValue.GetString(),out var parsedScene)?parsedScene:character.Type==CharacterType.Manager?SceneType.ManagerOffice:SceneType.PostMatch;var relationship=db.GetRelationship(characterId);
                var memories=new MemoryRanker().Rank(db.GetMemories(characterId),evt.Summary,evt.Timestamp);var history=db.GetMessages(careerId,characterId);
                var instruction=$"Initiate one natural private message to {career.PlayerName} in direct reaction to this career event: {evt.Summary} Treat FIFA facts as objective and emotional state as a private simulation signal. Stay in character, avoid diagnosing a medical condition, and do not announce that you are a simulation.";
                var model=premiumRouting&&evt.Importance>=75?premiumModel:defaultModel;
                var request=new PromptBuilder().Character(character,career,relationship,scene,memories,[evt],history,instruction,model,db.GetCharacterState(characterId),db.GetPlayerState(careerId)) with{MaxOutputTokens=180,Creativity=.75};
                var result=await llm.GenerateAsync(request,ct);var text=result.Text.Trim();if(string.IsNullOrWhiteSpace(text))throw new LlmUnavailableException("The automatic reaction was empty.");
                db.ReplaceAutomaticReactionText(careerId,conversationId,characterId,eventId,notificationKey,text);db.AddUsage(llm.Name,model,result.InputTokens,result.OutputTokens);db.CompleteGenerationJob(job.Id);completed++;
            }
            catch(OperationCanceledException){throw;}
            catch(Exception e) when(e is LlmUnavailableException or LlmRateLimitException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                db.FailGenerationJob(job.Id,e.Message,8);db.Log("automatic_generation",$"Job {job.Id} deferred or failed: {e.Message}");failed++;
            }
        }
        return new(completed,failed);
    }
}
