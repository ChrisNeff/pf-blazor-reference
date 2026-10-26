using NSubstitute;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Features.Invitations;
using PF.BlazorReference.Tests.Support;
using PF.BlazorReference.Core.Ports;

namespace PF.BlazorReference.Tests.Core;

/// <summary>Unit tests: the data port and the queue are substitutes.</summary>
public class RequeueInvitationRequestsUseCaseTests
{
    private readonly FakeConsentIdentityDb fake = new();
    private readonly IInvitationQueue queue = Substitute.For<IInvitationQueue>();

    private async Task<RequeueInvitationRequestsUseCase.Response> Requeue() =>
        (await new RequeueInvitationRequestsUseCase(fake.Factory, queue)
            .Handle(new RequeueInvitationRequestsUseCase.Request(), CancellationToken.None)).Value;

    [Fact]
    public async Task Queues_only_the_requested_registrations()
    {
        var submitted = FakeConsentIdentityDb.Registration("Submitted", "submitted@example.com");
        var requested = FakeConsentIdentityDb.Registration("Requested", "requested@example.com");
        requested.RequestProvisioning();
        var invited = FakeConsentIdentityDb.Registration("Invited", "invited@example.com");
        invited.RequestProvisioning();
        invited.MarkInvited("guest-1");
        fake.Registrations.AddRange([submitted, requested, invited]);
        queue.TryEnqueue(Arg.Any<Guid>()).Returns(true);

        Assert.Equal(new RequeueInvitationRequestsUseCase.Response(Queued: 1, Refused: 0), await Requeue());

        queue.Received(1).TryEnqueue(requested.Id);
        queue.ReceivedWithAnyArgs(1).TryEnqueue(default);
    }

    [Fact]
    public async Task Counts_what_a_full_queue_refused()
    {
        foreach (var email in new[] { "a@example.com", "b@example.com", "c@example.com" })
        {
            var registration = FakeConsentIdentityDb.Registration("Ada", email);
            registration.RequestProvisioning();
            fake.Registrations.Add(registration);
        }
        queue.TryEnqueue(Arg.Any<Guid>()).Returns(true, false, false);

        Assert.Equal(new RequeueInvitationRequestsUseCase.Response(Queued: 1, Refused: 2), await Requeue());
    }
}
