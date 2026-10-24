namespace PF.BlazorReference.Core.Entities;

/// <summary>
/// Entities with an optimistic concurrency token. The version interceptor in Infrastructure increments
/// <see cref="Version"/> on every update, and EF Core only writes the row if the version it read is still
/// the stored one; otherwise the data port throws <see cref="ConcurrencyConflictException"/>. This is
/// what lets several app hosts work the same table. On SQL Server the column would be a rowversion.
/// </summary>
public interface IVersioned
{
    int Version { get; }
}
