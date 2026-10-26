using Microsoft.AspNetCore.Components;

namespace PF.BlazorReference.Web.Features.Registration.Components;

/// <summary>Presentation: the registration form. Parameters in, <see cref="OnValidSubmit"/> out, nothing injected.</summary>
public partial class RegistrationForm
{
    /// <summary>The form's name, shared with the container's [SupplyParameterFromForm].</summary>
    public const string FormName = "register";

    /// <summary>Posted field names start with this (the <see cref="Model"/> parameter name); the container binds with it.</summary>
    public const string FieldPrefix = nameof(Model);

    [Parameter, EditorRequired] public RegisterDeveloperForm Model { get; set; } = default!;
    [Parameter] public IReadOnlyDictionary<string, string[]> ServerErrors { get; set; } = new Dictionary<string, string[]>();
    [Parameter] public EventCallback OnValidSubmit { get; set; }

    private string[] ServerMessages(string field) =>
        ServerErrors.TryGetValue(field, out var messages) ? messages : [];
}
