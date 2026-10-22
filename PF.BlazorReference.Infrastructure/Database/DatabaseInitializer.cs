using System.Data.Common;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace PF.BlazorReference.Infrastructure.Database;

/// <summary>
/// Applies the embedded schema.sql (and optionally seed.sql) with plain SQL, not through EF.
/// Both scripts are idempotent. Tests call <see cref="ApplyAsync"/> with the same schema the app uses,
/// so a mismatch between schema.sql and the hand-written EF mappings fails a test.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(string connectionString, bool seed, CancellationToken ct = default)
    {
        EnsureDirectory(connectionString);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await ApplyAsync(connection, seed, ct);
    }

    public static async Task ApplyAsync(DbConnection connection, bool seed, CancellationToken ct = default)
    {
        await ExecuteScriptAsync(connection, "schema.sql", ct);
        if (seed)
            await ExecuteScriptAsync(connection, "seed.sql", ct);
    }

    private static async Task ExecuteScriptAsync(DbConnection connection, string resourceName, CancellationToken ct)
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);

        await using var command = connection.CreateCommand();
        command.CommandText = await reader.ReadToEndAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void EnsureDirectory(string connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }
}
