namespace PF.BlazorReference.Core;

/// <summary>Creates a short-lived <see cref="IConsentIdentityDb"/>. Each Handle() call creates its own.</summary>
public interface IConsentIdentityDbFactory
{
    Task<IConsentIdentityDb> CreateAsync(CancellationToken ct = default);
}
