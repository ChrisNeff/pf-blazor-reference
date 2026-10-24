using ErrorOr;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Extensions;

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

    /// <param name="Sent">False when there was nothing to do: not requested any more, or claimed by another host.</param>
    /// <param name="Status">The status afterwards, when <paramref name="Sent"/>.</param>
    public sealed record Response(bool Sent, RegistrationStatus? Status);

    private static readonly SendInvitationUseCase.Response Skipped = new(Sent: false, Status: null);

    public async Task<ErrorOr<SendInvitationUseCase.Response>> Handle(
        SendInvitationUseCase.Request request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateAsync(ct);

        var registration = await db.ClaimAsync(request.RegistrationId, RegistrationStatus.ProvisioningRequested,
            request.ClaimOwner, timeProvider.GetUtcNow().UtcDateTime, ClaimLease, ct);
        if (registration is null)
            return Skipped;

        try
        {
            registration.MarkInvited(await userManagement.InviteGuestAsync(registration, ct));
        }
        catch (UserManagementException ex)
        {
            // An expected failure: recorded on the row so it can be seen and requested again.
            registration.MarkProvisioningFailed(ex.Message);
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

        return new SendInvitationUseCase.Response(Sent: true, registration.Status);
    }
}
