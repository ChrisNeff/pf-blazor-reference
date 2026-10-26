using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Ports.Data;

namespace PF.BlazorReference.Core.Extensions;

/// <summary>Claim handling shared by every <see cref="IClaimable"/> aggregate.</summary>
public static class ClaimableDbExtensions
{
    /// <summary>
    /// Takes the claim on an entity already loaded from this context and saves it. Returns false when
    /// another owner's lease is live, or another host saved first (the version token rejects the
    /// stale write). On false the caller must skip the row.
    /// </summary>
    public static async Task<bool> TryClaimAndSaveAsync<T>(
        this IConsentIdentityDb db, T entity, string owner, DateTime nowUtc, TimeSpan lease,
        CancellationToken ct = default) where T : class, IClaimable
    {
        if (!entity.TryClaim(owner, nowUtc, lease))
            return false;

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (ConcurrencyConflictException)
        {
            return false;
        }
    }
}
