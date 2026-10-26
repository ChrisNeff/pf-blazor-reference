using PF.BlazorReference.Core.Features.Invitations;
using PF.BlazorReference.Tests.Support;

namespace PF.BlazorReference.Tests.Core;

/// <summary>Unit tests: the data port and its factory are substitutes, so no database is involved.</summary>
public class ListInvitedRegistrationsUseCaseTests
{
    private readonly FakeConsentIdentityDb fake = new();

    [Fact]
    public async Task Lists_only_the_invited_registrations()
    {
        var submitted = FakeConsentIdentityDb.Registration("Submitted", "submitted@example.com");
        var requested = FakeConsentIdentityDb.Registration("Requested", "requested@example.com");
        requested.RequestProvisioning();
        var invited = FakeConsentIdentityDb.Registration("Invited", "invited@example.com");
        invited.RequestProvisioning();
        invited.MarkInvited("guest-1");
        fake.Registrations.AddRange([submitted, requested, invited]);

        var result = await new ListInvitedRegistrationsUseCase(fake.Factory)
            .Handle(new ListInvitedRegistrationsUseCase.Request(), CancellationToken.None);

        Assert.Equal([invited.Id], result.Value.RegistrationIds);
    }
}
