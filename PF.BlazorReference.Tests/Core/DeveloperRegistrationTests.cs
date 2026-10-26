using ErrorOr;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Tests.Core;

public class DeveloperRegistrationTests
{
    [Fact]
    public void Create_with_valid_input_returns_a_submitted_registration_with_normalised_values()
    {
        var result = DeveloperRegistration.Create("  Ada Lovelace  ", "  Ada.Lovelace@Example.COM ");

        Assert.False(result.IsError);
        Assert.Equal("Ada Lovelace", result.Value.Name);
        Assert.Equal("ada.lovelace@example.com", result.Value.Email);
        Assert.Equal(RegistrationStatus.Submitted, result.Value.Status);
        Assert.NotEqual(default, result.Value.Status); // default is Invalid: a created registration is never unset
        Assert.NotEqual(Guid.Empty, result.Value.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_a_name(string? name)
    {
        var result = DeveloperRegistration.Create(name, "ada@example.com");

        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(DeveloperRegistration.NameField, error.Code);
    }

    [Fact]
    public void Create_rejects_a_name_over_the_maximum_length()
    {
        var result = DeveloperRegistration.Create(new string('a', DeveloperRegistration.NameMaxLength + 1), "ada@example.com");

        Assert.Equal(DeveloperRegistration.NameField, Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing-domain@")]
    [InlineData("no-dot@localhost")]
    [InlineData("Display Name <ada@example.com>")]
    public void Create_rejects_a_missing_or_malformed_email(string? email)
    {
        var result = DeveloperRegistration.Create("Ada", email);

        var error = Assert.Single(result.Errors);
        Assert.Equal(DeveloperRegistration.EmailField, error.Code);
    }

    [Fact]
    public void Create_reports_every_invalid_field_at_once()
    {
        var result = DeveloperRegistration.Create("", "nope");

        Assert.Equal(
            new[] { DeveloperRegistration.NameField, DeveloperRegistration.EmailField }.Order(),
            result.Errors.Select(e => e.Code).Order());
    }

    private static DeveloperRegistration Requested()
    {
        var registration = DeveloperRegistration.Create("Ada", "ada@example.com").Value;
        Assert.False(registration.RequestProvisioning().IsError);
        return registration;
    }

    private static DeveloperRegistration Invited()
    {
        var registration = Requested();
        registration.MarkInvited("guest-id");
        return registration;
    }

    [Fact]
    public void Provisioning_can_be_requested_for_a_submitted_registration()
    {
        var registration = DeveloperRegistration.Create("Ada", "ada@example.com").Value;

        var result = registration.RequestProvisioning();

        Assert.False(result.IsError);
        Assert.Equal(RegistrationStatus.ProvisioningRequested, registration.Status);
    }

    [Fact]
    public void Provisioning_can_be_requested_again_after_a_failure_and_the_old_error_is_cleared()
    {
        var registration = Requested();
        registration.MarkProvisioningFailed("Graph was down.");

        var result = registration.RequestProvisioning();

        Assert.False(result.IsError);
        Assert.Equal(RegistrationStatus.ProvisioningRequested, registration.Status);
        Assert.Null(registration.ProvisioningError);
        Assert.Equal(1, registration.ProvisioningAttempts);
    }

    [Fact]
    public void Provisioning_cannot_be_requested_twice()
    {
        var registration = Requested();

        var error = Assert.Single(registration.RequestProvisioning().Errors);

        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal(DeveloperRegistration.StatusField, error.Code);
    }

    [Fact]
    public void Provisioning_cannot_be_requested_once_invited()
    {
        var registration = Invited();

        Assert.True(registration.RequestProvisioning().IsError);
        Assert.Equal(RegistrationStatus.Invited, registration.Status);
    }

    [Fact]
    public void Marking_invited_records_the_guest_object_id_and_the_attempt()
    {
        var registration = Invited();

        Assert.Equal(RegistrationStatus.Invited, registration.Status);
        Assert.Equal("guest-id", registration.EntraObjectId);
        Assert.Equal(1, registration.ProvisioningAttempts);
    }

    [Fact]
    public void Marking_failed_keeps_a_reason_truncated_to_the_column()
    {
        var registration = Requested();

        registration.MarkProvisioningFailed(new string('x', DeveloperRegistration.ProvisioningErrorMaxLength + 50));

        Assert.Equal(RegistrationStatus.ProvisioningFailed, registration.Status);
        Assert.Equal(DeveloperRegistration.ProvisioningErrorMaxLength, registration.ProvisioningError!.Length);
        Assert.Equal(1, registration.ProvisioningAttempts);
    }

    [Fact]
    public void An_invited_registration_is_activated_and_a_previous_activation_error_is_cleared()
    {
        var registration = Invited();
        registration.RecordActivationFailure("Group not found.");

        registration.MarkActivated();

        Assert.Equal(RegistrationStatus.Activated, registration.Status);
        Assert.Null(registration.ProvisioningError);
        Assert.Equal("guest-id", registration.EntraObjectId);
    }

    [Fact]
    public void A_failed_activation_keeps_the_registration_invited_for_the_next_attempt()
    {
        var registration = Invited();

        registration.RecordActivationFailure("Group not found.");

        Assert.Equal(RegistrationStatus.Invited, registration.Status);
        Assert.Equal("Group not found.", registration.ProvisioningError);
        Assert.Equal(2, registration.ProvisioningAttempts);
    }

    [Fact]
    public void A_worker_outcome_on_a_registration_in_the_wrong_status_is_a_bug()
    {
        var submitted = DeveloperRegistration.Create("Ada", "ada@example.com").Value;
        var requested = Requested();

        Assert.Throws<InvalidOperationException>(() => submitted.MarkInvited("guest-id"));
        Assert.Throws<InvalidOperationException>(() => submitted.MarkProvisioningFailed("reason"));
        Assert.Throws<InvalidOperationException>(() => requested.MarkActivated());
        Assert.Throws<InvalidOperationException>(() => requested.RecordActivationFailure("reason"));
    }

    [Theory]
    [InlineData(RegistrationStatus.Submitted, true)]
    [InlineData(RegistrationStatus.ProvisioningFailed, true)]
    [InlineData(RegistrationStatus.ProvisioningRequested, false)]
    [InlineData(RegistrationStatus.Invited, false)]
    [InlineData(RegistrationStatus.Activated, false)]
    [InlineData(RegistrationStatus.Invalid, false)]
    public void CanRequestProvisioning_answers_for_each_status(RegistrationStatus status, bool expected) =>
        Assert.Equal(expected, DeveloperRegistration.CanRequestProvisioning(status));

    private static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    [Fact]
    public void A_claim_is_refused_to_another_owner_while_its_lease_is_live()
    {
        var registration = Requested();
        Assert.True(registration.TryClaim("host-a", Now, Lease));

        Assert.False(registration.TryClaim("host-b", Now.AddMinutes(4), Lease));
        Assert.Equal("host-a", registration.ClaimedBy);
    }

    [Fact]
    public void A_lapsed_claim_can_be_taken_by_another_owner()
    {
        var registration = Requested();
        registration.TryClaim("host-a", Now, Lease);

        Assert.True(registration.TryClaim("host-b", Now + Lease, Lease));
        Assert.Equal("host-b", registration.ClaimedBy);
        Assert.Equal(Now + Lease + Lease, registration.ClaimedUntilUtc);
    }

    [Fact]
    public void The_owner_can_renew_its_own_claim()
    {
        var registration = Requested();
        registration.TryClaim("host-a", Now, Lease);

        Assert.True(registration.TryClaim("host-a", Now.AddMinutes(1), Lease));
    }

    [Fact]
    public void Every_worker_outcome_releases_the_claim()
    {
        var invited = Requested();
        invited.TryClaim("host-a", Now, Lease);
        invited.MarkInvited("guest-id");

        var failed = Requested();
        failed.TryClaim("host-a", Now, Lease);
        failed.MarkProvisioningFailed("reason");

        var activated = Invited();
        activated.TryClaim("host-a", Now, Lease);
        activated.MarkActivated();

        var activationFailed = Invited();
        activationFailed.TryClaim("host-a", Now, Lease);
        activationFailed.RecordActivationFailure("reason");

        Assert.All(new[] { invited, failed, activated, activationFailed }, r =>
        {
            Assert.Null(r.ClaimedBy);
            Assert.Null(r.ClaimedUntilUtc);
        });
    }
}
