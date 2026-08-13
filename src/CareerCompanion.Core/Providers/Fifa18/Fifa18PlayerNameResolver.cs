using System.Reflection;

namespace CareerCompanion.Core.Providers.Fifa18;

public sealed record Fifa18PlayerIdentity(string Name, string Nationality);

public sealed class Fifa18PlayerNameResolver
{
    private readonly Lazy<IReadOnlyDictionary<int, Fifa18PlayerIdentity>> _players = new(Load);

    public Fifa18PlayerIdentity? Find(int playerId)
        => _players.Value.TryGetValue(playerId, out var player) ? player : null;

    private static IReadOnlyDictionary<int, Fifa18PlayerIdentity> Load()
    {
        var assembly = typeof(Fifa18PlayerNameResolver).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(x => x.EndsWith("fifa18-player-names.tsv", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("The embedded FIFA 18 player-name index is missing.");
        using var reader = new StreamReader(stream);
        var players = new Dictionary<int, Fifa18PlayerIdentity>();
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split('\t');
            if (fields.Length >= 2 && int.TryParse(fields[0], out var id) && !string.IsNullOrWhiteSpace(fields[1]))
                players[id] = new(fields[1], fields.Length > 2 ? fields[2] : "");
        }
        return players;
    }
}
