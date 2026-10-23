using ErrorOr;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Features.Provisioning;
using PF.BlazorReference.Tests.Support;

namespace PF.BlazorReference.Tests.Core;

/// <summary>Unit tests: the data port and the directory port are substitutes; no database, no Graph.</summary>
public class ProvisionDeveloperUseCaseTests
{
    private readonly FakeConsentIdentityDb fake = new();
    private readonly IUserManagementService directory = Substitute.For<IUserManagementService>();

    private Task<ErrorOr<ProvisionDeveloperUseCase.Response>> Provision(Guid id) =>
        new ProvisionDeveloperUseCase(fake.Factory, directory)
            .Handle(new ProvisionDeveloperUseCase.Request(id), CancellationToken.None);

    private DeveloperRegistration Seed(RegistrationStatus status)
    {
        var registration = FakeConsentIdentityDb.Registration("Ada", "ada@example.com");
        if (status != RegistrationStatus.Pending)
            registration.RequestProvisioning();
        if (status is RegistrationStatus.Invited or RegistrationStatus.Activated)
            registration.MarkInvited("guest-id");
        if (status == RegistrationStatus.Activated)
            registration.MarkActivated();
        fake.Registrations.Add(registration);
        return registration;
    }

    [Fact]
    public async Task A_requested_registration_is_invited_and_the_guest_id_recorded()
    {
        var registration = Seed(RegistrationStatus.ProvisioningRequested);
        directory.InviteGuestAsync(registration, Arg.Any<CancellationToken>()).Returns("guest-id");

        var result = await Provision(registration.Id);

        Assert.Equal(new ProvisionDeveloperUseCase.Response(RegistrationStatus.Invited, Changed: true), result.Value);
        Assert.Equal("guest-id", registration.EntraObjectId);
        await directory.DidNotReceiveWithAnyArgs().ActivateGuestAsync(default!, default);
        await fake.Db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_invitation_is_recorded_on_the_row()
    {
        var registration = Seed(RegistrationStatus.ProvisioningRequested);
        directory.InviteGuestAsync(registration, Arg.Any<CancellationToken>())
            .ThrowsAsync(new UserManagementException("Microsoft Graph could not be reached."));

        var result = await Provision(registration.Id);

        Assert.Equal(RegistrationStatus.ProvisioningFailed, result.Value.Status);
        Assert.Equal("Microsoft Graph could not be reached.", registration.ProvisioningError);
        await fake.Db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unexpected_failure_propagates_and_leaves_the_request_in_place()
    {
        var registration = Seed(RegistrationStatus.ProvisioningRequested);
        directory.InviteGuestAsync(registration, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException());

        await Assert.ThrowsAsync<InvalidOperationException>(() => Provision(registration.Id));

        Assert.Equal(RegistrationStatus.ProvisioningRequested, registration.Status);
        await fake.Db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_redeemed_invitation_activates_the_guest()
    {
        var registration = Seed(RegistrationStatus.Invited);
        directory.HasAcceptedInvitationAsync("guest-id", Arg.Any<CancellationToken>()).Returns(true);

        var result = await Provision(registration.Id);

        Assert.Equal(new ProvisionDeveloperUseCase.Response(RegistrationStatus.Activated, Changed: true), result.Value);
        await directory.Received(1).ActivateGuestAsync("guest-id", Arg.Any<CancellationToken>());
        await fake.Db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unredeemed_invitation_changes_nothing()
    {
        var registration = Seed(RegistrationStatus.Invited);
        directory.HasAcceptedInvitationAsync("guest-id", Arg.Any<CancellationToken>()).Returns(false);

        var result = await Provision(registration.Id);

        Assert.Equal(new ProvisionDeveloperUseCase.Response(RegistrationStatus.Invited, Changed: false), result.Value);
        await directory.DidNotReceiveWithAnyArgs().ActivateGuestAsync(default!, default);
        await fake.Db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_activation_is_recorded_and_the_registration_stays_invited()
    {
        var registration = Seed(RegistrationStatus.Invited);
        directory.HasAcceptedInvitationAsync("guest-id", Arg.Any<CancellationToken>()).Returns(true);
        directory.ActivateGuestAsync("guest-id", Arg.Any<CancellationToken>())
            .ThrowsAsync(new UserManagementException("Group not found."));

        var result = await Provision(registration.Id);

        Assert.Equal(RegistrationStatus.Invited, result.Value.Status);
        Assert.Equal("Group not found.", registration.ProvisioningError);
        await fake.Db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(RegistrationStatus.Pending)]
    [InlineData(RegistrationStatus.Activated)]
    public async Task A_registration_with_no_step_left_is_left_alone(RegistrationStatus status)
    {
        var registration = Seed(status);

        var result = await Provision(registration.Id);

        Assert.Equal(new ProvisionDeveloperUseCase.Response(status, Changed: false), result.Value);
        Assert.Empty(directory.ReceivedCalls());
        await fake.Db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_registration_is_not_found()
    {
        var error = Assert.Single((await Provision(Guid.NewGuid())).Errors);

        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Empty(directory.ReceivedCalls());
    }
}
