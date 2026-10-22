using ErrorOr;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Features.Registration;
using PF.BlazorReference.Tests.Support;

namespace PF.BlazorReference.Tests.Core;

/// <summary>Unit tests: the data port and its factory are substitutes, so no database is involved.</summary>
public class RegisterDeveloperUseCaseTests
{
    private readonly FakeConsentIdentityDb fake = new();

    private Task<ErrorOr<RegisterDeveloperUseCase.Response>> Register(string? name, string? email, CancellationToken ct = default) =>
        new RegisterDeveloperUseCase(fake.Factory).Handle(new RegisterDeveloperUseCase.Request(name, email), ct);

    [Fact]
    public async Task A_valid_request_adds_one_normalised_registration_and_saves_it()
    {
        var result = await Register("  Ada Lovelace ", "Ada@Example.COM");

        Assert.False(result.IsError);
        var added = Assert.Single(fake.Registrations);
        Assert.Equal(result.Value.RegistrationId, added.Id);
        Assert.Equal("Ada Lovelace", added.Name);
        Assert.Equal("ada@example.com", added.Email);
        Assert.Equal(RegistrationStatus.Pending, added.Status);
        await fake.Db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_invalid_request_returns_validation_errors_without_touching_the_database()
    {
        var result = await Register("", "nope");

        Assert.True(result.IsError);
        Assert.All(result.Errors, e => Assert.Equal(ErrorType.Validation, e.Type));
        await fake.Factory.DidNotReceive().CreateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_duplicate_email_is_a_conflict_even_when_the_case_differs_and_nothing_is_added()
    {
        fake.Registrations.Add(FakeConsentIdentityDb.Registration("Ada", "ada@example.com"));

        var result = await Register("Someone Else", "ADA@Example.com");

        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal(DeveloperRegistration.EmailField, error.Code);
        fake.Set.DidNotReceive().Add(Arg.Any<DeveloperRegistration>());
        await fake.Db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_race_that_passes_the_pre_check_is_still_reported_as_the_same_conflict()
    {
        // The pre-check sees an empty table, then the save is rejected by the unique index.
        fake.Db.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new UniqueConstraintViolationException(new InvalidOperationException("simulated race")));

        var result = await Register("Ada", "ada@example.com");

        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal(DeveloperRegistration.EmailField, error.Code);
        Assert.Empty(fake.Registrations);
    }

    [Fact]
    public async Task An_unexpected_failure_is_not_swallowed()
    {
        fake.Db.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Register("Ada", "ada@example.com"));
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_factory_and_the_save()
    {
        using var source = new CancellationTokenSource();

        await Register("Ada", "ada@example.com", source.Token);

        await fake.Factory.Received(1).CreateAsync(source.Token);
        await fake.Db.Received(1).SaveChangesAsync(source.Token);
    }

    [Fact]
    public async Task The_context_is_disposed_after_each_call()
    {
        await Register("Ada", "ada@example.com");

        await fake.Db.Received(1).DisposeAsync();
    }
}
