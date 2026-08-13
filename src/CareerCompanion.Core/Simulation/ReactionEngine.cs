using CareerCompanion.Core.Domain;

namespace CareerCompanion.Core.Simulation;

public sealed record ReactionTarget(long? CharacterId, string Channel, int Priority, string Reason);

public sealed class ReactionEngine
{
    public IReadOnlyList<ReactionTarget> Select(CareerEvent evt, IEnumerable<Character> characters,
        IReadOnlyDictionary<long, Relationship> relationships)
    {
        if (evt.Importance < 25) return [];
        var targets = new List<ReactionTarget>();
        foreach (var c in characters)
        {
            relationships.TryGetValue(c.Id, out var rel);
            var relevance = evt.Importance + (rel?.Familiarity ?? 0) / 4 + (rel?.Score ?? 0) / 5;
            relevance += c.Type switch { CharacterType.Manager => 18, CharacterType.Teammate => 7, CharacterType.Journalist => 4, _ => 0 };
            if (c.Type == CharacterType.Manager && evt.Importance >= 45 || relevance >= 72)
                targets.Add(new(c.Id, c.Type == CharacterType.Manager ? "manager" : "message", relevance, "event and relationship relevance"));
        }
        if (evt.Importance >= 48) targets.Add(new(null, "news", evt.Importance, "news threshold"));
        if (evt.Importance >= 55) targets.Add(new(null, "social", evt.Importance, "social threshold"));
        if (evt.Importance >= 62) targets.Add(new(null, "press", evt.Importance, "press threshold"));
        return targets.OrderByDescending(x => x.Priority).Take(8).ToList();
    }
}
