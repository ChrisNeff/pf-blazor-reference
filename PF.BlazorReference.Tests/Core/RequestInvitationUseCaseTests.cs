using ErrorOr;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Features.Invitations;
using PF.BlazorReference.Tests.Support;

namespace PF.BlazorReference.Tests.Core;

/// <summary>Unit tests: the data port and the queue are substitutes, so no database or worker is involved.</summary>
public class RequestInvitationUseCaseTests
{
    private readonly FakeConsentIdentityDb fake = new();
    private readonly IInvitationQueue queue = Substitute.For<IInvitationQueue>();

    public RequestInvitationUseCaseTests() => queue.TryEnqueue(Arg.Any<Guid>()).Returns(true);

    private Task<ErrorOr<RequestInvitationUseCase.Response>> Request(Guid id) =>
        new RequestInvitationUseCase(fake.Factory, queue)
            .Handle(new RequestInvitationUseCase.Request(id), CancellationToken.None);

    [Fact]
    public async Task Records_the_request_on_a_submitted_row_then_signals_the_worker()
    {
        var registration = FakeConsentIdentityDb.Registration("Ada", "ada@example.com");
        fake.Registrations.Add(registration);

        var result = await Request(registration.Id);

        Assert.False(result.IsError);
        Assert.Equal(RegistrationStatus.ProvisioningRequested, registration.Status);
        Received.InOrder(() =>
        {
            fake.Db.SaveChangesAsync(Arg.Any<CancellationToken>());
            queue.TryEnqueue(registration.Id);
        });
    }

    [Fact]
    public async Task A_full_queue_still_succeeds_because_the_row_holds_the_request()
    {
        var registration = FakeConsentIdentityDb.Registration("Ada", "ada@example.com");
        fake.Registrations.Add(registration);
        queue.TryEnqueue(Arg.Any<Guid>()).Returns(false);

        Assert.False((await Request(registration.Id)).IsError);
    }

    [Fact]
    public async Task An_unknown_registration_is_not_found_and_nothing_is_queued()
    {
        var error = Assert.Single((await Request(Guid.NewGuid())).Errors);

        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal(RequestInvitationUseCase.RegistrationIdField, error.Code);
        queue.DidNotReceiveWithAnyArgs().TryEnqueue(default);
    }

    [Fact]
    public async Task A_second_request_is_a_conflict_and_saves_nothing()
    {
        var registration = FakeConsentIdentityDb.Registration("Ada", "ada@example.com");
        registration.RequestProvisioning();
        fake.Registrations.Add(registration);

        var error = Assert.Single((await Request(registration.Id)).Errors);

        Assert.Equal(DeveloperRegistration.StatusField, error.Code);
        await fake.Db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        queue.DidNotReceiveWithAnyArgs().TryEnqueue(default);
    }

    [Fact]
    public async Task A_row_changed_by_someone_else_meanwhile_is_a_conflict_and_nothing_is_queued()
    {
        var registration = FakeConsentIdentityDb.Registration("Ada", "ada@example.com");
        fake.Registrations.Add(registration);
        fake.Db.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException(new InvalidOperationException("simulated")));

        var error = Assert.Single((await Request(registration.Id)).Errors);

        Assert.Equal(ErrorType.Conflict, error.Type);
        queue.DidNotReceiveWithAnyArgs().TryEnqueue(default);
    }
}
