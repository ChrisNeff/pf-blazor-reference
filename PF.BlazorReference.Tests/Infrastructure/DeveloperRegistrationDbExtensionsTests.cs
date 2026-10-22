using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Extensions;
using PF.BlazorReference.Tests.Support;

namespace PF.BlazorReference.Tests.Infrastructure;

/// <summary>The reused queries, run against the real schema.</summary>
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
}
