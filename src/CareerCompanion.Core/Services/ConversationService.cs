using CareerCompanion.Core.Domain;
using CareerCompanion.Core.LLM;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Simulation;

namespace CareerCompanion.Core.Services;

public sealed class ConversationService(Database db,ILlmProvider llm)
{
    public async Task<GenerationResult> SendAsync(long careerId,long characterId,SceneType scene,string message,string model,CancellationToken ct=default)
    {
        var character=db.GetCharacters(careerId).FirstOrDefault(x=>x.Id==characterId)
            ??throw new InvalidOperationException("That character is no longer part of this career.");var career=db.GetCareer(careerId);var rel=db.GetRelationship(characterId);var careerTime=DateTime.TryParse(career.CurrentDate,out var date)?date:DateTime.UtcNow;
        var memories=new MemoryRanker().Rank(db.GetMemories(characterId),message,careerTime);var events=db.GetEvents(careerId,12);var history=db.GetMessages(careerId,characterId);
        var conversation=db.StartConversation(careerId,characterId,scene,"{}",careerTime);db.AddMessage(conversation,"user",message,careerTime);
        var characterState=db.GetCharacterState(characterId);var playerState=db.GetPlayerState(careerId);GenerationResult result;var usedAi=false;var tokens=(Input:0,Output:0);
        // Offline dialogue and the model are grounded in exactly the same account of the career.
        var latest=db.GetMatches(careerId,1).LastOrDefault();
        var situation=latest is null?null:new MatchNarrativeBuilder(db).Build(career,latest).Brief();
        // A bare "hey" does not need a model. Everything else does, including a greeting that carries a
        // second line, because that is where the player is actually saying something.
        if(!OfflineDialogueLibrary.RequiresAi(message))
        {
            result=OfflineReply(character,career,rel,characterState,playerState,message,scene,"offline-first");
        }
        else
        {
            var outcome=await new DialogueGenerator(llm).GenerateAsync(
                new PromptBuilder().Character(character,career,rel,scene,memories,events,history,message,model,characterState,playerState,situation),character.Name,[message],ct);
            tokens=(outcome.InputTokens,outcome.OutputTokens);
            if(outcome.Succeeded)
            {
                result=outcome.Result!;usedAi=true;
                if(outcome.Attempts>1)db.Log("llm_retry",$"Conversation for character {characterId} needed {outcome.Attempts} attempts before a usable reply.");
            }
            // Offline dialogue is the last resort, not the second option: the model has already been asked
            // again with a correction by this point. The roleplay must never stop because a request failed.
            else
            {
                db.Log("llm_error",$"Conversation fallback for character {characterId} after {outcome.Attempts} attempts: {outcome.Failure}");
                result=OfflineReply(character,career,rel,characterState,playerState,message,scene,outcome.Failure??"the model could not be reached");
            }
        }
        db.AddMessage(conversation,"assistant",result.Text,careerTime);var updated=new RelationshipEngine().Apply(rel,result.RelationshipDelta,result.TrustDelta,result.RespectDelta);db.SaveRelationship(updated);
        foreach(var memory in result.Memories.Take(3))db.AddMemory(careerId,characterId,null,memory,45,result.RelationshipDelta*10,"conversation",false,careerTime);
        foreach(var compressed in new MemoryCompressor().FindCandidates(db.GetMemories(characterId)).Take(1))db.AddMemory(careerId,characterId,null,compressed.Summary,compressed.Importance,compressed.Valence,compressed.Topic,true,careerTime);
        // Retries that were rejected still cost tokens, so usage is recorded whether or not the reply was used.
        if(tokens.Input+tokens.Output>0)db.AddUsage(llm.Name,model,tokens.Input,tokens.Output);
        if(usedAi)db.Log("llm",$"Conversation generated; model={model}; input={tokens.Input}; output={tokens.Output}");
        else db.Log("offline",$"Conversation answered by offline library; character={characterId}; reason={(result.Raw.StartsWith("offline-library:",StringComparison.Ordinal)?result.Raw["offline-library:".Length..]:result.Raw)}");
        return result;
    }
    private static GenerationResult OfflineReply(Character character,Career career,Relationship relationship,CharacterState state,PlayerState player,string message,SceneType scene,string reason){var result=OfflineDialogueLibrary.Direct(character,career,relationship,state,player,message,scene);return result with{Raw=$"offline-library:{reason}"};}
}
