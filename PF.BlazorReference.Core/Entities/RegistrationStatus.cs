namespace PF.BlazorReference.Core.Entities;

public enum RegistrationStatus
{
    /// <summary>The default value: an unset status. Never assigned by the domain and never persisted.</summary>
    Invalid = 0,

    /// <summary>The form was submitted and the row recorded. Nothing has been sent to Entra.</summary>
    Submitted,

    /// <summary>An Entra guest invitation was asked for from the grid; the invitation worker has not sent it yet.</summary>
    ProvisioningRequested,

    /// <summary>
    /// The invitation was sent and the guest account exists, but it has no access: the invitee has not
    /// redeemed it (the one-time code step), so the email is not yet proven.
    /// </summary>
    Invited,

    /// <summary>The invitee redeemed the invitation, proving the email, and the guest was given access.</summary>
    Activated,

    /// <summary>Sending the invitation failed. It can be requested again from the grid.</summary>
    ProvisioningFailed,
}
