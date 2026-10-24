using Microsoft.Extensions.Options;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Features.Invitations;

namespace PF.BlazorReference.Web.BackgroundServices.AcceptanceCheck;

/// <summary>
/// Activates guests who redeemed their invitation (the one-time code step, which proves the email).
/// Each run lists the Invited registrations and runs <see cref="ActivateAcceptedGuestUseCase"/> on each,
/// in its own scope, so one failing row cannot stop the rest. Polling, because Entra does not call back.
/// On several hosts each runs one; the use case claims a row before writing.
/// </summary>
public sealed class AcceptanceCheckWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<AcceptanceCheckOptions> options,
    ILogger<AcceptanceCheckWorker> logger)
    : PeriodicWorker(TimeSpan.FromSeconds(options.Value.IntervalSeconds), scopeFactory, timeProvider, logger)
{
    protected override async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        IReadOnlyList<Guid> invited = [];
        await InScopeAsync<ListRegistrationIdsUseCase>(async list =>
        {
            var result = await list.Handle(new ListRegistrationIdsUseCase.Request(RegistrationStatus.Invited), stoppingToken);
            if (!result.IsError)
                invited = result.Value.RegistrationIds;
        }, "listing invited registrations", stoppingToken);

        foreach (var id in invited)
        {
            await InScopeAsync<ActivateAcceptedGuestUseCase>(async activate =>
            {
                var result = await activate.Handle(new ActivateAcceptedGuestUseCase.Request(id, ClaimOwner.Current), stoppingToken);
                if (result.IsError)
                    return;

                switch (result.Value.Outcome)
                {
                    case ActivateAcceptedGuestUseCase.Outcome.Activated:
                        Logger.LogInformation("Registration {RegistrationId} redeemed its invitation and is now Activated.", id);
                        break;
                    case ActivateAcceptedGuestUseCase.Outcome.ActivationFailed:
                        Logger.LogWarning("Registration {RegistrationId} redeemed its invitation, but activating it failed; the next check retries.", id);
                        break;
                }
            }, $"checking registration {id} for acceptance", stoppingToken);
        }
    }
}
