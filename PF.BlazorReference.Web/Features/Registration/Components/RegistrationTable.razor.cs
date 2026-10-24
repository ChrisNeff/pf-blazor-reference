using Microsoft.AspNetCore.Components;
using PF.BlazorReference.Web.Features.Registration.ViewModels;

namespace PF.BlazorReference.Web.Features.Registration.Components;

/// <summary>Presentation: the registrations grid. Parameters in, <see cref="OnInvite"/> out, nothing injected.</summary>
public partial class RegistrationTable
{
    private string? search;

    [Parameter, EditorRequired] public IReadOnlyList<RegistrationRow> Rows { get; set; } = [];

    /// <summary>The row to highlight, such as the one just registered.</summary>
    [Parameter] public Guid? HighlightId { get; set; }

    /// <summary>Whether to show the invite column at all.</summary>
    [Parameter] public bool ShowInvite { get; set; }

    /// <summary>The row whose request is in flight; its button is disabled.</summary>
    [Parameter] public Guid? CurrentRecordProcessingId { get; set; }

    /// <summary>Raised with the registration id when its Entra invitation is requested.</summary>
    [Parameter] public EventCallback<Guid> OnInvite { get; set; }

    private bool MatchesSearch(RegistrationRow row) =>
        string.IsNullOrWhiteSpace(search)
        || row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || row.Email.Contains(search, StringComparison.OrdinalIgnoreCase);

    private string RowClass(RegistrationRow row, int index) => row.Id == HighlightId ? "pf-row-highlight" : "";
}
