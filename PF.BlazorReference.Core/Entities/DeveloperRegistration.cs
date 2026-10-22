using System.Net.Mail;
using ErrorOr;

namespace PF.BlazorReference.Core.Entities;

/// <summary>
/// Always-valid aggregate: the only way to get one is <see cref="Create"/>, and state with rules
/// has no public setter. Error codes are the field names ("Name", "Email") so the UI can place them.
/// </summary>
public sealed class DeveloperRegistration : IAuditable
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 254;

    public const string NameField = nameof(Name);
    public const string EmailField = nameof(Email);

    // For EF Core only.
    private DeveloperRegistration() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";
    public RegistrationStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastUpdatedUtc { get; private set; }

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
            Status = RegistrationStatus.Pending,
        };
    }

    // MailAddress accepts display names ("A <a@b.c>") and quoted forms; requiring the parsed address
    // to equal the input rejects those, and requiring a dot in the domain rejects "a@b".
    private static bool IsEmailShaped(string email) =>
        MailAddress.TryCreate(email, out var parsed)
        && parsed.Address == email
        && parsed.Host.Contains('.');
}
