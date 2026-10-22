using System.ComponentModel.DataAnnotations;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Web.Features.Registration;

/// <summary>
/// The submitted form, typed. The annotations produce field messages on submit; the same rules are
/// enforced again by <see cref="DeveloperRegistration.Create"/>, which is the last line of defence.
/// Shared length constants keep the two from drifting. Settable properties because EditForm binds to them.
/// </summary>
public sealed record RegisterDeveloperForm
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(DeveloperRegistration.NameMaxLength, ErrorMessage = "Name must be {1} characters or fewer.")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Email is required.")]
    [StringLength(DeveloperRegistration.EmailMaxLength, ErrorMessage = "Email must be {1} characters or fewer.")]
    [EmailAddress(ErrorMessage = "Email is not a valid address.")]
    public string? Email { get; set; }
}
