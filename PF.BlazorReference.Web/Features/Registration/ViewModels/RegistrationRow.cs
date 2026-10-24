using MudBlazor;
using PF.BlazorReference.Core.Features.Registration;

namespace PF.BlazorReference.Web.Features.Registration.ViewModels;

/// <summary>
/// One row of the registrations table, already decided: every label, color, hint and enabled state the
/// table shows. The container builds it from the use case's contract with <see cref="From"/>; the
/// presentation component only binds to it, so it knows nothing of Core. Wording and colors are
/// presentation decisions and live here; whether an invitation can be requested is the domain's,
/// and arrives from the use case.
/// </summary>
public sealed record RegistrationRow(
    Guid Id,
    string Name,
    string Email,
    DateTime CreatedAtUtc,
    string StatusLabel,
    Color StatusColor,
    string? StatusHint,
    bool CanInvite,
    string InviteLabel,
    bool IsSendingInvitation)
{
    public static RegistrationRow From(ListRegistrationsUseCase.Item item) => new(
        item.Id,
        item.Name,
        item.Email,
        item.CreatedAtUtc,
        StatusLabel: item.Status switch
        {
            ListRegistrationsUseCase.Status.InvitationRequested => "Sending invite",
            ListRegistrationsUseCase.Status.InvitationFailed => "Failed",
            _ => item.Status.ToString(),
        },
        StatusColor: item.Status switch
        {
            ListRegistrationsUseCase.Status.InvitationRequested => Color.Info,
            ListRegistrationsUseCase.Status.Invited => Color.Primary,
            ListRegistrationsUseCase.Status.Activated => Color.Success,
            ListRegistrationsUseCase.Status.InvitationFailed => Color.Error,
            _ => Color.Default,
        },
        StatusHint: item.Status switch
        {
            // An activation failure keeps the row Invited, so the reason can show there too.
            ListRegistrationsUseCase.Status.InvitationFailed or ListRegistrationsUseCase.Status.Invited
                when item.ProvisioningError is not null => item.ProvisioningError,
            ListRegistrationsUseCase.Status.Invited => "Invitation sent. Waiting for the invitee to redeem it.",
            ListRegistrationsUseCase.Status.InvitationRequested => "The invitation is being sent in the background.",
            _ => null,
        },
        CanInvite: item.CanRequestInvitation,
        InviteLabel: item.Status == ListRegistrationsUseCase.Status.InvitationFailed ? "Retry invite" : "Invite",
        IsSendingInvitation: item.Status == ListRegistrationsUseCase.Status.InvitationRequested);

    public static RegistrationRow[] From(IEnumerable<ListRegistrationsUseCase.Item> items) => items.Select(From).ToArray();
}
