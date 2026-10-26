using Microsoft.Extensions.Options;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Features.Invitations;
using PF.BlazorReference.Core.Ports;

namespace PF.BlazorReference.Web.BackgroundServices.Invitations;

/// <summary>
/// Sends Entra guest invitations. Reads the queue the grid's button fills and runs
/// <see cref="SendInvitationUseCase"/> for each id, one at a time. Its periodic run is
/// <see cref="RequeueInvitationRequestsUseCase"/>, which recovers lost signals. On several hosts each
/// runs one; the use case's row claim keeps them apart.
/// </summary>
public sealed class InvitationWorker(
    IInvitationQueue queue,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<InvitationWorkerOptions> options,
    ILogger<InvitationWorker> logger)
    : PeriodicWorker(TimeSpan.FromSeconds(options.Value.SweepIntervalSeconds), scopeFactory, timeProvider, logger)
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(base.ExecuteAsync(stoppingToken), ReadQueueAsync(stoppingToken));

    protected override async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        await InScopeAsync<RequeueInvitationRequestsUseCase>(async requeue =>
        {
            var result = await requeue.Handle(new RequeueInvitationRequestsUseCase.Request(), stoppingToken);
            if (!result.IsError && result.Value.Refused > 0)
                Logger.LogWarning("The invitation queue is full; {Count} requests wait for the next sweep.", result.Value.Refused);
        }, "requeueing requested invitations", stoppingToken);
    }

    private async Task ReadQueueAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var id in queue.ReadAllAsync(stoppingToken))
            {
                await InScopeAsync<SendInvitationUseCase>(async send =>
                {
                    var result = await send.Handle(new SendInvitationUseCase.Request(id, ClaimOwner.Current), stoppingToken);
                    if (result.IsError)
                        return;
                    switch (result.Value.Outcome)
                    {
                        case SendInvitationUseCase.Outcome.Invited:
                            Logger.LogInformation("Registration {RegistrationId} was invited.", id);
                            break;
                        case SendInvitationUseCase.Outcome.Failed:
                            Logger.LogWarning("Inviting registration {RegistrationId} failed; the reason is on the row and the grid offers a retry.", id);
                            break;
                    }
                }, $"sending the invitation for registration {id}", stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Logger.LogInformation(
                "{Worker} stopped reading the invitation queue: the host is shutting down. Unsent requests stay on their rows for the next sweep, here or on another host.",
                nameof(InvitationWorker));
        }
    }
}
