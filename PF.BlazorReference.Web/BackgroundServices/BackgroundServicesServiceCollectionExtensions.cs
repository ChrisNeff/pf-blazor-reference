using PF.BlazorReference.Web.BackgroundServices.AcceptanceCheck;
using PF.BlazorReference.Web.BackgroundServices.Invitations;

namespace PF.BlazorReference.Web.BackgroundServices;

public static class BackgroundServicesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the workers and their options. One folder per worker under BackgroundServices/, each a
    /// <see cref="BackgroundService"/> named *Worker that calls Core use cases; an architecture test
    /// enforces the layout. The queue they read is registered by AddInfrastructure().
    /// </summary>
    public static IServiceCollection AddBackgroundServices(this IServiceCollection services)
    {
        services.AddOptions<InvitationWorkerOptions>().BindConfiguration(InvitationWorkerOptions.SectionName);
        services.AddHostedService<InvitationWorker>();

        services.AddOptions<AcceptanceCheckOptions>().BindConfiguration(AcceptanceCheckOptions.SectionName);
        services.AddHostedService<AcceptanceCheckWorker>();

        return services;
    }
}
