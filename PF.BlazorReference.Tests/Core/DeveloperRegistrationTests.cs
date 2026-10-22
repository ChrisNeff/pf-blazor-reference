using ErrorOr;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Tests.Core;

public class DeveloperRegistrationTests
{
    [Fact]
    public void Create_with_valid_input_returns_a_pending_registration_with_normalised_values()
    {
        var result = DeveloperRegistration.Create("  Ada Lovelace  ", "  Ada.Lovelace@Example.COM ");

        Assert.False(result.IsError);
        Assert.Equal("Ada Lovelace", result.Value.Name);
        Assert.Equal("ada.lovelace@example.com", result.Value.Email);
        Assert.Equal(RegistrationStatus.Pending, result.Value.Status);
        Assert.NotEqual(default, result.Value.Status); // default is Invalid: a created registration is never unset
        Assert.NotEqual(Guid.Empty, result.Value.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_a_name(string? name)
    {
        var result = DeveloperRegistration.Create(name, "ada@example.com");

        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(DeveloperRegistration.NameField, error.Code);
    }

    [Fact]
    public void Create_rejects_a_name_over_the_maximum_length()
    {
        var result = DeveloperRegistration.Create(new string('a', DeveloperRegistration.NameMaxLength + 1), "ada@example.com");

        Assert.Equal(DeveloperRegistration.NameField, Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing-domain@")]
    [InlineData("no-dot@localhost")]
    [InlineData("Display Name <ada@example.com>")]
    public void Create_rejects_a_missing_or_malformed_email(string? email)
    {
        var result = DeveloperRegistration.Create("Ada", email);

        var error = Assert.Single(result.Errors);
        Assert.Equal(DeveloperRegistration.EmailField, error.Code);
    }

    [Fact]
    public void Create_reports_every_invalid_field_at_once()
    {
        var result = DeveloperRegistration.Create("", "nope");

        Assert.Equal(
            new[] { DeveloperRegistration.NameField, DeveloperRegistration.EmailField }.Order(),
            result.Errors.Select(e => e.Code).Order());
    }
}
