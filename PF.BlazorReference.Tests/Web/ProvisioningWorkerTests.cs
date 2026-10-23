using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Features.Provisioning;
using PF.BlazorReference.Tests.Support;
using PF.BlazorReference.Web.Provisioning;

namespace PF.BlazorReference.Tests.Web;

/// <summary>
/// The worker with the real queue and the real use cases, over the substitute data port and a
/// substitute directory. Proves the plumbing: queue to use case, the startup sweep, that one failure
/// does not stop the worker, and that the periodic sweep is what notices a redeemed invitation.
/// </summary>
public sealed class ProvisioningWorkerTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly FakeConsentIdentityDb fake = new();
    private readonly IUserManagementService directory = Substitute.For<IUserManagementService>();
    private readonly ProvisioningQueue queue = new();
    private readonly FakeTimeProvider time = new();
    private readonly ServiceProvider services;
    private readonly ProvisioningWorker worker;

    public ProvisioningWorkerTests()
    {
        directory.InviteGuestAsync(Arg.Any<DeveloperRegistration>(), Arg.Any<CancellationToken>())
            .Returns(call => $"guest-{call.Arg<DeveloperRegistration>().Id:N}");

        services = new ServiceCollection()
            .AddSingleton(fake.Factory)
            .AddSingleton(directory)
            .AddTransient<ProvisionDeveloperUseCase>()
            .AddTransient<ListProvisioningBacklogUseCase>()
            .BuildServiceProvider();

        worker = new ProvisioningWorker(queue, services.GetRequiredService<IServiceScopeFactory>(), time,
            NullLogger<ProvisioningWorker>.Instance);
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
    public async Task An_enqueued_registration_is_invited()
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
    public async Task An_unexpected_failure_does_not_stop_the_worker()
    {
        await worker.StartAsync(CancellationToken.None);
        var broken = SeedRequested("broken@example.com");
        var healthy = SeedRequested("healthy@example.com");
        directory.InviteGuestAsync(broken, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException());

        queue.TryEnqueue(broken.Id);
        queue.TryEnqueue(healthy.Id);

        await WaitFor(() => healthy.Status == RegistrationStatus.Invited);
        Assert.Equal(RegistrationStatus.ProvisioningRequested, broken.Status); // left for the next sweep
    }

    [Fact]
    public async Task The_periodic_sweep_retries_a_request_whose_signal_was_lost()
    {
        await worker.StartAsync(CancellationToken.None);
        await WaitFor(() => fake.Factory.ReceivedCalls().Any()); // the startup sweep ran, on an empty table
        var registration = SeedRequested("ada@example.com");     // requested, but never enqueued

        time.Advance(worker.SweepInterval);

        await WaitFor(() => registration.Status == RegistrationStatus.Invited);
    }

    [Fact]
    public async Task A_redeemed_invitation_is_activated_on_the_next_sweep()
    {
        var accepted = false;
        directory.HasAcceptedInvitationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => accepted);
        await worker.StartAsync(CancellationToken.None);
        var registration = SeedRequested("ada@example.com");
        queue.TryEnqueue(registration.Id);
        await WaitFor(() => registration.Status == RegistrationStatus.Invited);

        accepted = true; // the invitee redeems it; Entra tells nobody
        time.Advance(worker.SweepInterval);

        await WaitFor(() => registration.Status == RegistrationStatus.Activated);
        await directory.Received(1).ActivateGuestAsync($"guest-{registration.Id:N}", Arg.Any<CancellationToken>());
    }
}
