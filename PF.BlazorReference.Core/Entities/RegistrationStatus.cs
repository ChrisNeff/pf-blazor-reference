namespace PF.BlazorReference.Core.Entities;

public enum RegistrationStatus
{
    /// <summary>The default value: an unset status. Never assigned by the domain and never persisted.</summary>
    Invalid = 0,
    Pending,

    /// <summary>An Entra guest invitation was asked for; the background worker has not sent it yet.</summary>
    ProvisioningRequested,

    /// <summary>
    /// The invitation was sent and the guest account exists, but it has no access: the invitee has not
    /// redeemed it, so the email is not yet proven.
    /// </summary>
    Invited,

    /// <summary>The invitee redeemed the invitation (proving the email) and the guest was given access.</summary>
    Activated,

    /// <summary>Sending the invitation failed. It can be requested again.</summary>
    ProvisioningFailed,
}
