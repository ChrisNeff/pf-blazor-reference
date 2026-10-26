using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Core.Ports.Data;

/// <summary>
/// Data port, implemented by the DbContext in Infrastructure. Core references the EF Core package
/// (for <see cref="DbSet{TEntity}"/>) but no database provider and never Infrastructure.
/// A query reused by more than one use case becomes an extension method on this port; see Extensions/.
/// </summary>
public interface IConsentIdentityDb : IAsyncDisposable
{
    DbSet<DeveloperRegistration> DeveloperRegistrations { get; }

    /// <exception cref="UniqueConstraintViolationException">A unique index rejected the write.</exception>
    /// <exception cref="ConcurrencyConflictException">A versioned row was changed since it was read.</exception>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
