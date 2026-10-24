using ErrorOr;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Extensions;

namespace PF.BlazorReference.Core.Features.Registration;

/// <summary>
/// Registers a developer: records the row as Submitted, and nothing else. Entra is not involved; the
/// invitation is requested later from the registrations grid (<see cref="Invitations.RequestInvitationUseCase"/>).
/// </summary>
public sealed class RegisterDeveloperUseCase(IConsentIdentityDbFactory dbFactory)
    : IUseCase<RegisterDeveloperUseCase.Request, ErrorOr<RegisterDeveloperUseCase.Response>>
{
    /// <summary>The longest name the domain accepts, for callers that validate early (the web form's annotations).</summary>
    public const int NameMaxLength = DeveloperRegistration.NameMaxLength;

    /// <summary>The longest email the domain accepts.</summary>
    public const int EmailMaxLength = DeveloperRegistration.EmailMaxLength;

    public sealed record Request(string? Name, string? Email);

    public sealed record Response(Guid RegistrationId);

    private static readonly Error DuplicateEmail =
        Error.Conflict(DeveloperRegistration.EmailField, "A registration with this email already exists.");

    public async Task<ErrorOr<RegisterDeveloperUseCase.Response>> Handle(
        RegisterDeveloperUseCase.Request request, CancellationToken ct)
    {
        // The aggregate guards its own invariants, so an invalid request never reaches the database.
        var created = DeveloperRegistration.Create(request.Name, request.Email);
        if (created.IsError)
            return created.Errors;
        var registration = created.Value;

        await using var db = await dbFactory.CreateAsync(ct);

        // Friendly pre-check. Two concurrent submits can both pass it, so the unique index in
        // schema.sql is the real guarantee and the save below maps its failure to the same error.
        if (await db.IsEmailRegisteredAsync(registration.Email, ct))
            return DuplicateEmail;

        db.DeveloperRegistrations.Add(registration);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            return DuplicateEmail;
        }

        return new RegisterDeveloperUseCase.Response(registration.Id);
    }
}
