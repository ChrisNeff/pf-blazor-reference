using ErrorOr;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Extensions;
using PF.BlazorReference.Core.Ports.Data;
using PF.BlazorReference.Core.Ports.Directory;

namespace PF.BlazorReference.Core.Features.Invitations;

/// <summary>
/// Sends the Entra guest invitation for one requested registration. Run by the invitation worker.
/// The row is claimed (and the claim saved) before Entra is called, so when the app runs on several
/// hosts only one of them sends it. Outcome: Invited, or ProvisioningFailed with the reason.
/// </summary>
public sealed class SendInvitationUseCase(
    IConsentIdentityDbFactory dbFactory, IUserManagementService userManagement, TimeProvider timeProvider)
    : IUseCase<SendInvitationUseCase.Request, ErrorOr<SendInvitationUseCase.Response>>
{
    /// <summary>Longer than an invitation call takes. A host that dies holding a claim loses it after this.</summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);

    /// <param name="ClaimOwner">Who is working the row: the host and process, for the claim.</param>
    public sealed record Request(Guid RegistrationId, string ClaimOwner);

    public enum Outcome
    {
        /// <summary>The default value: never returned.</summary>
        Invalid = 0,

        /// <summary>Not requested any more, or claimed by another host.</summary>
        Skipped,

        Invited,

        /// <summary>Entra refused or failed; the reason is on the row and the grid offers a retry.</summary>
        Failed,
    }

    public sealed record Response(SendInvitationUseCase.Outcome Outcome);

    private static readonly SendInvitationUseCase.Response Skipped = new(SendInvitationUseCase.Outcome.Skipped);

    public async Task<ErrorOr<SendInvitationUseCase.Response>> Handle(
        SendInvitationUseCase.Request request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateAsync(ct);

        var registration = await db.ClaimAsync(request.RegistrationId, RegistrationStatus.ProvisioningRequested,
            request.ClaimOwner, timeProvider.GetUtcNow().UtcDateTime, ClaimLease, ct);
        if (registration is null)
            return Skipped;

        SendInvitationUseCase.Outcome outcome;
        try
        {
            registration.MarkInvited(await userManagement.InviteGuestAsync(registration, ct));
            outcome = SendInvitationUseCase.Outcome.Invited;
        }
        catch (UserManagementException ex)
        {
            // An expected failure: recorded on the row so it can be seen and requested again.
            registration.MarkProvisioningFailed(ex.Message);
            outcome = SendInvitationUseCase.Outcome.Failed;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            // The lease ran out and another host took the row. It finds the guest this call created
            // (the adapter looks the email up first), so nothing is sent twice.
            return Skipped;
        }

        return new SendInvitationUseCase.Response(outcome);
    }
}
