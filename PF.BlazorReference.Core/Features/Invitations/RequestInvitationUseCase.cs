using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Core.Features.Invitations;

/// <summary>
/// The grid's button: asks for an Entra guest invitation for a Submitted (or failed) registration.
/// Records the request on the row first, which is what makes it durable, then wakes the invitation
/// worker. The invitation itself is sent by <see cref="SendInvitationUseCase"/> in the background.
/// </summary>
public sealed class RequestInvitationUseCase(IConsentIdentityDbFactory dbFactory, IInvitationQueue queue)
    : IUseCase<RequestInvitationUseCase.Request, ErrorOr<RequestInvitationUseCase.Response>>
{
    public const string RegistrationIdField = nameof(RequestInvitationUseCase.Request.RegistrationId);

    public sealed record Request(Guid RegistrationId);

    public sealed record Response;

    public async Task<ErrorOr<RequestInvitationUseCase.Response>> Handle(
        RequestInvitationUseCase.Request request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateAsync(ct);

        var registration = await db.DeveloperRegistrations.FirstOrDefaultAsync(r => r.Id == request.RegistrationId, ct);
        if (registration is null)
            return Error.NotFound(RegistrationIdField, "The registration no longer exists.");

        var requested = registration.RequestProvisioning();
        if (requested.IsError)
            return requested.Errors;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            // Someone else (another user, or a worker on another host) changed the row since it was read.
            return Error.Conflict(DeveloperRegistration.StatusField, "The registration was changed by someone else. Refresh and try again.");
        }

        // After the save, so the worker never reads the row before the request is on it. A full queue
        // is not a failure: the worker's sweep finds the row.
        queue.TryEnqueue(registration.Id);

        return new RequestInvitationUseCase.Response();
    }
}
