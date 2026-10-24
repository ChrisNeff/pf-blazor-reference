using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Features.Invitations;
using PF.BlazorReference.Tests.Support;

namespace PF.BlazorReference.Tests.Core;

/// <summary>Unit tests: the data port and its factory are substitutes, so no database is involved.</summary>
public class ListRegistrationIdsUseCaseTests
{
    private readonly FakeConsentIdentityDb fake = new();

    [Theory]
    [InlineData(RegistrationStatus.ProvisioningRequested)]
    [InlineData(RegistrationStatus.Invited)]
    public async Task Lists_only_the_registrations_in_the_requested_status(RegistrationStatus status)
    {
        var submitted = FakeConsentIdentityDb.Registration("Submitted", "submitted@example.com");
        var requested = FakeConsentIdentityDb.Registration("Requested", "requested@example.com");
        requested.RequestProvisioning();
        var invited = FakeConsentIdentityDb.Registration("Invited", "invited@example.com");
        invited.RequestProvisioning();
        invited.MarkInvited("guest-1");
        fake.Registrations.AddRange([submitted, requested, invited]);

        var result = await new ListRegistrationIdsUseCase(fake.Factory)
            .Handle(new ListRegistrationIdsUseCase.Request(status), CancellationToken.None);

        var expected = status == RegistrationStatus.Invited ? invited : requested;
        Assert.Equal([expected.Id], result.Value.RegistrationIds);
    }
}
