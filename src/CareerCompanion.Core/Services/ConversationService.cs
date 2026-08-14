using CareerCompanion.Core.Domain;
using CareerCompanion.Core.LLM;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Simulation;

namespace CareerCompanion.Core.Services;

public sealed class ConversationService(Database db,ILlmProvider llm)
{
    public async Task<GenerationResult> SendAsync(long careerId,long characterId,SceneType scene,string message,string model,CancellationToken ct=default)
    {
        var character=db.GetCharacters(careerId).Single(x=>x.Id==characterId);var career=db.GetCareer(careerId);var rel=db.GetRelationship(characterId);var careerTime=DateTime.TryParse(career.CurrentDate,out var date)?date:DateTime.UtcNow;
        var memories=new MemoryRanker().Rank(db.GetMemories(characterId),message,careerTime);var events=db.GetEvents(careerId,12);var history=db.GetMessages(careerId,characterId);
        var conversation=db.StartConversation(careerId,characterId,scene,"{}",careerTime);db.AddMessage(conversation,"user",message,careerTime);
        var characterState=db.GetCharacterState(characterId);var playerState=db.GetPlayerState(careerId);GenerationResult result;try{result=await llm.GenerateAsync(new PromptBuilder().Character(character,career,rel,scene,memories,events,history,message,model,characterState,playerState),ct);}catch(LlmUnavailableException e){db.Log("llm_error",$"Conversation fallback for character {characterId}: {e.Message}");result=OfflineReply(character,career,rel,characterState,playerState,message,scene);}catch(LlmRateLimitException e){db.Log("llm_error",$"Conversation fallback for character {characterId}: {e.Message}");result=OfflineReply(character,career,rel,characterState,playerState,message,scene);}
        db.AddMessage(conversation,"assistant",result.Text,careerTime);var updated=new RelationshipEngine().Apply(rel,result.RelationshipDelta,result.TrustDelta,result.RespectDelta);db.SaveRelationship(updated);
        foreach(var memory in result.Memories.Take(3))db.AddMemory(careerId,characterId,null,memory,45,result.RelationshipDelta*10,"conversation",false,careerTime);
        foreach(var compressed in new MemoryCompressor().FindCandidates(db.GetMemories(characterId)).Take(1))db.AddMemory(careerId,characterId,null,compressed.Summary,compressed.Importance,compressed.Valence,compressed.Topic,true,careerTime);
        db.AddUsage(llm.Name,model,result.InputTokens,result.OutputTokens);db.Log("llm",$"Conversation generated; model={model}; input={result.InputTokens}; output={result.OutputTokens}");return result;
    }
    private static GenerationResult OfflineReply(Character character,Career career,Relationship relationship,CharacterState state,PlayerState player,string message,SceneType scene)=>OfflineDialogueLibrary.Direct(character,career,relationship,state,player,message,scene);
}
