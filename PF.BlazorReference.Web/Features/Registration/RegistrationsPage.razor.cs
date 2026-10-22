using Microsoft.AspNetCore.Components;
using MudBlazor;
using PF.BlazorReference.Core.Features.Registration;

namespace PF.BlazorReference.Web.Features.Registration;

/// <summary>Container: loads the list and shows the toast after a redirect from the register page.</summary>
public partial class RegistrationsPage
{
    private sealed record RegistrationsState(IReadOnlyList<ListRegistrationsUseCase.Item> Items, bool Loading, string? Error);

    private RegistrationsState state = new([], Loading: true, Error: null);
    private Guid? highlightId;

    [Inject] private ListRegistrationsUseCase ListRegistrations { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Parameter, SupplyParameterFromQuery(Name = "registered")]
    public Guid? Registered { get; set; }

    protected override async Task OnInitializedAsync()
    {
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
}
