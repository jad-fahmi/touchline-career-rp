using CareerCompanion.Core.Domain;

namespace CareerCompanion.Core.Simulation;

public sealed class MemoryRanker
{
    public IReadOnlyList<Memory> Rank(IEnumerable<Memory> memories, string topic, DateTime now, int limit = 8)
    {
        var terms = topic.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return memories.Select(m => new
        {
            Memory = m,
            Score = m.Importance * 1.6 + Math.Abs(m.Valence) * 0.5
                + Math.Max(0, 30 - (now - m.Timestamp).TotalDays * .35)
                + (terms.Any(t => m.Topic.Contains(t, StringComparison.OrdinalIgnoreCase)
                    || m.Text.Contains(t, StringComparison.OrdinalIgnoreCase)) ? 35 : 0)
                + (m.Resolved ? -8 : 8) + (m.IsCompressed ? 5 : 0)
        }).OrderByDescending(x => x.Score).ThenByDescending(x => x.Memory.Timestamp)
          .Take(Math.Clamp(limit, 1, 20)).Select(x => x.Memory).ToList();
    }
}
