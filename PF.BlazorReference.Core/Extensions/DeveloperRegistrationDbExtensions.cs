using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Core.Extensions;

/// <summary>
/// Queries on <see cref="DeveloperRegistration"/> that more than one use case needs. They extend the
/// data port, so use cases keep seeing only the port. A query used by a single use case stays inline
/// in that use case; promote it here when a second one needs it. One class per aggregate.
/// </summary>
public static class DeveloperRegistrationDbExtensions
{
    /// <param name="normalizedEmail">The stored form, from <see cref="DeveloperRegistration.NormalizeEmail"/>.</param>
    public static Task<bool> IsEmailRegisteredAsync(
        this IConsentIdentityDb db, string normalizedEmail, CancellationToken ct = default) =>
        db.DeveloperRegistrations.AnyAsync(r => r.Email == normalizedEmail, ct);

    /// <summary>Registrations, newest first. Composable: callers add paging, projection or tracking choices.</summary>
    public static IQueryable<DeveloperRegistration> RegistrationsNewestFirst(this IConsentIdentityDb db) =>
        db.DeveloperRegistrations.OrderByDescending(r => r.CreatedAtUtc);

    /// <summary>
    /// Claims a registration in the expected status for one worker, and saves the claim, before the
    /// worker calls Entra. Returns the tracked registration, or null when it is gone, in another status,
    /// claimed by another owner whose lease is live, or claimed by another host between this read and
    /// this save (the version token rejects the stale write). Used by every worker step that writes to Entra.
    /// </summary>
    public static async Task<DeveloperRegistration?> ClaimAsync(
        this IConsentIdentityDb db, Guid registrationId, RegistrationStatus expected,
        string owner, DateTime nowUtc, TimeSpan lease, CancellationToken ct = default)
    {
        var registration = await db.DeveloperRegistrations.FirstOrDefaultAsync(r => r.Id == registrationId, ct);
        if (registration is null || registration.Status != expected || !registration.TryClaim(owner, nowUtc, lease))
            return null;

        try
        {
            await db.SaveChangesAsync(ct);
            return registration;
        }
        catch (ConcurrencyConflictException)
        {
            return null;
        }
    }
}
