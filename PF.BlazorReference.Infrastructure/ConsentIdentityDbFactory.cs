using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Ports.Data;

namespace PF.BlazorReference.Infrastructure;

/// <summary>Wraps <see cref="IDbContextFactory{TContext}"/> so each Handle() call gets its own short-lived context.</summary>
public sealed class ConsentIdentityDbFactory(IDbContextFactory<ConsentIdentityDbContext> contextFactory)
    : IConsentIdentityDbFactory
{
    public async Task<IConsentIdentityDb> CreateAsync(CancellationToken ct = default) =>
        await contextFactory.CreateDbContextAsync(ct);
}
