using CareerCompanion.Core.Domain;

namespace CareerCompanion.Core.Simulation;

public sealed record MemoryCompression(string Topic,string Summary,int Importance,int Valence,IReadOnlyList<long> SourceIds);
public sealed class MemoryCompressor
{
    public IReadOnlyList<MemoryCompression> FindCandidates(IEnumerable<Memory> memories,int minimumGroup=4)
        => memories.Where(m=>!m.IsCompressed&&m.Importance<70).GroupBy(m=>m.Topic,StringComparer.OrdinalIgnoreCase)
            .Where(g=>g.Count()>=minimumGroup).Select(g=>new MemoryCompression(g.Key,
                $"A repeated pattern developed around {g.Key}: {g.Count()} related moments shaped the character's view.",
                Math.Clamp((int)g.Average(x=>x.Importance)+10,20,75),(int)g.Average(x=>x.Valence),g.Select(x=>x.Id).ToList())).ToList();
}
