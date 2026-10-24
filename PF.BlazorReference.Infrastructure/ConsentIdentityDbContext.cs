using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Infrastructure;

/// <summary>
/// Implements the data port. The EF model is hand-mapped onto schema.sql by the
/// <c>IEntityTypeConfiguration&lt;T&gt;</c> classes; EF never creates or migrates the schema.
/// </summary>
public sealed class ConsentIdentityDbContext(DbContextOptions<ConsentIdentityDbContext> options)
    : DbContext(options), IConsentIdentityDb
{
    // SQLITE_CONSTRAINT_UNIQUE. SQL Server would use error numbers 2601 and 2627 here.
    private const int SqliteConstraintUnique = 2067;

    public DbSet<DeveloperRegistration> DeveloperRegistrations => Set<DeveloperRegistration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConsentIdentityDbContext).Assembly);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // The row's Version is not the one this context read: someone else saved it first.
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteConstraintUnique })
        {
            // Translate the provider failure so Core never inspects a provider exception.
            throw new UniqueConstraintViolationException(ex);
        }
    }
}
