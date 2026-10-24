using ErrorOr;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Features.Invitations;
using PF.BlazorReference.Tests.Support;

namespace PF.BlazorReference.Tests.Core;

/// <summary>Unit tests: the data port and the directory port are substitutes; no database, no Graph.</summary>
public class ActivateAcceptedGuestUseCaseTests
{
    private readonly FakeConsentIdentityDb fake = new();
    private readonly IUserManagementService directory = Substitute.For<IUserManagementService>();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

    private async Task<ActivateAcceptedGuestUseCase.Outcome> Activate(Guid id, string owner = "host-a") =>
        (await new ActivateAcceptedGuestUseCase(fake.Factory, directory, time)
            .Handle(new ActivateAcceptedGuestUseCase.Request(id, owner), CancellationToken.None)).Value.Outcome;

    private DeveloperRegistration SeedInvited()
    {
        var registration = FakeConsentIdentityDb.Registration("Ada", "ada@example.com");
        registration.RequestProvisioning();
        registration.MarkInvited("guest-id");
        fake.Registrations.Add(registration);
        return registration;
    }

    [Fact]
    public async Task A_redeemed_invitation_activates_the_guest()
    {
        var registration = SeedInvited();
        directory.HasAcceptedInvitationAsync("guest-id", Arg.Any<CancellationToken>()).Returns(true);

        Assert.Equal(ActivateAcceptedGuestUseCase.Outcome.Activated, await Activate(registration.Id));

        Assert.Equal(RegistrationStatus.Activated, registration.Status);
        await directory.Received(1).ActivateGuestAsync("guest-id", Arg.Any<CancellationToken>());
        Assert.Null(registration.ClaimedBy);
    }

    [Fact]
    public async Task An_unredeemed_invitation_writes_nothing_not_even_a_claim()
    {
        var registration = SeedInvited();
        directory.HasAcceptedInvitationAsync("guest-id", Arg.Any<CancellationToken>()).Returns(false);

        Assert.Equal(ActivateAcceptedGuestUseCase.Outcome.NotAccepted, await Activate(registration.Id));

        await directory.DidNotReceiveWithAnyArgs().ActivateGuestAsync(default!, default);
        await fake.Db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_acceptance_check_is_retried_next_time_and_records_nothing()
    {
        var registration = SeedInvited();
        directory.HasAcceptedInvitationAsync("guest-id", Arg.Any<CancellationToken>())
            .ThrowsAsync(new UserManagementException("Microsoft Graph could not be reached."));

        Assert.Equal(ActivateAcceptedGuestUseCase.Outcome.NotAccepted, await Activate(registration.Id));

        Assert.Null(registration.ProvisioningError);
        await fake.Db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_activation_is_recorded_and_the_registration_stays_invited()
    {
        var registration = SeedInvited();
        directory.HasAcceptedInvitationAsync("guest-id", Arg.Any<CancellationToken>()).Returns(true);
        directory.ActivateGuestAsync("guest-id", Arg.Any<CancellationToken>()).ThrowsAsync(new UserManagementException("Group not found."));

        Assert.Equal(ActivateAcceptedGuestUseCase.Outcome.ActivationFailed, await Activate(registration.Id));

        Assert.Equal(RegistrationStatus.Invited, registration.Status);
        Assert.Equal("Group not found.", registration.ProvisioningError);
    }

    [Fact]
    public async Task A_row_another_host_is_activating_is_skipped()
    {
        var registration = SeedInvited();
        registration.TryClaim("host-b", time.GetUtcNow().UtcDateTime, ActivateAcceptedGuestUseCase.ClaimLease);
        directory.HasAcceptedInvitationAsync("guest-id", Arg.Any<CancellationToken>()).Returns(true);

        Assert.Equal(ActivateAcceptedGuestUseCase.Outcome.Skipped, await Activate(registration.Id, owner: "host-a"));

        await directory.DidNotReceiveWithAnyArgs().ActivateGuestAsync(default!, default);
    }

    [Fact]
    public async Task A_registration_that_is_not_invited_is_skipped_without_asking_entra()
    {
        var registration = FakeConsentIdentityDb.Registration("Ada", "ada@example.com");
        fake.Registrations.Add(registration);

        Assert.Equal(ActivateAcceptedGuestUseCase.Outcome.Skipped, await Activate(registration.Id));

        Assert.Empty(directory.ReceivedCalls());
    }
}
