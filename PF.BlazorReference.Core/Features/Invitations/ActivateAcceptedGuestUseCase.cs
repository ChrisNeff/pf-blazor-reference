using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Extensions;
using PF.BlazorReference.Core.Ports.Data;
using PF.BlazorReference.Core.Ports.Directory;

namespace PF.BlazorReference.Core.Features.Invitations;

/// <summary>
/// For one Invited registration: asks Entra whether the invitation was redeemed (the one-time code step,
/// which proves the email) and, if so, gives the guest access. Run by the acceptance check worker on its
/// schedule, because Entra does not call back. The check is a read, so the row is claimed only when
/// there is something to write; several hosts may check the same row, but only one activates it.
/// </summary>
public sealed class ActivateAcceptedGuestUseCase(
    IConsentIdentityDbFactory dbFactory, IUserManagementService userManagement, TimeProvider timeProvider)
    : IUseCase<ActivateAcceptedGuestUseCase.Request, ErrorOr<ActivateAcceptedGuestUseCase.Response>>
{
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);

    public sealed record Request(Guid RegistrationId, string ClaimOwner);

    public enum Outcome
    {
        /// <summary>The default value: never returned.</summary>
        Invalid = 0,

        /// <summary>Not Invited any more, or claimed by another host.</summary>
        Skipped,

        /// <summary>The invitation is not redeemed yet. Nothing was written.</summary>
        NotAccepted,

        Activated,

        /// <summary>Redeemed, but giving access failed; the reason is on the row and the next check retries.</summary>
        ActivationFailed,
    }

    public sealed record Response(ActivateAcceptedGuestUseCase.Outcome Outcome);

    public async Task<ErrorOr<ActivateAcceptedGuestUseCase.Response>> Handle(
        ActivateAcceptedGuestUseCase.Request request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateAsync(ct);

        var invited = await db.DeveloperRegistrations.AsNoTracking()
            .Where(r => r.Id == request.RegistrationId && r.Status == RegistrationStatus.Invited)
            .Select(r => new { r.EntraObjectId })
            .FirstOrDefaultAsync(ct);
        if (invited?.EntraObjectId is not { } objectId)
            return new ActivateAcceptedGuestUseCase.Response(ActivateAcceptedGuestUseCase.Outcome.Skipped);

        try
        {
            if (!await userManagement.HasAcceptedInvitationAsync(objectId, ct))
                return new ActivateAcceptedGuestUseCase.Response(ActivateAcceptedGuestUseCase.Outcome.NotAccepted);
        }
        catch (UserManagementException)
        {
            // Nothing to record yet; the next scheduled check asks again.
            return new ActivateAcceptedGuestUseCase.Response(ActivateAcceptedGuestUseCase.Outcome.NotAccepted);
        }

        var registration = await db.ClaimAsync(request.RegistrationId, RegistrationStatus.Invited,
            request.ClaimOwner, timeProvider.GetUtcNow().UtcDateTime, ClaimLease, ct);
        if (registration is null)
            return new ActivateAcceptedGuestUseCase.Response(ActivateAcceptedGuestUseCase.Outcome.Skipped);

        ActivateAcceptedGuestUseCase.Outcome outcome;
        try
        {
            await userManagement.ActivateGuestAsync(objectId, ct);
            registration.MarkActivated();
            outcome = ActivateAcceptedGuestUseCase.Outcome.Activated;
        }
        catch (UserManagementException ex)
        {
            registration.RecordActivationFailure(ex.Message);
            outcome = ActivateAcceptedGuestUseCase.Outcome.ActivationFailed;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            // Another host took the row after this claim lapsed; activation is repeatable, so it is safe.
            return new ActivateAcceptedGuestUseCase.Response(ActivateAcceptedGuestUseCase.Outcome.Skipped);
        }

        return new ActivateAcceptedGuestUseCase.Response(outcome);
    }
}
