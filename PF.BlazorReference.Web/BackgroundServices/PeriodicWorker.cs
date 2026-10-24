namespace PF.BlazorReference.Web.BackgroundServices;

/// <summary>
/// The hosting a worker needs and nothing else: run <see cref="RunOnceAsync"/> at startup and then every
/// interval, give each unit of work its own DI scope, and log failures instead of throwing them, because
/// an unhandled exception in a <see cref="BackgroundService"/> stops the host. What a run does is a Core
/// use case's decision; a worker only decides when to call one and what to log.
/// A cancellation is never swallowed silently: it is logged at Information, then the worker stops.
/// </summary>
public abstract class PeriodicWorker(
    TimeSpan interval, IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger logger) : BackgroundService
{
    protected ILogger Logger { get; } = logger;

    /// <summary>One scheduled run. Use <see cref="InScopeAsync{TUseCase}"/> for each unit of work.</summary>
    protected abstract Task RunOnceAsync(CancellationToken stoppingToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval, timeProvider);
        try
        {
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Logger.LogInformation(
                "{Worker} stopped: the host is shutting down. Unfinished work stays on its row for the next run, here or on another host.",
                GetType().Name);
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> with a use case resolved from a new scope; the work handles (and logs)
    /// the use case's result itself. A failure is logged with <paramref name="what"/> and swallowed, so
    /// one bad unit never stops the rest of the run.
    /// </summary>
    protected async Task InScopeAsync<TUseCase>(Func<TUseCase, Task> work, string what, CancellationToken stoppingToken)
        where TUseCase : notnull
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await work(scope.ServiceProvider.GetRequiredService<TUseCase>());
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Name the interrupted work here, where it is known; the caller logs the stop.
            Logger.LogInformation("{Worker} cancelled {What}: the host is shutting down.", GetType().Name, what);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{Worker} failed: {What}.", GetType().Name, what);
        }
    }
}
