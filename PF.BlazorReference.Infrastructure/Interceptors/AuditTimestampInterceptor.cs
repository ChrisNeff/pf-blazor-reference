using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Infrastructure.Interceptors;

/// <summary>
/// Stamps <see cref="IAuditable"/> entities on save: both timestamps on insert, only the last-updated
/// timestamp on update. It writes through the change tracker, which works on private setters.
/// There is no "created by" stamp because the reference has no authenticated user.
/// </summary>
public sealed class AuditTimestampInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
            return;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    Set(entry, nameof(IAuditable.CreatedAtUtc), now);
                    Set(entry, nameof(IAuditable.LastUpdatedUtc), now);
                    break;
                case EntityState.Modified:
                    Set(entry, nameof(IAuditable.LastUpdatedUtc), now);
                    break;
            }
        }
    }

    private static void Set(EntityEntry entry, string property, DateTime value) =>
        entry.Property(property).CurrentValue = value;
}
