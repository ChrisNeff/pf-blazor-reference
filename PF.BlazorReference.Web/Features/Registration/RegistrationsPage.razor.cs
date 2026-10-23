using Microsoft.AspNetCore.Components;
using MudBlazor;
using PF.BlazorReference.Core.Features.Provisioning;
using PF.BlazorReference.Core.Features.Registration;

namespace PF.BlazorReference.Web.Features.Registration;

/// <summary>
/// Container: loads the list, shows the toast after a redirect from the register page, and requests
/// Entra guest invitations. The worker sends the invitation and, once it is redeemed, activates the guest,
/// so a row moves through ProvisioningRequested, Invited and Activated as the list is refreshed.
/// </summary>
public partial class RegistrationsPage
{
    private sealed record RegistrationsState(IReadOnlyList<ListRegistrationsUseCase.Item> Items, bool Loading, string? Error);

    private RegistrationsState state = new([], Loading: true, Error: null);
    private Guid? highlightId;
    private Guid? provisioningId;

    // The page is public until authentication arrives, so inviting guests into a real tenant from it
    // is limited to Development. Replace this with an authorization policy then.
    private bool CanProvision => Environment.IsDevelopment();

    [Inject] private ListRegistrationsUseCase ListRegistrations { get; set; } = default!;
    [Inject] private RequestEntraProvisioningUseCase RequestEntraProvisioning { get; set; } = default!;
    [Inject] private IWebHostEnvironment Environment { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Parameter, SupplyParameterFromQuery(Name = "registered")]
    public Guid? Registered { get; set; }

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        state = state with { Loading = true };
        var result = await ListRegistrations.Handle(new ListRegistrationsUseCase.Request(), CancellationToken.None);
        state = result.IsError
            ? new([], Loading: false, Error: "Registrations could not be loaded.")
            : new(result.Value.Items, Loading: false, Error: null);
    }

    protected override void OnAfterRender(bool firstRender)
    {
        // Not in OnInitialized: a snackbar added before the first render on the circuit is lost.
        if (!firstRender || Registered is not { } id)
            return;

        highlightId = id;
        var name = state.Items.FirstOrDefault(i => i.Id == id)?.Name;
        Snackbar.Add(name is null ? "Registration saved." : $"Registered {name}.", Severity.Success);

        // Drop the query string so a refresh does not repeat the toast.
        Navigation.NavigateTo("/registrations", replace: true);
        StateHasChanged();
    }

    private async Task RequestProvisioningAsync(Guid registrationId)
    {
        if (!CanProvision)
            return;

        provisioningId = registrationId;
        try
        {
            var result = await RequestEntraProvisioning.Handle(
                new RequestEntraProvisioningUseCase.Request(registrationId), CancellationToken.None);
            if (result.IsError)
            {
                Snackbar.Add(result.FirstError.Description, Severity.Warning);
                return;
            }

            state = state with
            {
                Items = state.Items.Select(i => i.Id == registrationId ? i with { Status = result.Value.Status } : i).ToList(),
            };
            Snackbar.Add("Invitation requested. It is sent in the background, and the guest is activated once they redeem it; refresh to see progress.", Severity.Info);
        }
        finally
        {
            provisioningId = null;
        }
    }
}
