using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Core.Features.Provisioning;

/// <summary>
/// Moves one registration a step along the Entra guest flow. Run by the background worker:
/// <list type="bullet">
/// <item>ProvisioningRequested: sends the invitation (Invited), or records the failure (ProvisioningFailed).</item>
/// <item>Invited: if the invitee has redeemed it, which proves the email, gives the guest access (Activated).
/// Not redeemed yet, nothing changes; a failed activation is recorded and the row stays Invited.</item>
/// </list>
/// Any other status is left alone, so running it twice for the same registration is safe.
/// </summary>
public sealed class ProvisionDeveloperUseCase(IConsentIdentityDbFactory dbFactory, IUserManagementService userManagement)
    : IUseCase<ProvisionDeveloperUseCase.Request, ErrorOr<ProvisionDeveloperUseCase.Response>>
{
    public const string RegistrationIdField = nameof(ProvisionDeveloperUseCase.Request.RegistrationId);

    public sealed record Request(Guid RegistrationId);

    /// <param name="Status">The registration's status afterwards.</param>
    /// <param name="Changed">False when there was nothing to do (not redeemed yet, or no step applies).</param>
    public sealed record Response(RegistrationStatus Status, bool Changed);

    public async Task<ErrorOr<ProvisionDeveloperUseCase.Response>> Handle(
        ProvisionDeveloperUseCase.Request request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateAsync(ct);

        var registration = await db.DeveloperRegistrations.FirstOrDefaultAsync(r => r.Id == request.RegistrationId, ct);
        if (registration is null)
            return Error.NotFound(RegistrationIdField, "The registration no longer exists.");

        var changed = registration.Status switch
        {
            RegistrationStatus.ProvisioningRequested => await InviteAsync(registration, ct),
            RegistrationStatus.Invited => await ActivateIfAcceptedAsync(registration, ct),
            _ => false,
        };

        if (changed)
            await db.SaveChangesAsync(ct);
        return new ProvisionDeveloperUseCase.Response(registration.Status, changed);
    }

    private async Task<bool> InviteAsync(DeveloperRegistration registration, CancellationToken ct)
    {
        try
        {
            registration.MarkInvited(await userManagement.InviteGuestAsync(registration, ct));
        }
        catch (UserManagementException ex)
        {
            // An expected failure: recorded on the row so it can be seen and requested again.
            registration.MarkProvisioningFailed(ex.Message);
        }
        return true;
    }

    private async Task<bool> ActivateIfAcceptedAsync(DeveloperRegistration registration, CancellationToken ct)
    {
        var objectId = registration.EntraObjectId!; // an Invited registration always has one
        try
        {
            if (!await userManagement.HasAcceptedInvitationAsync(objectId, ct))
                return false;

            await userManagement.ActivateGuestAsync(objectId, ct);
            registration.MarkActivated();
        }
        catch (UserManagementException ex)
        {
            registration.RecordActivationFailure(ex.Message);
        }
        return true;
    }
}
