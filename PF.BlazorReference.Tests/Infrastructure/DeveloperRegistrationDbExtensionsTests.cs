using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Extensions;
using PF.BlazorReference.Tests.Support;

namespace PF.BlazorReference.Tests.Infrastructure;

/// <summary>The reused queries and the row claim, run against the real schema, so the concurrency token is real.</summary>
public sealed class DeveloperRegistrationDbExtensionsTests : IAsyncDisposable
{
    private readonly SqliteTestDatabase database = new();

    public ValueTask DisposeAsync() => database.DisposeAsync();

    private async Task Add(string name, string email)
    {
        await using var db = database.NewContext();
        db.DeveloperRegistrations.Add(DeveloperRegistration.Create(name, email).Value);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task IsEmailRegisteredAsync_finds_a_stored_email_and_only_that_one()
    {
        await Add("Ada", "ada@example.com");

        await using var db = database.NewContext();
        Assert.True(await db.IsEmailRegisteredAsync("ada@example.com"));
        Assert.False(await db.IsEmailRegisteredAsync("grace@example.com"));
    }

    [Fact]
    public async Task RegistrationsNewestFirst_orders_by_creation_time_and_stays_composable()
    {
        await Add("First", "first@example.com");
        database.Time.Advance(TimeSpan.FromMinutes(5));
        await Add("Second", "second@example.com");

        await using var db = database.NewContext();
        var names = await db.RegistrationsNewestFirst().Select(r => r.Name).ToListAsync();
        var newest = await db.RegistrationsNewestFirst().Take(1).Select(r => r.Name).SingleAsync();

        Assert.Equal(["Second", "First"], names);
        Assert.Equal("Second", newest);
    }

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private async Task<Guid> AddRequested()
    {
        await using var db = database.NewContext();
        var registration = DeveloperRegistration.Create("Ada", "ada@example.com").Value;
        registration.RequestProvisioning();
        db.DeveloperRegistrations.Add(registration);
        await db.SaveChangesAsync();
        return registration.Id;
    }

    private DateTime Now => database.Time.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task ClaimAsync_saves_the_claim_and_bumps_the_version()
    {
        var id = await AddRequested();

        await using (var db = database.NewContext())
            Assert.NotNull(await db.ClaimAsync(id, RegistrationStatus.ProvisioningRequested, "host-a", Now, Lease));

        await using var read = database.NewContext();
        var stored = await read.DeveloperRegistrations.SingleAsync();
        Assert.Equal("host-a", stored.ClaimedBy);
        Assert.Equal(Now + Lease, stored.ClaimedUntilUtc);
        Assert.Equal(DateTimeKind.Utc, stored.ClaimedUntilUtc!.Value.Kind);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task ClaimAsync_refuses_a_row_another_host_holds_until_the_lease_lapses()
    {
        var id = await AddRequested();
        await using (var a = database.NewContext())
            await a.ClaimAsync(id, RegistrationStatus.ProvisioningRequested, "host-a", Now, Lease);

        await using (var b = database.NewContext())
            Assert.Null(await b.ClaimAsync(id, RegistrationStatus.ProvisioningRequested, "host-b", Now, Lease));

        database.Time.Advance(Lease);
        await using (var b = database.NewContext())
            Assert.NotNull(await b.ClaimAsync(id, RegistrationStatus.ProvisioningRequested, "host-b", Now, Lease));
    }

    [Fact]
    public async Task ClaimAsync_refuses_a_row_in_another_status()
    {
        var id = await AddRequested();

        await using var db = database.NewContext();
        Assert.Null(await db.ClaimAsync(id, RegistrationStatus.Invited, "host-a", Now, Lease));
    }

    [Fact]
    public async Task Two_hosts_that_read_the_same_version_cannot_both_claim_the_row()
    {
        // Both hosts read the row before either saves: the claim check passes for both in memory,
        // and the version token is what stops the second.
        var id = await AddRequested();
        await using var hostA = database.NewContext();
        await using var hostB = database.NewContext();
        var seenByA = await hostA.DeveloperRegistrations.SingleAsync(r => r.Id == id);
        var seenByB = await hostB.DeveloperRegistrations.SingleAsync(r => r.Id == id);

        Assert.True(seenByA.TryClaim("host-a", Now, Lease));
        Assert.True(seenByB.TryClaim("host-b", Now, Lease));
        await hostA.SaveChangesAsync();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => hostB.SaveChangesAsync());

        await using var read = database.NewContext();
        Assert.Equal("host-a", (await read.DeveloperRegistrations.SingleAsync()).ClaimedBy);
    }
}
