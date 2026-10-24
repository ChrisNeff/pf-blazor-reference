using System.Net.Mail;
using ErrorOr;

namespace PF.BlazorReference.Core.Entities;

/// <summary>
/// Always-valid aggregate: the only way to get one is <see cref="Create"/>, and state with rules
/// has no public setter. Error codes are the field names ("Name", "Email") so the UI can place them.
/// </summary>
public sealed class DeveloperRegistration : IAuditable, IVersioned
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 254;

    public const int ProvisioningErrorMaxLength = 500;
    public const int ClaimedByMaxLength = 200;

    public const string NameField = nameof(Name);
    public const string EmailField = nameof(Email);
    public const string StatusField = nameof(Status);

    // For EF Core only.
    private DeveloperRegistration() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";
    public RegistrationStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastUpdatedUtc { get; private set; }

    /// <summary>The Entra object id of the guest account, once the invitation is sent.</summary>
    public string? EntraObjectId { get; private set; }

    /// <summary>Why the last directory call failed; cleared when the next step succeeds or provisioning is requested again.</summary>
    public string? ProvisioningError { get; private set; }

    /// <summary>How many directory calls the worker has made for this registration that changed or failed something.</summary>
    public int ProvisioningAttempts { get; private set; }

    /// <summary>The concurrency token, incremented on every save by the version interceptor.</summary>
    public int Version { get; private set; }

    /// <summary>The worker (host and process) that holds the row while it calls Entra, if any.</summary>
    public string? ClaimedBy { get; private set; }

    /// <summary>When the claim lapses, so a row held by a crashed host is picked up again.</summary>
    public DateTime? ClaimedUntilUtc { get; private set; }

    /// <summary>
    /// Takes the row for one worker before it calls Entra. Refused while another owner's claim is still
    /// live. The claim only counts once saved: two hosts that both take it in memory race on
    /// <see cref="Version"/>, and the second save is rejected.
    /// </summary>
    public bool TryClaim(string owner, DateTime nowUtc, TimeSpan lease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (ClaimedBy is not null && ClaimedBy != owner && ClaimedUntilUtc > nowUtc)
            return false;

        ClaimedBy = owner.Length > ClaimedByMaxLength ? owner[..ClaimedByMaxLength] : owner;
        ClaimedUntilUtc = nowUtc + lease;
        return true;
    }

    /// <summary>Gives the row back. Every worker outcome below releases it too.</summary>
    public void ReleaseClaim()
    {
        ClaimedBy = null;
        ClaimedUntilUtc = null;
    }

    /// <summary>Whether an Entra invitation can be requested from this status. The UI asks the same question.</summary>
    public static bool CanRequestProvisioning(RegistrationStatus status) =>
        status is RegistrationStatus.Submitted or RegistrationStatus.ProvisioningFailed;

    /// <summary>Asks for an Entra guest invitation. Allowed from Submitted, and from ProvisioningFailed to retry.</summary>
    public ErrorOr<Success> RequestProvisioning()
    {
        if (!CanRequestProvisioning(Status))
            return Error.Conflict(StatusField, $"An Entra invitation cannot be requested while the registration is {Status}.");

        Status = RegistrationStatus.ProvisioningRequested;
        ProvisioningError = null;
        return Result.Success;
    }

    /// <summary>Records the sent invitation and the guest account it created. Only on a requested registration.</summary>
    public void MarkInvited(string entraObjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entraObjectId);
        EnsureStatus(RegistrationStatus.ProvisioningRequested);

        Status = RegistrationStatus.Invited;
        EntraObjectId = entraObjectId;
        ProvisioningError = null;
        ProvisioningAttempts++;
        ReleaseClaim();
    }

    /// <summary>Records a failed invitation. The reason is kept, truncated to fit the column.</summary>
    public void MarkProvisioningFailed(string reason)
    {
        EnsureStatus(RegistrationStatus.ProvisioningRequested);

        Status = RegistrationStatus.ProvisioningFailed;
        ProvisioningError = Truncate(reason);
        ProvisioningAttempts++;
        ReleaseClaim();
    }

    /// <summary>Records that the invitation was redeemed and the guest was given access.</summary>
    public void MarkActivated()
    {
        EnsureStatus(RegistrationStatus.Invited);

        Status = RegistrationStatus.Activated;
        ProvisioningError = null;
        ProvisioningAttempts++;
        ReleaseClaim();
    }

    /// <summary>
    /// Records a failed activation. The registration stays Invited, because the invitation is out and
    /// the guest exists; the worker's next sweep tries again.
    /// </summary>
    public void RecordActivationFailure(string reason)
    {
        EnsureStatus(RegistrationStatus.Invited);

        ProvisioningError = Truncate(reason);
        ProvisioningAttempts++;
        ReleaseClaim();
    }

    private static string Truncate(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return reason.Length > ProvisioningErrorMaxLength ? reason[..ProvisioningErrorMaxLength] : reason;
    }

    // A worker outcome on a registration in the wrong status is a bug, not an expected failure.
    private void EnsureStatus(RegistrationStatus expected)
    {
        if (Status != expected)
            throw new InvalidOperationException($"Expected the registration to be {expected}, but it is {Status}.");
    }

    /// <summary>The comparison form of an email: trimmed and lowercase. Stored this way, so the unique index compares it.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static ErrorOr<DeveloperRegistration> Create(string? name, string? email)
    {
        var errors = new List<Error>();

        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
            errors.Add(Error.Validation(NameField, "Name is required."));
        else if (trimmedName.Length > NameMaxLength)
            errors.Add(Error.Validation(NameField, $"Name must be {NameMaxLength} characters or fewer."));

        var normalizedEmail = email is null ? null : NormalizeEmail(email);
        if (string.IsNullOrEmpty(normalizedEmail))
            errors.Add(Error.Validation(EmailField, "Email is required."));
        else if (normalizedEmail.Length > EmailMaxLength)
            errors.Add(Error.Validation(EmailField, $"Email must be {EmailMaxLength} characters or fewer."));
        else if (!IsEmailShaped(normalizedEmail))
            errors.Add(Error.Validation(EmailField, "Email is not a valid address."));

        if (errors.Count > 0)
            return errors;

        return new DeveloperRegistration
        {
            Id = Guid.NewGuid(),
            Name = trimmedName!,
            Email = normalizedEmail!,
            Status = RegistrationStatus.Submitted,
        };
    }

    // MailAddress accepts display names ("A <a@b.c>") and quoted forms; requiring the parsed address
    // to equal the input rejects those, and requiring a dot in the domain rejects "a@b".
    private static bool IsEmailShaped(string email) =>
        MailAddress.TryCreate(email, out var parsed)
        && parsed.Address == email
        && parsed.Host.Contains('.');
}
