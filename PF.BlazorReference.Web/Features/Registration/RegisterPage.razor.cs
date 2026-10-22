using Microsoft.AspNetCore.Components;
using PF.BlazorReference.Core.Features.Registration;
using PF.BlazorReference.Web.Features.Registration.Components;

namespace PF.BlazorReference.Web.Features.Registration;

/// <summary>
/// Container: the routable page. Static SSR (no render mode), so the form is a plain HTTP post
/// handled with [SupplyParameterFromForm]. It owns the use-case call; the form below it is presentation only.
/// </summary>
public partial class RegisterPage
{
    private IReadOnlyDictionary<string, string[]> serverErrors = new Dictionary<string, string[]>();

    [Inject] private RegisterDeveloperUseCase RegisterDeveloper { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    // The inputs live in RegistrationForm and are named after its Model parameter ("Model.Name"),
    // so the container binds with that prefix instead of its own property name.
    [SupplyParameterFromForm(FormName = RegistrationForm.FormName, Name = RegistrationForm.FieldPrefix)]
    private RegisterDeveloperForm? Form { get; set; }

    // Not a property initializer: a form post overwrites it, and an initial GET still needs a model.
    protected override void OnInitialized() => Form ??= new();

    private async Task SubmitAsync()
    {
        var result = await RegisterDeveloper.Handle(new RegisterDeveloperUseCase.Request(Form!.Name, Form.Email), CancellationToken.None);

        if (result.IsError)
        {
            // Stay on the form, showing the errors next to their fields.
            serverErrors = FieldErrors.From(result.Errors);
            return;
        }

        // Post-redirect-get: a refresh cannot resubmit, and the list shows the new row.
        Navigation.NavigateTo($"/registrations?registered={result.Value.RegistrationId}");
    }
}
