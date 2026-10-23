using PF.BlazorReference.Core.Features.Provisioning;
using PF.BlazorReference.Tests.Support;

namespace PF.BlazorReference.Tests.Core;

/// <summary>Unit tests: the data port and its factory are substitutes, so no database is involved.</summary>
public class ListProvisioningBacklogUseCaseTests
{
    private readonly FakeConsentIdentityDb fake = new();

    [Fact]
    public async Task Lists_the_registrations_with_a_step_left_requested_and_invited()
    {
        var pending = FakeConsentIdentityDb.Registration("Pending", "pending@example.com");
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
        fake.Registrations.AddRange([pending, requested, invited, activated, failed]);

        var result = await new ListProvisioningBacklogUseCase(fake.Factory)
            .Handle(new ListProvisioningBacklogUseCase.Request(), CancellationToken.None);

        Assert.Equal(new[] { requested.Id, invited.Id }.Order(), result.Value.RegistrationIds.Order());
    }
}
