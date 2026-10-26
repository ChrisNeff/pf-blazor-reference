using ErrorOr;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Features.Invitations;
using PF.BlazorReference.Tests.Support;
using PF.BlazorReference.Core.Ports.Data;
using PF.BlazorReference.Core.Ports.Directory;

namespace PF.BlazorReference.Tests.Core;

/// <summary>Unit tests: the data port and the directory port are substitutes; no database, no Graph.</summary>
public class SendInvitationUseCaseTests
{
    private readonly FakeConsentIdentityDb fake = new();
    private readonly IUserManagementService directory = Substitute.For<IUserManagementService>();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

    private Task<ErrorOr<SendInvitationUseCase.Response>> Send(Guid id, string owner = "host-a") =>
        new SendInvitationUseCase(fake.Factory, directory, time)
            .Handle(new SendInvitationUseCase.Request(id, owner), CancellationToken.None);

    private DeveloperRegistration SeedRequested()
    {
        var registration = FakeConsentIdentityDb.Registration("Ada", "ada@example.com");
        registration.RequestProvisioning();
        fake.Registrations.Add(registration);
        return registration;
    }

    [Fact]
    public async Task Claims_the_row_before_calling_entra_then_records_the_guest()
    {
        var registration = SeedRequested();
        directory.InviteGuestAsync(registration, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            // The claim is saved before Entra is called, so another host would see it.
            Assert.Equal("host-a", registration.ClaimedBy);
            fake.Db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
            return "guest-id";
        });

        var result = await Send(registration.Id);

        Assert.Equal(new SendInvitationUseCase.Response(SendInvitationUseCase.Outcome.Invited), result.Value);
        Assert.Equal("guest-id", registration.EntraObjectId);
        Assert.Null(registration.ClaimedBy);
        await fake.Db.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_invitation_is_recorded_on_the_row()
    {
        var registration = SeedRequested();
        directory.InviteGuestAsync(registration, Arg.Any<CancellationToken>())
            .ThrowsAsync(new UserManagementException("Microsoft Graph could not be reached."));

        var result = await Send(registration.Id);

        Assert.Equal(SendInvitationUseCase.Outcome.Failed, result.Value.Outcome);
        Assert.Equal(RegistrationStatus.ProvisioningFailed, registration.Status);
        Assert.Equal("Microsoft Graph could not be reached.", registration.ProvisioningError);
        Assert.Null(registration.ClaimedBy);
    }

    [Fact]
    public async Task A_row_claimed_by_another_host_is_skipped_without_calling_entra()
    {
        var registration = SeedRequested();
        registration.TryClaim("host-b", time.GetUtcNow().UtcDateTime, SendInvitationUseCase.ClaimLease);

        var result = await Send(registration.Id, owner: "host-a");

        Assert.Equal(SendInvitationUseCase.Outcome.Skipped, result.Value.Outcome);
        Assert.Empty(directory.ReceivedCalls());
        Assert.Equal("host-b", registration.ClaimedBy);
    }

    [Fact]
    public async Task Losing_the_claim_race_skips_without_calling_entra()
    {
        var registration = SeedRequested();
        fake.Db.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException(new InvalidOperationException("another host saved first")));

        var result = await Send(registration.Id);

        Assert.Equal(SendInvitationUseCase.Outcome.Skipped, result.Value.Outcome);
        Assert.Empty(directory.ReceivedCalls());
    }

    [Fact]
    public async Task An_unexpected_failure_propagates_and_leaves_the_request_for_a_sweep()
    {
        var registration = SeedRequested();
        directory.InviteGuestAsync(registration, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException());

        await Assert.ThrowsAsync<InvalidOperationException>(() => Send(registration.Id));

        // Still requested; the claim lapses after the lease and the next sweep retries it.
        Assert.Equal(RegistrationStatus.ProvisioningRequested, registration.Status);
    }

    [Theory]
    [InlineData(RegistrationStatus.Submitted)]
    [InlineData(RegistrationStatus.Invited)]
    public async Task A_registration_not_requested_is_left_alone(RegistrationStatus status)
    {
        var registration = FakeConsentIdentityDb.Registration("Ada", "ada@example.com");
        if (status == RegistrationStatus.Invited)
        {
            registration.RequestProvisioning();
            registration.MarkInvited("guest-id");
        }
        fake.Registrations.Add(registration);

        var result = await Send(registration.Id);

        Assert.Equal(SendInvitationUseCase.Outcome.Skipped, result.Value.Outcome);
        Assert.Empty(directory.ReceivedCalls());
        await fake.Db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
