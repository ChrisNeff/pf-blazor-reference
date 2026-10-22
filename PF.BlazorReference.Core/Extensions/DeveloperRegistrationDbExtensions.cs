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
}
