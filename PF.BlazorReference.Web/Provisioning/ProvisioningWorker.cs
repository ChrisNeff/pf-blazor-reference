using PF.BlazorReference.Core.Features.Provisioning;

namespace PF.BlazorReference.Web.Provisioning;

/// <summary>
/// Sends Entra guest invitations and activates redeemed guests in the background. Thin on purpose: it
/// only moves ids from the queue to <see cref="ProvisionDeveloperUseCase"/>, one at a time. It sweeps the
/// table at startup and every <see cref="SweepInterval"/>, which recovers lost signals and is also how a
/// redemption is noticed (Entra does not call back). It logs; the use cases do not.
/// One instance per app: running several would need the row claimed before it is processed.
/// </summary>
public sealed class ProvisioningWorker(
    ProvisioningQueue queue,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ProvisioningWorker> logger) : BackgroundService
{
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromMinutes(5);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(SweepAsync(stoppingToken), ProcessAsync(stoppingToken));

    private async Task ProcessAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var id in queue.Reader.ReadAllAsync(stoppingToken))
                await ProvisionAsync(id, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Unprocessed requests stay on their rows for the next start's sweep.
        }
    }

    private async Task ProvisionAsync(Guid registrationId, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var provision = scope.ServiceProvider.GetRequiredService<ProvisionDeveloperUseCase>();
            var result = await provision.Handle(new ProvisionDeveloperUseCase.Request(registrationId), stoppingToken);

            if (result.IsError)
                logger.LogWarning("Registration {RegistrationId} was not provisioned: {Error}", registrationId, result.FirstError.Description);
            else if (result.Value.Changed)
                logger.LogInformation("Registration {RegistrationId} is now {Status}.", registrationId, result.Value.Status);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // An unhandled exception would stop the host. The row stays requested, so a sweep retries it.
            logger.LogError(ex, "Provisioning registration {RegistrationId} failed unexpectedly.", registrationId);
        }
    }

    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, timeProvider);
        try
        {
            do
            {
                await EnqueueBacklogAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task EnqueueBacklogAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var backlog = scope.ServiceProvider.GetRequiredService<ListProvisioningBacklogUseCase>();
            var result = await backlog.Handle(new ListProvisioningBacklogUseCase.Request(), stoppingToken);
            if (result.IsError)
                return;

            // An id already in the queue may be enqueued twice; the use case skips the second.
            var refused = result.Value.RegistrationIds.Count(id => !queue.TryEnqueue(id));
            if (refused > 0)
                logger.LogWarning("The provisioning queue is full; {Count} requests wait for the next sweep.", refused);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sweeping the provisioning backlog failed.");
        }
    }
}
