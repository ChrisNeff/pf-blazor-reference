using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Features.Invitations;
using PF.BlazorReference.Tests.Support;
using PF.BlazorReference.Web.BackgroundServices.AcceptanceCheck;

namespace PF.BlazorReference.Tests.Web.BackgroundServices;

/// <summary>
/// The scheduled acceptance check with the real use cases over the substitute data port and directory.
/// <see cref="FakeTimeProvider"/> drives the schedule.
/// </summary>
public sealed class AcceptanceCheckWorkerTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly FakeConsentIdentityDb fake = new();
    private readonly IUserManagementService directory = Substitute.For<IUserManagementService>();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly AcceptanceCheckOptions options = new();
    private readonly ServiceProvider services;
    private readonly AcceptanceCheckWorker worker;
    private readonly HashSet<string> accepted = [];
    private readonly CapturingLogger<AcceptanceCheckWorker> logger = new();

    public AcceptanceCheckWorkerTests()
    {
        directory.HasAcceptedInvitationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => accepted.Contains(call.Arg<string>()));

        services = new ServiceCollection()
            .AddSingleton(fake.Factory)
            .AddSingleton(directory)
            .AddSingleton<TimeProvider>(time)
            .AddTransient<ActivateAcceptedGuestUseCase>()
            .AddTransient<ListInvitedRegistrationsUseCase>()
            .BuildServiceProvider();

        worker = new AcceptanceCheckWorker(services.GetRequiredService<IServiceScopeFactory>(), time,
            Options.Create(options), logger);
    }

    public async ValueTask DisposeAsync()
    {
        await worker.StopAsync(CancellationToken.None);
        worker.Dispose();
        await services.DisposeAsync();
    }

    private DeveloperRegistration SeedInvited(string email, string guestId)
    {
        var registration = FakeConsentIdentityDb.Registration("Ada", email);
        registration.RequestProvisioning();
        registration.MarkInvited(guestId);
        fake.Registrations.Add(registration);
        return registration;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!condition())
            await Task.Delay(10, cts.Token);
    }

    private Task FirstCheckRan() =>
        WaitFor(() => directory.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IUserManagementService.HasAcceptedInvitationAsync)));

    [Fact]
    public async Task A_guest_who_redeemed_is_activated_at_startup()
    {
        var registration = SeedInvited("ada@example.com", "guest-1");
        accepted.Add("guest-1");

        await worker.StartAsync(CancellationToken.None);

        await WaitFor(() => registration.Status == RegistrationStatus.Activated);
    }

    [Fact]
    public async Task A_redemption_is_noticed_on_the_next_scheduled_check()
    {
        var registration = SeedInvited("ada@example.com", "guest-1");
        await worker.StartAsync(CancellationToken.None);
        await FirstCheckRan();
        Assert.Equal(RegistrationStatus.Invited, registration.Status);

        accepted.Add("guest-1"); // the invitee enters the one-time code; Entra tells nobody
        time.Advance(TimeSpan.FromSeconds(options.IntervalSeconds));

        await WaitFor(() => registration.Status == RegistrationStatus.Activated);
    }

    [Fact]
    public async Task Stopping_is_logged_at_information()
    {
        await worker.StartAsync(CancellationToken.None);
        await WaitFor(() => fake.Factory.ReceivedCalls().Any()); // the startup check listed an empty table

        await worker.StopAsync(CancellationToken.None);

        Assert.True(logger.Logged(LogLevel.Information, "AcceptanceCheckWorker stopped: the host is shutting down"));
    }

    [Fact]
    public async Task Work_interrupted_by_shutdown_is_named_at_information()
    {
        var registration = SeedInvited("ada@example.com", "guest-1");
        var checking = new TaskCompletionSource();
        directory.HasAcceptedInvitationAsync("guest-1", Arg.Any<CancellationToken>()).Returns(async call =>
        {
            checking.SetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, call.Arg<CancellationToken>()); // Entra never answers
            return false;
        });
        await worker.StartAsync(CancellationToken.None);
        await checking.Task.WaitAsync(Timeout);

        await worker.StopAsync(CancellationToken.None);

        Assert.True(logger.Logged(LogLevel.Information, $"cancelled checking registration {registration.Id} for acceptance"));
        Assert.True(logger.Logged(LogLevel.Information, "AcceptanceCheckWorker stopped"));
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning); // a shutdown is not a failure
    }

    [Fact]
    public async Task One_failing_row_does_not_stop_the_check_for_the_others()
    {
        var broken = SeedInvited("broken@example.com", "guest-broken");
        var healthy = SeedInvited("healthy@example.com", "guest-healthy");
        accepted.UnionWith(["guest-broken", "guest-healthy"]);
        directory.ActivateGuestAsync("guest-broken", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException());

        await worker.StartAsync(CancellationToken.None);

        await WaitFor(() => healthy.Status == RegistrationStatus.Activated);
        Assert.Equal(RegistrationStatus.Invited, broken.Status);
    }
}
