using CareerCompanion.Core.Domain;

namespace CareerCompanion.Core.LLM;

public sealed class PromptBuilder
{
    public LlmRequest Character(Character character,Career career,Relationship relationship,SceneType scene,
        IReadOnlyList<Memory> memories,IReadOnlyList<CareerEvent> events,IReadOnlyList<ConversationMessage> history,string userMessage,string model,CharacterState? state=null,PlayerState? playerState=null)
    {
        var publicScene=scene is SceneType.PressConference;
        state??=new(character.Id);
        var privatePlayerContext=publicScene?"The player's private emotional state is not public knowledge.":$"Private player state: mood={playerState?.Mood??"steady"}; confidence={playerState?.Confidence??55}; pressure={playerState?.Pressure??25}; fatigue={playerState?.Fatigue??15}; isolation={playerState?.Isolation??10}; latest emotional trigger={playerState?.LastTrigger??"unknown"}. Respond with appropriate care, restraint, or directness based on your relationship. Do not diagnose a medical condition.";
        var system=$"""You write concise, natural football dialogue for a persistent simulation. Never alter objective save facts or invent an injury, transfer, result, selection decision, or statistic as fact. Player: {career.PlayerName}. Character: {character.Name}; age: {(character.Age>0?character.Age.ToString():"unknown")}; nationality: {character.Nationality}; type: {character.Type}; club: {character.Club}; position: {character.Position}; role: {character.SquadRole}. Verified/provider facts JSON: {character.FactsJson}. Personality is simulated interpretation: {character.PersonalityJson}. Communication: {character.CommunicationJson}. Current state: mood={state.Mood}; satisfaction={state.Satisfaction}; concerns={state.Concerns}; ambitions={state.Ambitions}. {privatePlayerContext} Historical notes are context only and must not imply future knowledge: {character.HistoricalNotes}. Scene: {scene}; mode: {(publicScene?"PUBLIC, guarded":"PRIVATE")}. Current career date/season: {career.CurrentDate}, {career.Season}; player club: {career.Club}. Respond as this character without announcing the role. Keep it brief and permit neutrality, silence, or disagreement. New memories must be simulated interpretations, never historical facts.""";
        var prompt=$"Relationship: score {relationship.Score}, trust {relationship.Trust}, respect {relationship.Respect}, familiarity {relationship.Familiarity}.\nRelevant save events:\n{string.Join('\n',events.Take(5).Select(e=>$"- [{e.Type}] {e.Summary}"))}\nRelevant memories:\n{string.Join('\n',memories.Take(8).Select(m=>$"- {m.Text}"))}\nRecent messages:\n{string.Join('\n',history.TakeLast(8).Select(m=>$"{m.Role}: {m.Content}"))}\nUser: {userMessage}";
        return new(system,prompt,model);
    }
}
