using CareerCompanion.Core.Domain;

namespace CareerCompanion.Core.Providers;

public interface ICareerDataProvider
{
    string Name { get; }
    Task<CareerSnapshot> GetSnapshotAsync(long careerId, CancellationToken cancellationToken = default);
}

public sealed class ManualCareerDataProvider(Func<long, Task<CareerSnapshot>> loader) : ICareerDataProvider
{
    public string Name => "Manual";
    public Task<CareerSnapshot> GetSnapshotAsync(long careerId, CancellationToken cancellationToken = default)
        => loader(careerId);
}

// Future FIFA/Cheat Engine providers only need to implement ICareerDataProvider and return CareerSnapshot.
