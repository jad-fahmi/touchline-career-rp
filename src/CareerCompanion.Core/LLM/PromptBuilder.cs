using CareerCompanion.Core.Domain;

namespace CareerCompanion.Core.LLM;

public sealed class PromptBuilder
{
    public LlmRequest Character(Character character,Career career,Relationship relationship,SceneType scene,
        IReadOnlyList<Memory> memories,IReadOnlyList<CareerEvent> events,IReadOnlyList<ConversationMessage> history,string userMessage,string model)
    {
        var publicScene=scene is SceneType.PressConference or SceneType.PostMatch;
        var system=$"""You write concise, natural football dialogue for a persistent simulation. Never alter objective save facts or invent an injury, transfer, result, or statistic as fact. Character: {character.Name}; type: {character.Type}; role: {character.SquadRole}. Personality: {character.PersonalityJson}. Communication: {character.CommunicationJson}. Historical notes are context only and must not imply future knowledge: {character.HistoricalNotes}. Scene: {scene}; mode: {(publicScene?"PUBLIC, guarded":"PRIVATE")}. Current career date/season: {career.CurrentDate}, {career.Season}; club: {career.Club}. Respond as this character without announcing the role. Keep it brief and permit neutrality or disagreement. New memories must be simulated interpretations, never historical facts.""";
        var prompt=$"Relationship: score {relationship.Score}, trust {relationship.Trust}, respect {relationship.Respect}, familiarity {relationship.Familiarity}.\nRelevant save events:\n{string.Join('\n',events.Take(5).Select(e=>$"- [{e.Type}] {e.Summary}"))}\nRelevant memories:\n{string.Join('\n',memories.Take(8).Select(m=>$"- {m.Text}"))}\nRecent messages:\n{string.Join('\n',history.TakeLast(8).Select(m=>$"{m.Role}: {m.Content}"))}\nUser: {userMessage}";
        return new(system,prompt,model);
    }
}
