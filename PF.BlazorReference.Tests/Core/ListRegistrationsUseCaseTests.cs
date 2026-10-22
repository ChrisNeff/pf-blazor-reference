using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Features.Registration;
using PF.BlazorReference.Tests.Support;

namespace PF.BlazorReference.Tests.Core;

/// <summary>Unit tests: the data port and its factory are substitutes, so no database is involved.</summary>
public class ListRegistrationsUseCaseTests
{
    private readonly FakeConsentIdentityDb fake = new();

    private Task<ErrorOr.ErrorOr<ListRegistrationsUseCase.Response>> List() =>
        new ListRegistrationsUseCase(fake.Factory).Handle(new ListRegistrationsUseCase.Request(), CancellationToken.None);

    [Fact]
    public async Task Lists_registrations_newest_first()
    {
        fake.Registrations.Add(FakeConsentIdentityDb.Registration("First", "first@example.com", new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc)));
        fake.Registrations.Add(FakeConsentIdentityDb.Registration("Third", "third@example.com", new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc)));
        fake.Registrations.Add(FakeConsentIdentityDb.Registration("Second", "second@example.com", new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc)));

        var result = await List();

        Assert.False(result.IsError);
        Assert.Equal(["Third", "Second", "First"], result.Value.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task Maps_each_registration_to_an_item()
    {
        var created = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        var registration = FakeConsentIdentityDb.Registration("Ada", "ada@example.com", created);
        fake.Registrations.Add(registration);

        var item = Assert.Single((await List()).Value.Items);

        Assert.Equal(new ListRegistrationsUseCase.Item(registration.Id, "Ada", "ada@example.com", RegistrationStatus.Pending, created), item);
    }

    [Fact]
    public async Task Returns_an_empty_list_when_nothing_is_registered()
    {
        var result = await List();

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Items);
    }
}
