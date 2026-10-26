namespace PF.BlazorReference.Core.Ports.Data;

/// <summary>Creates a short-lived <see cref="IConsentIdentityDb"/>. Each Handle() call creates its own.</summary>
public interface IConsentIdentityDbFactory
{
    Task<IConsentIdentityDb> CreateAsync(CancellationToken ct = default);
}
