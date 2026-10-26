namespace PF.BlazorReference.Infrastructure.UserManagement;

/// <summary>The <c>Entra</c> configuration section, read in <c>AddInfrastructure</c>. No secrets.</summary>
/// <param name="TenantId">The workforce tenant. Null to let the credential decide.</param>
/// <param name="ManagedIdentityClientId">A user-assigned managed identity. Null for the system-assigned one.</param>
/// <param name="InviteRedirectUrl">Where the invitee lands after redeeming, normally this app.</param>
/// <param name="ActivatedGroupId">The group a guest joins once the invitation is redeemed. Membership is the access.</param>
/// <param name="DryRun">Authenticate and read, but never send an invitation or change a group.</param>
public sealed record GraphUserManagementOptions(
    string? TenantId,
    string? ManagedIdentityClientId,
    string InviteRedirectUrl,
    string ActivatedGroupId,
    bool DryRun);
