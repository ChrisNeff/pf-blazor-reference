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

        Assert.Equal(new ListRegistrationsUseCase.Item(registration.Id, "Ada", "ada@example.com", ListRegistrationsUseCase.Status.Submitted, created,
            CanRequestInvitation: true, ProvisioningError: null), item);
    }

    [Fact]
    public async Task Returns_an_empty_list_when_nothing_is_registered()
    {
        var result = await List();

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task Maps_each_domain_status_to_the_contract_with_the_domains_answer_on_inviting()
    {
        var submitted = FakeConsentIdentityDb.Registration("Submitted", "submitted@example.com");
        var requested = FakeConsentIdentityDb.Registration("Requested", "requested@example.com");
        requested.RequestProvisioning();
        var invited = FakeConsentIdentityDb.Registration("Invited", "invited@example.com");
        invited.RequestProvisioning();
        invited.MarkInvited("guest-1");
        var activated = FakeConsentIdentityDb.Registration("Activated", "activated@example.com");
        activated.RequestProvisioning();
        activated.MarkInvited("guest-2");
        activated.MarkActivated();
        var failed = FakeConsentIdentityDb.Registration("Failed", "failed@example.com");
        failed.RequestProvisioning();
        failed.MarkProvisioningFailed("Graph was down.");
        fake.Registrations.AddRange([submitted, requested, invited, activated, failed]);

        var items = (await List()).Value.Items.ToDictionary(i => i.Name);

        Assert.Equal((ListRegistrationsUseCase.Status.Submitted, true), (items["Submitted"].Status, items["Submitted"].CanRequestInvitation));
        Assert.Equal((ListRegistrationsUseCase.Status.InvitationRequested, false), (items["Requested"].Status, items["Requested"].CanRequestInvitation));
        Assert.Equal((ListRegistrationsUseCase.Status.Invited, false), (items["Invited"].Status, items["Invited"].CanRequestInvitation));
        Assert.Equal((ListRegistrationsUseCase.Status.Activated, false), (items["Activated"].Status, items["Activated"].CanRequestInvitation));
        Assert.Equal((ListRegistrationsUseCase.Status.InvitationFailed, true), (items["Failed"].Status, items["Failed"].CanRequestInvitation));
        Assert.Equal("Graph was down.", items["Failed"].ProvisioningError);
    }
}
