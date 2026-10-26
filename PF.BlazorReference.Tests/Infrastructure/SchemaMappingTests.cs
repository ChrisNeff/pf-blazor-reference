using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Tests.Support;
using PF.BlazorReference.Core.Ports.Data;

namespace PF.BlazorReference.Tests.Infrastructure;

/// <summary>Proves the hand-written EF mappings and the audit interceptor against the real schema.sql.</summary>
public sealed class SchemaMappingTests : IAsyncDisposable
{
    private readonly SqliteTestDatabase database = new();

    public ValueTask DisposeAsync() => database.DisposeAsync();

    private static DeveloperRegistration NewRegistration(string email) =>
        DeveloperRegistration.Create("Ada", email).Value;

    [Fact]
    public async Task An_entity_round_trips_through_the_schema()
    {
        var original = NewRegistration("ada@example.com");
        await using (var db = database.NewContext())
        {
            db.DeveloperRegistrations.Add(original);
            await db.SaveChangesAsync();
        }

        await using var read = database.NewContext();
        var loaded = await read.DeveloperRegistrations.SingleAsync();

        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.Email, loaded.Email);
        Assert.Equal(RegistrationStatus.Submitted, loaded.Status);
    }

    [Fact]
    public async Task The_unique_email_index_is_translated_to_the_core_exception()
    {
        await using (var first = database.NewContext())
        {
            first.DeveloperRegistrations.Add(NewRegistration("ada@example.com"));
            await first.SaveChangesAsync();
        }

        await using var second = database.NewContext();
        second.DeveloperRegistrations.Add(NewRegistration("ada@example.com"));

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task An_insert_stamps_both_audit_timestamps_with_the_current_utc_time()
    {
        await using var db = database.NewContext();
        var registration = NewRegistration("ada@example.com");
        db.DeveloperRegistrations.Add(registration);

        await db.SaveChangesAsync();

        Assert.Equal(database.Time.GetUtcNow().UtcDateTime, registration.CreatedAtUtc);
        Assert.Equal(registration.CreatedAtUtc, registration.LastUpdatedUtc);
        Assert.Equal(DateTimeKind.Utc, registration.CreatedAtUtc.Kind);
    }

    [Fact]
    public async Task An_update_stamps_only_the_last_updated_timestamp()
    {
        await using var db = database.NewContext();
        var registration = NewRegistration("ada@example.com");
        db.DeveloperRegistrations.Add(registration);
        await db.SaveChangesAsync();
        var created = registration.CreatedAtUtc;

        database.Time.Advance(TimeSpan.FromHours(1));
        db.Entry(registration).Property(r => r.Name).CurrentValue = "Ada Byron";
        await db.SaveChangesAsync();

        Assert.Equal(created, registration.CreatedAtUtc);
        Assert.Equal(created.AddHours(1), registration.LastUpdatedUtc);
    }

    [Fact]
    public async Task The_schema_refuses_the_invalid_status()
    {
        await using var db = database.NewContext();

        // Raw SQL: the entity has no way to hold Invalid, which is the point of the sentinel.
        var insert = db.Database.ExecuteSqlRawAsync(
            "INSERT INTO DeveloperRegistrations (Id, Name, Email, Status, CreatedAtUtc, LastUpdatedUtc) " +
            "VALUES ('00000000-0000-0000-0000-000000000001', 'Ada', 'ada@example.com', 'Invalid', '2026-10-21 12:00:00', '2026-10-21 12:00:00')");

        await Assert.ThrowsAsync<SqliteException>(() => insert);
    }

    [Fact]
    public async Task Provisioning_state_round_trips_through_the_schema()
    {
        var original = NewRegistration("ada@example.com");
        original.RequestProvisioning();
        original.MarkInvited("00000000-0000-0000-0000-0000000000aa");
        original.MarkActivated();
        await using (var db = database.NewContext())
        {
            db.DeveloperRegistrations.Add(original);
            await db.SaveChangesAsync();
        }

        await using var read = database.NewContext();
        var loaded = await read.DeveloperRegistrations.SingleAsync();

        Assert.Equal(RegistrationStatus.Activated, loaded.Status);
        Assert.Equal(original.EntraObjectId, loaded.EntraObjectId);
        Assert.Equal(2, loaded.ProvisioningAttempts);
        Assert.Null(loaded.ProvisioningError);
    }

    public static TheoryData<RegistrationStatus> ValidStatuses() =>
        new(Enum.GetValues<RegistrationStatus>().Where(s => s != RegistrationStatus.Invalid));

    [Theory]
    [MemberData(nameof(ValidStatuses))]
    public async Task The_schema_accepts_every_valid_status(RegistrationStatus status)
    {
        // Fails when a status is added to the enum but not to the CHECK in schema.sql.
        await using var db = database.NewContext();
        var statusText = status.ToString();
        var objectId = status is RegistrationStatus.Invited or RegistrationStatus.Activated ? "object-id" : null;

        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO DeveloperRegistrations (Id, Name, Email, Status, CreatedAtUtc, LastUpdatedUtc, EntraObjectId)
            VALUES ('00000000-0000-0000-0000-000000000001', 'Ada', 'ada@example.com', {statusText}, '2026-10-21 12:00:00', '2026-10-21 12:00:00', {objectId})
            """);

        Assert.Equal(status, (await db.DeveloperRegistrations.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("Invited")]
    [InlineData("Activated")]
    public async Task The_schema_refuses_an_invited_or_activated_row_without_a_guest_object_id(string status)
    {
        await using var db = database.NewContext();

        var insert = db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO DeveloperRegistrations (Id, Name, Email, Status, CreatedAtUtc, LastUpdatedUtc)
            VALUES ('00000000-0000-0000-0000-000000000001', 'Ada', 'ada@example.com', {status}, '2026-10-21 12:00:00', '2026-10-21 12:00:00')
            """);

        await Assert.ThrowsAsync<SqliteException>(() => insert);
    }

    [Fact]
    public async Task Every_update_increments_the_version_and_an_insert_starts_at_zero()
    {
        await using var db = database.NewContext();
        var registration = NewRegistration("ada@example.com");
        db.DeveloperRegistrations.Add(registration);
        await db.SaveChangesAsync();
        Assert.Equal(0, registration.Version);

        registration.RequestProvisioning();
        await db.SaveChangesAsync();
        registration.MarkInvited("guest-id");
        await db.SaveChangesAsync();

        Assert.Equal(2, registration.Version);
    }

    [Fact]
    public async Task The_schema_refuses_a_claim_without_its_expiry()
    {
        await using var db = database.NewContext();

        var insert = db.Database.ExecuteSqlRawAsync(
            "INSERT INTO DeveloperRegistrations (Id, Name, Email, Status, CreatedAtUtc, LastUpdatedUtc, ClaimedBy) " +
            "VALUES ('00000000-0000-0000-0000-000000000001', 'Ada', 'ada@example.com', 'ProvisioningRequested', '2026-10-21 12:00:00', '2026-10-21 12:00:00', 'host-a')");

        await Assert.ThrowsAsync<SqliteException>(() => insert);
    }
}
