using ErrorOr;
using NSubstitute;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Features.Provisioning;
using PF.BlazorReference.Tests.Support;

namespace PF.BlazorReference.Tests.Core;

/// <summary>Unit tests: the data port and the queue are substitutes, so no database or worker is involved.</summary>
public class RequestEntraProvisioningUseCaseTests
{
    private readonly FakeConsentIdentityDb fake = new();
    private readonly IProvisioningQueue queue = Substitute.For<IProvisioningQueue>();

    public RequestEntraProvisioningUseCaseTests() => queue.TryEnqueue(Arg.Any<Guid>()).Returns(true);

    private Task<ErrorOr<RequestEntraProvisioningUseCase.Response>> Request(Guid id) =>
        new RequestEntraProvisioningUseCase(fake.Factory, queue)
            .Handle(new RequestEntraProvisioningUseCase.Request(id), CancellationToken.None);

    [Fact]
    public async Task Records_the_request_on_the_row_then_signals_the_worker()
    {
        var registration = FakeConsentIdentityDb.Registration("Ada", "ada@example.com");
        fake.Registrations.Add(registration);

        var result = await Request(registration.Id);

        Assert.False(result.IsError);
        Assert.Equal(RegistrationStatus.ProvisioningRequested, result.Value.Status);
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

        var result = await Request(registration.Id);

        Assert.False(result.IsError);
        Assert.Equal(RegistrationStatus.ProvisioningRequested, registration.Status);
    }

    [Fact]
    public async Task An_unknown_registration_is_not_found_and_nothing_is_queued()
    {
        var error = Assert.Single((await Request(Guid.NewGuid())).Errors);

        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal(RequestEntraProvisioningUseCase.RegistrationIdField, error.Code);
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
}
