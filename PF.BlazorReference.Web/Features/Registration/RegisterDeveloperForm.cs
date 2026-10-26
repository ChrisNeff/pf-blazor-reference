using System.ComponentModel.DataAnnotations;
using PF.BlazorReference.Core.Features.Registration;

namespace PF.BlazorReference.Web.Features.Registration;

/// <summary>
/// The submitted form, typed. The annotations produce field messages on submit; the same rules are
/// enforced again by the domain behind <see cref="RegisterDeveloperUseCase"/>, which is the last line of defence.
/// Shared length constants keep the two from drifting. Settable properties because EditForm binds to them.
/// </summary>
public sealed record RegisterDeveloperForm
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(RegisterDeveloperUseCase.NameMaxLength, ErrorMessage = "Name must be {1} characters or fewer.")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Email is required.")]
    [StringLength(RegisterDeveloperUseCase.EmailMaxLength, ErrorMessage = "Email must be {1} characters or fewer.")]
    [EmailAddress(ErrorMessage = "Email is not a valid address.")]
    public string? Email { get; set; }
}
