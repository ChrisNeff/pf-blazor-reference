using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using PF.BlazorReference.Infrastructure;
using PF.BlazorReference.Infrastructure.Database;
using PF.BlazorReference.Infrastructure.Interceptors;

namespace PF.BlazorReference.Tests.Support;

/// <summary>
/// A real SQLite database in memory, built from the same embedded schema.sql the app uses, so a
/// mismatch between schema.sql and the hand-written EF mappings fails a test. For tests of EF itself
/// (mappings, the unique index, the interceptor, query extensions), not for use-case unit tests, which
/// use <see cref="FakeConsentIdentityDb"/>. One per test class instance; the connection stays open for
/// its lifetime because an in-memory database dies with it.
/// </summary>
public sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<ConsentIdentityDbContext> options;

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));

    public SqliteTestDatabase()
    {
        connection.Open();
        DatabaseInitializer.ApplyAsync(connection, seed: false).GetAwaiter().GetResult();

        options = new DbContextOptionsBuilder<ConsentIdentityDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditTimestampInterceptor(Time), new VersionInterceptor())
            .Options;
    }

    public ConsentIdentityDbContext NewContext() => new(options);

    public async ValueTask DisposeAsync() => await connection.DisposeAsync();
}
