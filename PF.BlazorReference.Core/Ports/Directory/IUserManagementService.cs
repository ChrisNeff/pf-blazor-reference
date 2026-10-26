using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Core.Ports.Directory;

/// <summary>
/// Port to the user directory (Entra ID, as B2B guests), implemented in Infrastructure. Core never
/// sees the directory's SDK or its exception types. Every method is safe to call again after a crash.
/// </summary>
public interface IUserManagementService
{
    /// <summary>
    /// Invites the registrant as a guest, which sends the invitation email, or returns the guest
    /// already invited for this email without sending another.
    /// </summary>
    /// <returns>The directory's object id for the guest account.</returns>
    /// <exception cref="UserManagementException">The directory rejected or failed the request.</exception>
    Task<string> InviteGuestAsync(DeveloperRegistration registration, CancellationToken ct);

    /// <summary>Whether the guest has redeemed the invitation, which proves they control the email.</summary>
    /// <exception cref="UserManagementException">The directory rejected or failed the request.</exception>
    Task<bool> HasAcceptedInvitationAsync(string entraObjectId, CancellationToken ct);

    /// <summary>Gives the guest access (the activated group). Doing it twice is not an error.</summary>
    /// <exception cref="UserManagementException">The directory rejected or failed the request.</exception>
    Task ActivateGuestAsync(string entraObjectId, CancellationToken ct);
}
