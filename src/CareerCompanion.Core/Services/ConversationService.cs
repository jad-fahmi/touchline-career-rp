using CareerCompanion.Core.Domain;
using CareerCompanion.Core.LLM;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Simulation;

namespace CareerCompanion.Core.Services;

public sealed class ConversationService(Database db,ILlmProvider llm)
{
    public async Task<GenerationResult> SendAsync(long careerId,long characterId,SceneType scene,string message,string model,CancellationToken ct=default)
    {
        var character=db.GetCharacters(careerId).Single(x=>x.Id==characterId);var career=db.GetCareer(careerId);var rel=db.GetRelationship(characterId);
        var memories=new MemoryRanker().Rank(db.GetMemories(characterId),message,DateTime.UtcNow);var events=db.GetEvents(careerId,12);var history=db.GetMessages(careerId,characterId);
        var conversation=db.StartConversation(careerId,characterId,scene);db.AddMessage(conversation,"user",message);
        GenerationResult result;try{result=await llm.GenerateAsync(new PromptBuilder().Character(character,career,rel,scene,memories,events,history,message,model),ct);}catch{db.Log("llm_error",$"Conversation generation failed for character {characterId}");throw;}
        db.AddMessage(conversation,"assistant",result.Text);var updated=new RelationshipEngine().Apply(rel,result.RelationshipDelta,result.TrustDelta,result.RespectDelta);db.SaveRelationship(updated);
        foreach(var memory in result.Memories.Take(3))db.AddMemory(careerId,characterId,null,memory,45,result.RelationshipDelta*10,"conversation");
        foreach(var compressed in new MemoryCompressor().FindCandidates(db.GetMemories(characterId)).Take(1))db.AddMemory(careerId,characterId,null,compressed.Summary,compressed.Importance,compressed.Valence,compressed.Topic,true);
        db.AddUsage(llm.Name,model,result.InputTokens,result.OutputTokens);db.Log("llm",$"Conversation generated; model={model}; input={result.InputTokens}; output={result.OutputTokens}");return result;
    }
}
