using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Infrastructure.Interceptors;

/// <summary>
/// Increments <see cref="IVersioned.Version"/> on every update. The mapping marks it a concurrency token,
/// so EF writes the row only where the stored version is still the one read, and a save based on a stale
/// read fails. Writes through the change tracker, which works on the private setter.
/// SQL Server would not need this: a rowversion column changes by itself.
/// </summary>
public sealed class VersionInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Increment(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Increment(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Increment(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries<IVersioned>().Where(e => e.State == EntityState.Modified))
        {
            var version = entry.Property(nameof(IVersioned.Version));
            version.CurrentValue = (int)version.OriginalValue! + 1;
        }
    }
}
