using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Features.Invitations;
using PF.BlazorReference.Infrastructure.Queues;
using PF.BlazorReference.Tests.Support;
using PF.BlazorReference.Web.BackgroundServices.Invitations;
using PF.BlazorReference.Core.Ports.Directory;
using PF.BlazorReference.Core.Ports;

namespace PF.BlazorReference.Tests.Web.BackgroundServices;

/// <summary>
/// The invitation worker with the real queue and real use cases, over the substitute data port and a
/// substitute directory. Proves the plumbing: queue to use case, the sweeps, and that one failure does
/// not stop the worker.
/// </summary>
public sealed class InvitationWorkerTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly FakeConsentIdentityDb fake = new();
    private readonly IUserManagementService directory = Substitute.For<IUserManagementService>();
    private readonly ChannelInvitationQueue queue = new();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly InvitationWorkerOptions options = new();
    private readonly CapturingLogger<InvitationWorker> logger = new();
    private readonly ServiceProvider services;
    private readonly InvitationWorker worker;

    public InvitationWorkerTests()
    {
        directory.InviteGuestAsync(Arg.Any<DeveloperRegistration>(), Arg.Any<CancellationToken>())
            .Returns(call => $"guest-{call.Arg<DeveloperRegistration>().Id:N}");

        services = new ServiceCollection()
            .AddSingleton(fake.Factory)
            .AddSingleton(directory)
            .AddSingleton<TimeProvider>(time)
            .AddSingleton<IInvitationQueue>(queue)
            .AddTransient<SendInvitationUseCase>()
            .AddTransient<RequeueInvitationRequestsUseCase>()
            .BuildServiceProvider();

        worker = new InvitationWorker(queue, services.GetRequiredService<IServiceScopeFactory>(), time,
            Options.Create(options), logger);
    }

    public async ValueTask DisposeAsync()
    {
        await worker.StopAsync(CancellationToken.None);
        worker.Dispose();
        await services.DisposeAsync();
    }

    private DeveloperRegistration SeedRequested(string email)
    {
        var registration = FakeConsentIdentityDb.Registration("Ada", email);
        registration.RequestProvisioning();
        fake.Registrations.Add(registration);
        return registration;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!condition())
            await Task.Delay(10, cts.Token);
    }

    [Fact]
    public async Task An_enqueued_request_is_invited()
    {
        await worker.StartAsync(CancellationToken.None);
        var registration = SeedRequested("ada@example.com");

        Assert.True(queue.TryEnqueue(registration.Id));

        await WaitFor(() => registration.Status == RegistrationStatus.Invited);
        Assert.Equal($"guest-{registration.Id:N}", registration.EntraObjectId);
    }

    [Fact]
    public async Task Requests_left_on_rows_are_swept_at_startup_without_a_signal()
    {
        var registration = SeedRequested("ada@example.com");

        await worker.StartAsync(CancellationToken.None);

        await WaitFor(() => registration.Status == RegistrationStatus.Invited);
    }

    [Fact]
    public async Task A_submitted_registration_is_never_invited_by_the_sweep()
    {
        var submitted = FakeConsentIdentityDb.Registration("Ada", "ada@example.com");
        fake.Registrations.Add(submitted);
        var requested = SeedRequested("grace@example.com");

        await worker.StartAsync(CancellationToken.None);

        await WaitFor(() => requested.Status == RegistrationStatus.Invited);
        Assert.Equal(RegistrationStatus.Submitted, submitted.Status);
        await directory.DidNotReceive().InviteGuestAsync(submitted, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stopping_logs_both_loops_at_information()
    {
        await worker.StartAsync(CancellationToken.None);
        await WaitFor(() => fake.Factory.ReceivedCalls().Any());

        await worker.StopAsync(CancellationToken.None);

        Assert.True(logger.Logged(LogLevel.Information, "InvitationWorker stopped reading the invitation queue"));
        Assert.True(logger.Logged(LogLevel.Information, "InvitationWorker stopped: the host is shutting down"));
    }

    [Fact]
    public async Task An_unexpected_failure_does_not_stop_the_worker()
    {
        await worker.StartAsync(CancellationToken.None);
        var broken = SeedRequested("broken@example.com");
        var healthy = SeedRequested("healthy@example.com");
        directory.InviteGuestAsync(broken, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException());

        queue.TryEnqueue(broken.Id);
        queue.TryEnqueue(healthy.Id);

        await WaitFor(() => healthy.Status == RegistrationStatus.Invited);
        Assert.Equal(RegistrationStatus.ProvisioningRequested, broken.Status); // left for a later sweep
    }

    [Fact]
    public async Task The_periodic_sweep_retries_a_request_whose_signal_was_lost()
    {
        await worker.StartAsync(CancellationToken.None);
        await WaitFor(() => fake.Factory.ReceivedCalls().Any()); // the startup sweep ran, on an empty table
        var registration = SeedRequested("ada@example.com");     // requested, but never enqueued

        time.Advance(TimeSpan.FromSeconds(options.SweepIntervalSeconds));

        await WaitFor(() => registration.Status == RegistrationStatus.Invited);
    }
}
