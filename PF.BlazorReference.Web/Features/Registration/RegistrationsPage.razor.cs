using Microsoft.AspNetCore.Components;
using MudBlazor;
using PF.BlazorReference.Core.Features.Invitations;
using PF.BlazorReference.Core.Features.Registration;
using PF.BlazorReference.Web.Features.Registration.ViewModels;

namespace PF.BlazorReference.Web.Features.Registration;

/// <summary>
/// Container: calls the use cases, maps their results to <see cref="RegistrationRow"/> view models for the
/// presentation table, shows the toast after a redirect from the register page, and requests Entra guest
/// invitations for Submitted (or failed) rows. The invitation worker sends it in the
/// background, so while any row is still being sent the page re-reads the list every
/// <see cref="PollInterval"/> until each has been invited or has failed, for at most
/// <see cref="PollFor"/> (a lost signal waits for the worker's sweep). Redemption (Invited to Activated)
/// can take days and is found by the acceptance check, so it is not polled; the refresh button shows it.
/// </summary>
public partial class RegistrationsPage : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollFor = TimeSpan.FromMinutes(2);

    private sealed record RegistrationsState(IReadOnlyList<RegistrationRow> Rows, bool Loading, string? Error);

    private RegistrationsState state = new([], Loading: true, Error: null);
    private Guid? highlightId;
    private Guid? currentRecordProcessingId;
    private readonly CancellationTokenSource disposed = new();
    private Task polling = Task.CompletedTask;

    // The page is public until authentication arrives, so inviting guests into a real tenant from it
    // is limited to Development. Replace this with an authorization policy then.
    private bool CanInvite => Environment.IsDevelopment();

    [Inject] private ListRegistrationsUseCase ListRegistrations { get; set; } = default!;
    [Inject] private RequestInvitationUseCase RequestInvitation { get; set; } = default!;
    [Inject] private IWebHostEnvironment Environment { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ILogger<RegistrationsPage> Logger { get; set; } = default!;

    [Parameter, SupplyParameterFromQuery(Name = "registered")]
    public Guid? Registered { get; set; }

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        state = state with { Loading = true };
        var result = await ListRegistrations.Handle(new ListRegistrationsUseCase.Request(), CancellationToken.None);
        state = result.IsError
            ? new([], Loading: false, Error: "Registrations could not be loaded.")
            : new(RegistrationRow.From(result.Value.Items), Loading: false, Error: null);
        PollWhileInvitationsAreBeingSent();
    }

    /// <summary>Re-reads the rows without the loading state, so the table (and its search box) stays put.</summary>
    private async Task<bool> RefreshRowsAsync(CancellationToken ct)
    {
        var result = await ListRegistrations.Handle(new ListRegistrationsUseCase.Request(), ct);
        if (result.IsError)
            return false;

        state = state with { Rows = RegistrationRow.From(result.Value.Items) };
        return true;
    }

    protected override void OnAfterRender(bool firstRender)
    {
        // Not in OnInitialized: a snackbar added before the first render on the circuit is lost.
        if (!firstRender || Registered is not { } id)
            return;

        highlightId = id;
        var name = state.Rows.FirstOrDefault(r => r.Id == id)?.Name;
        Snackbar.Add(name is null ? "Registration saved." : $"Registered {name}.", Severity.Success);

        // Drop the query string so a refresh does not repeat the toast.
        Navigation.NavigateTo("/registrations", replace: true);
        StateHasChanged();
    }

    private async Task RequestInvitationAsync(Guid registrationId)
    {
        if (!CanInvite)
            return;

        currentRecordProcessingId = registrationId;
        try
        {
            var result = await RequestInvitation.Handle(
                new RequestInvitationUseCase.Request(registrationId), CancellationToken.None);
            if (result.IsError)
            {
                Snackbar.Add(result.FirstError.Description, Severity.Warning);
                return;
            }

            await RefreshRowsAsync(CancellationToken.None);
            Snackbar.Add("Invitation requested. It is sent in the background, and the guest is activated once they redeem it.", Severity.Info);
            PollWhileInvitationsAreBeingSent();
        }
        finally
        {
            currentRecordProcessingId = null;
        }
    }

    private bool AnyBeingSent => state.Rows.Any(r => r.IsSendingInvitation);

    private void PollWhileInvitationsAreBeingSent()
    {
        if (polling.IsCompleted && AnyBeingSent)
            polling = PollAsync(disposed.Token);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);
        var ticksLeft = (int)(PollFor / PollInterval);
        try
        {
            while (ticksLeft-- > 0 && await timer.WaitForNextTickAsync(ct))
            {
                if (!await RefreshRowsAsync(ct))
                    continue;

                await InvokeAsync(StateHasChanged);
                if (!AnyBeingSent)
                    return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Logger.LogInformation("Stopped refreshing the registrations list: the page was closed.");
        }
        catch (Exception)
        {
            // Polling is a convenience: stop quietly and leave the refresh button to the user.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await disposed.CancelAsync();
        await polling;
        disposed.Dispose();
    }
}
